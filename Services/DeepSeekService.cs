using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FitQuest.Api.Models;

namespace FitQuest.Api.Services;

public class DeepSeekService : IAiService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _provider;
    private readonly ILogger<DeepSeekService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public DeepSeekService(HttpClient http, IConfiguration cfg, ILogger<DeepSeekService> logger)
    {
        _http = http;
        _logger = logger;
        _provider = GetConfigValue(cfg, "Provider", "AI_PROVIDER") ?? "DeepSeek";
        _apiKey = GetConfigValue(cfg, "ApiKey", "AI_API_KEY", "DEEPSEEK_API_KEY")
            ?? throw new InvalidOperationException("Ai:ApiKey is not configured.");
        _model = GetConfigValue(cfg, "Model", "AI_MODEL", "DEEPSEEK_MODEL") ?? "deepseek-v4-flash";
    }

    public string Provider => _provider;

    public string Model => _model;

    public async Task<AiPlanResult> GeneratePlanAsync(AiPlanPromptContext context, CancellationToken ct = default)
    {
        var systemPrompt = BuildGenerateSystemPrompt(context.OutputLanguage);
        var userMessage = BuildGenerateUserMessage(context);

        var (promptSnapshot, content) = await CompleteJsonAsync(systemPrompt, userMessage, 0.5, 3200, ct);

        var plan = DeserializePlan(content, "DeepSeek returned invalid plan JSON.");

        try
        {
            return new AiPlanResult(
                NormalizePlan(plan, context.SelectedMuscleGroup, context.DurationMinutes, minExercises: 5, maxExercises: 8),
                promptSnapshot,
                content);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
        {
            throw new AiResponseParseException("DeepSeek returned invalid plan content.", content, ex);
        }
    }

    public async Task<AiPlanResult> AdjustPlanAsync(AiAdjustPromptContext context, CancellationToken ct = default)
    {
        var systemPrompt = BuildAdjustSystemPrompt(context.OutputLanguage);
        var userMessage = BuildAdjustUserMessage(context);

        var (promptSnapshot, content) = await CompleteJsonAsync(systemPrompt, userMessage, 0.5, 3200, ct);

        var plan = DeserializePlan(content, "DeepSeek returned invalid adjusted plan JSON.");

        try
        {
            return new AiPlanResult(
                NormalizePlan(plan, context.CurrentSession.MuscleGroup, context.CurrentSession.DurationMinutes, minExercises: 5, maxExercises: 8),
                promptSnapshot,
                content);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
        {
            throw new AiResponseParseException("DeepSeek returned invalid adjusted plan content.", content, ex);
        }
    }

    private async Task<(string PromptSnapshot, string Content)> CompleteJsonAsync(
        string systemPrompt,
        string userMessage,
        double temperature,
        int maxTokens,
        CancellationToken ct)
    {
        var promptSnapshot = JsonSerializer.Serialize(new
        {
            provider = _provider,
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage },
            },
        }, JsonOpts);

        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userMessage  },
            },
            response_format = new { type = "json_object" },
            temperature,
            max_tokens = maxTokens,
        };

        var response = await PostAsync("/v1/chat/completions", requestBody, ct);
        return (promptSnapshot, ExtractContent(response));
    }

    private static GeneratedPlan DeserializePlan(string content, string errorMessage)
    {
        try
        {
            return JsonSerializer.Deserialize<GeneratedPlan>(content, JsonOpts)
                ?? throw new JsonException("Deserialized plan was null.");
        }
        catch (JsonException ex)
        {
            throw new AiResponseParseException(errorMessage, content, ex);
        }
    }

    // Prompt builders

    private static string BuildGenerateSystemPrompt(string outputLanguage) => $$"""
        你是一位专业、热情、有感染力的 AI 私人教练。你的任务是根据用户训练画像、目标肌群、训练时长和最近训练记录，生成今日训练计划。
        {{BuildCoachTonePrompt(outputLanguage)}}

        你必须只返回一个合法 JSON object。不要返回 Markdown，不要返回解释文字，不要使用代码块。
        输出语言必须是：{{DescribeLanguage(outputLanguage)}}。
        不允许中英混杂。dayType、aiNote、exerciseName、rationale 必须全部使用目标语言。

        返回 JSON 必须严格符合以下结构：
        {
          "muscleGroup": "legs | chest | back | shoulders | arms | full_body",
          "dayType": "string",
          "durationMinutes": number,
          "aiNote": "string",
          "exercises": [
            {
              "exerciseName": "string",
              "category": "warmup | main | accessory | finisher | cooldown",
              "sets": number,
              "reps": number,
              "weight": number | null,
              "unit": "kg" | "lb" | null,
              "rationale": "string"
            }
          ]
        }

        规则：
        - muscleGroup 必须是以下值之一：legs, chest, back, shoulders, arms, full_body
        - category 必须是以下值之一：warmup, main, accessory, finisher, cooldown
        - unit 只能是 kg, lb 或 null
        - sets 和 reps 必须是大于 0 的整数，绝对不能返回 0
        - 如果动作是计时动作、拉伸、热身或放松，也必须设置 sets 至少为 1、reps 为 1，并在 rationale 中说明时长或目的
        - 不要返回 id、sessionId、sortOrder、calories、source
        - exercises 必须包含 5 到 8 个动作
        - exercises 字段必须存在，必须是数组，不能是 null，不能省略
        - 计划必须按 warmup -> main -> accessory -> finisher -> cooldown 顺序返回
        - warmup 至少 1 个动作
        - main 动作必须正好 1 个
        - accessory 动作必须 2 到 3 个
        - finisher 动作 0 到 2 个，可选
        - cooldown 至少 1 个动作
        - durationMinutes 应接近用户请求的训练时长
        - 必须使用 request.selectedMuscleGroup 作为 muscleGroup，不要自行改成其他肌群
        - 如果 request.muscleGroupSource 是 auto_selected_by_backend，说明后端已经根据当天完成训练和最近历史选好了更合理肌群
        - 如果 completedMuscleGroupsToday 包含 legs，避免生成腿部计划，除非 request.selectedMuscleGroup 明确是 legs
        - 如果用户是 beginner，避免复杂高风险动作，重量保守
        - 如果用户是 intermediate，可以安排中等训练量和渐进超负荷
        - 如果用户是 advanced，可以安排更高强度，但仍需合理
        - 如果目标是 muscle_gain，优先 6-12 次区间和足够训练量
        - 如果目标是 fat_loss，控制休息时间，训练密度略高，但不要把力量训练变成纯有氧
        - 如果目标是 strength，主项优先较低 reps 和更高重量
        - gender 可作为训练量、动作选择、恢复建议的参考因素之一，但不要做刻板化判断
        - 如果 gender 是 not_specified，按通用方案处理
        - 如果没有历史训练记录，使用保守入门重量
        - 如果有历史训练记录，参考最近同肌群或相关动作的重量，不要突然大幅增加
        - dayType 要像真实私人教练给今天训练起的标题
        - aiNote 使用目标语言，1-2 句，积极、有感染力、亲切，但必须具体说明训练逻辑
        - rationale 使用目标语言，每个动作 1 句，像教练在解释为什么安排这个动作
        """;

    private static string BuildGenerateUserMessage(AiPlanPromptContext context)
    {
        return JsonSerializer.Serialize(new
        {
            userProfile = context.Profile is null
                ? null
                : new
                {
                    experienceLevel = context.Profile.ExperienceLevel,
                    goal = context.Profile.Goal,
                    gender = context.Profile.Gender,
                    heightCm = context.Profile.HeightCm,
                    weightKg = context.Profile.WeightKg,
                },
            request = new
            {
                sessionDate = context.SessionDate,
                selectedMuscleGroup = context.SelectedMuscleGroup,
                muscleGroupSource = context.MuscleGroupSource,
                requestedDurationMinutes = context.DurationMinutes,
                outputLanguage = context.OutputLanguage,
            },
            completedMuscleGroupsToday = context.CompletedMuscleGroupsToday,
            recentSessions = context.RecentSessions.Select(ToPromptSession),
        }, JsonOpts);
    }

    private static string BuildAdjustSystemPrompt(string outputLanguage) => $$"""
        你是一位专业、热情、有感染力的 AI 私人教练。用户想调整一份已经生成的训练计划。
        你必须基于原计划和用户调整需求，返回一份完整的新训练计划 JSON。后端会用新计划替换旧计划。
        {{BuildCoachTonePrompt(outputLanguage)}}
        调整计划时要支持用户当前状态。如果用户 low_energy 或 short_time，不要让用户觉得自己失败了。强调调整是聪明训练的一部分，并给出更现实可完成的版本。

        你必须只返回一个合法 JSON object。不要返回 Markdown，不要返回解释文字，不要使用代码块。
        输出语言必须是：{{DescribeLanguage(outputLanguage)}}。
        不允许中英混杂。dayType、aiNote、exerciseName、rationale 必须全部使用目标语言。

        返回 JSON 必须严格符合以下结构：
        {
          "muscleGroup": "legs | chest | back | shoulders | arms | full_body",
          "dayType": "string",
          "durationMinutes": number,
          "aiNote": "string",
          "exercises": [
            {
              "exerciseName": "string",
              "category": "warmup | main | accessory | finisher | cooldown",
              "sets": number,
              "reps": number,
              "weight": number | null,
              "unit": "kg" | "lb" | null,
              "rationale": "string"
            }
          ]
        }

        调整规则：
        - 保留原计划的主要训练目标，除非用户明确要求更换
        - short_time：减少动作数量或组数，保留 main 和最关键 accessory
        - low_energy：降低重量或减少组数，避免高疲劳动作
        - high_intensity：可以增加组数或略微增加重量，但不要超过合理渐进幅度
        - swap：替换动作，但保持训练目标和强度相近
        - custom：优先满足用户 customMessage
        - 不要返回 id、sessionId、sortOrder、calories、source
        - muscleGroup 必须是以下值之一：legs, chest, back, shoulders, arms, full_body
        - category 必须是以下值之一：warmup, main, accessory, finisher, cooldown
        - unit 只能是 kg, lb 或 null
        - sets 和 reps 必须是大于 0 的整数，绝对不能返回 0
        - 如果动作是计时动作、拉伸、热身或放松，也必须设置 sets 至少为 1、reps 为 1，并在 rationale 中说明时长或目的
        - exercises 必须包含 5 到 8 个动作
        - exercises 字段必须存在，必须是数组，不能是 null，不能省略
        - 计划必须按 warmup -> main -> accessory -> finisher -> cooldown 顺序返回
        - warmup 至少 1 个动作
        - main 动作至少 1 个
        - accessory 至少 1 个动作
        - cooldown 至少 1 个动作
        - dayType 要像真实私人教练给调整后训练起的标题
        - aiNote 使用目标语言，1-2 句，积极、有感染力、亲切，但必须具体说明调整逻辑
        - rationale 使用目标语言，每个动作 1 句，像教练在解释为什么保留、减少或替换这个动作
        """;

    private static string BuildAdjustUserMessage(AiAdjustPromptContext context)
    {
        var adjustDesc = DescribeAdjustment(context.AdjustType, context.CustomMessage, context.OutputLanguage);

        return JsonSerializer.Serialize(new
        {
            userProfile = context.Profile is null
                ? null
                : new
                {
                    experienceLevel = context.Profile.ExperienceLevel,
                    goal = context.Profile.Goal,
                    gender = context.Profile.Gender,
                    heightCm = context.Profile.HeightCm,
                    weightKg = context.Profile.WeightKg,
                },
            adjustment = new
            {
                adjustType = context.AdjustType,
                adjustDescription = adjustDesc,
                customMessage = context.CustomMessage,
                outputLanguage = context.OutputLanguage,
            },
            currentSession = ToPromptSession(context.CurrentSession),
            recentSessions = context.RecentSessions.Select(ToPromptSession),
        }, JsonOpts);
    }

    // Helpers

    private async Task<JsonDocument> PostAsync(string path, object body, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOpts),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var response = await _http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("DeepSeek API error {Status}: {Body}", (int)response.StatusCode, json);
            throw new HttpRequestException(
                $"DeepSeek API returned {(int)response.StatusCode}: {json}");
        }

        _logger.LogDebug("DeepSeek raw response: {Body}", json);
        return JsonDocument.Parse(json);
    }

    private static string ExtractContent(JsonDocument doc)
    {
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()
            ?? throw new InvalidOperationException("Empty content from DeepSeek.");
    }

    private static string? GetConfigValue(IConfiguration cfg, string key, params string[] envNames)
    {
        var configured = cfg[$"Ai:{key}"] ?? cfg[$"DeepSeek:{key}"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        foreach (var envName in envNames)
        {
            var envValue = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(envValue)) return envValue;
        }

        return null;
    }

    private static GeneratedPlan NormalizePlan(GeneratedPlan plan, string? fallbackMuscleGroup, int fallbackDuration, int minExercises, int maxExercises)
    {
        if (plan.Exercises is null)
            throw new InvalidOperationException("AI plan is missing exercises.");

        var exercises = plan.Exercises
            .Where(e => e is not null)
            .Where(e => !string.IsNullOrWhiteSpace(e.ExerciseName))
            .Select(e => e with
            {
                Sets = e.Sets <= 0 ? 1 : e.Sets,
                Reps = e.Reps <= 0 ? 1 : e.Reps,
                Weight = e.Weight < 0 ? null : e.Weight,
                Category = NormalizeChoice(e.Category, ["warmup", "main", "accessory", "finisher", "cooldown"], "accessory"),
                Unit = string.IsNullOrWhiteSpace(e.Unit)
                    ? null
                    : NormalizeChoice(e.Unit, ["kg", "lb"], "kg")
            })
            .ToList();

        var normalized = plan with
        {
            MuscleGroup = NormalizeChoice(plan.MuscleGroup, ["legs", "chest", "back", "shoulders", "arms", "full_body"], fallbackMuscleGroup ?? "full_body"),
            DurationMinutes = plan.DurationMinutes > 0 ? plan.DurationMinutes : fallbackDuration,
            Exercises = exercises
        };

        ValidatePlan(normalized, minExercises, maxExercises);
        return normalized;
    }

    private static string NormalizeChoice(string? value, string[] allowed, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var normalized = value.Trim().ToLowerInvariant();
        return allowed.Contains(normalized) ? normalized : fallback;
    }

    private static object ToPromptSession(SessionSnapshot session)
    {
        return new
        {
            sessionDate = session.SessionDate,
            muscleGroup = session.MuscleGroup,
            dayType = session.DayType,
            durationMinutes = session.DurationMinutes,
            aiNote = session.AiNote,
            exercises = session.Exercises
                .OrderBy(e => e.SortOrder)
                .Select(e => new
                {
                    exerciseName = e.ExerciseName,
                    category = e.Category,
                    sets = e.Sets,
                    reps = e.Reps,
                    weight = e.Weight,
                    unit = e.Unit,
                    rationale = e.Rationale,
                }),
        };
    }

    private static void ValidatePlan(GeneratedPlan plan, int minExercises, int maxExercises)
    {
        if (string.IsNullOrWhiteSpace(plan.DayType))
            throw new InvalidOperationException("AI plan is missing dayType.");
        if (string.IsNullOrWhiteSpace(plan.AiNote))
            throw new InvalidOperationException("AI plan is missing aiNote.");
        if (plan.Exercises.Count < minExercises || plan.Exercises.Count > maxExercises)
            throw new InvalidOperationException($"AI plan must contain {minExercises}-{maxExercises} exercises.");
        if (plan.Exercises.Any(e => e.Sets <= 0 || e.Reps <= 0))
            throw new InvalidOperationException("AI plan contains invalid sets or reps.");

        var mainCount = plan.Exercises.Count(e => e.Category == "main");
        if (mainCount != 1)
            throw new InvalidOperationException("AI generated plan must contain exactly one main exercise.");

        if (!IsOrderedByTrainingSection(plan.Exercises))
            throw new InvalidOperationException("AI plan exercises must be ordered warmup -> main -> accessory -> finisher -> cooldown.");

        if (plan.Exercises.Count(e => e.Category == "warmup") < 1)
            throw new InvalidOperationException("AI plan must contain at least one warmup exercise.");
        if (plan.Exercises.Count(e => e.Category == "accessory") < 1)
            throw new InvalidOperationException("AI plan must contain at least one accessory exercise.");
        if (plan.Exercises.Count(e => e.Category == "cooldown") < 1)
            throw new InvalidOperationException("AI plan must contain at least one cooldown exercise.");
    }

    private static bool IsOrderedByTrainingSection(List<GeneratedPlanExercise> exercises)
    {
        var lastRank = -1;
        foreach (var exercise in exercises)
        {
            var rank = exercise.Category switch
            {
                "warmup" => 0,
                "main" => 1,
                "accessory" => 2,
                "finisher" => 3,
                "cooldown" => 4,
                _ => 99,
            };

            if (rank < lastRank) return false;
            lastRank = rank;
        }

        return true;
    }

    private static string DescribeLanguage(string outputLanguage)
        => outputLanguage == "zh"
            ? "中文。必须返回中文标题、中文说明、中文动作名称"
            : "English. All titles, notes, exercise names, and rationales must be in English";

    private static string BuildCoachTonePrompt(string outputLanguage)
    {
        if (outputLanguage == "zh")
        {
            return """
                教练风格：
                - 积极、亲切、直接、有陪伴感，像一个真正站在用户身边的私人教练
                - 语言要有激情和感染力，让用户感觉“今天可以开始动起来”
                - 鼓励要具体，必须和训练安排有关，不能空喊口号
                - 不羞辱用户，不责备用户，不制造焦虑，不使用恐吓式语言
                - 不要油腻，不要过度鸡血，不要使用“燃爆了”“狠狠练爆”“榨干自己”等夸张词
                - 避免机械、冷冰冰的说明
                - 不要中英混杂
                """;
        }

        return """
            Coaching style:
            - Be energetic, warm, direct, and practical, like a personal trainer who is on the user's side
            - Make the user feel capable and ready to train
            - Motivation must be specific and connected to the training plan, not empty hype
            - Avoid shame, guilt, fear-based language, or drill-sergeant aggression
            - Do not use exaggerated hype like "destroy yourself", "crush your body", or similar language
            - Avoid cold, mechanical explanations
            - Do not mix Chinese and English
            """;
    }

    private static string DescribeAdjustment(string adjustType, string? customMessage, string outputLanguage)
    {
        if (outputLanguage == "zh")
        {
            return adjustType switch
            {
                "low_energy" => "用户今天状态偏低，需要一版更温和但仍然有效的训练",
                "short_time" => "用户今天时间有限，需要保留关键训练效果的精简版本",
                "swap" => "用户想换一套不同动作，但仍希望保持训练目标",
                "high_intensity" => "用户今天状态很好，希望训练更有挑战但仍保持合理",
                "custom" => customMessage ?? "自定义调整",
                _ => customMessage ?? "调整计划",
            };
        }

        return adjustType switch
        {
            "low_energy" => "The user has lower energy today and needs a gentler plan that still feels useful.",
            "short_time" => "The user has limited time today and needs a shorter version that keeps the most valuable work.",
            "swap" => "The user wants a different exercise selection while keeping the same training goal.",
            "high_intensity" => "The user feels strong today and wants a more challenging but still reasonable plan.",
            "custom" => customMessage ?? "Custom adjustment.",
            _ => customMessage ?? "Adjust the plan.",
        };
    }
}
