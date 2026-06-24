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
    private readonly string _chatPath;
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

        _apiKey = GetConfigValue(cfg, "ApiKey", "AI_API_KEY")
            ?? throw new InvalidOperationException(
                "AI API key is not configured. Set AI_API_KEY in .env.");

        // Detect Azure AI Foundry from the base URL — no separate AI_PROVIDER var needed.
        // Azure uses /chat/completions; DeepSeek uses /v1/chat/completions.
        var baseUrl = GetConfigValue(cfg, "BaseUrl", "AI_BASE_URL") ?? "";
        var isAzure = baseUrl.Contains("services.ai.azure.com", StringComparison.OrdinalIgnoreCase)
                   || (GetConfigValue(cfg, "Provider", "AI_PROVIDER") ?? "")
                      .Equals("azure-foundry", StringComparison.OrdinalIgnoreCase);

        _provider = isAzure ? "azure-foundry" : (GetConfigValue(cfg, "Provider", "AI_PROVIDER") ?? "DeepSeek");
        _chatPath = isAzure ? "chat/completions" : "v1/chat/completions";
        _model    = GetConfigValue(cfg, "Model", "AI_MODEL")
            ?? (isAzure ? "gpt-4o" : "deepseek-chat");
    }

    public string Provider => _provider;

    public string Model => _model;

    public async Task<AiPlanResult> GeneratePlanAsync(AiPlanPromptContext context, CancellationToken ct = default)
    {
        var systemPrompt = BuildGenerateSystemPrompt();
        var userMessage = BuildGenerateUserMessage(context);

        var (promptSnapshot, content) = await CompleteJsonAsync(systemPrompt, userMessage, 0.5, 3200, ct);

        var plan = DeserializePlan(content, $"{_provider} returned invalid plan JSON.");

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
        var systemPrompt = BuildAdjustSystemPrompt();
        var userMessage = BuildAdjustUserMessage(context);

        var (promptSnapshot, content) = await CompleteJsonAsync(systemPrompt, userMessage, 0.5, 3200, ct);

        var plan = DeserializePlan(content, $"{_provider} returned invalid adjusted plan JSON.");

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

        var response = await PostAsync(_chatPath, requestBody, ct);
        return (promptSnapshot, ExtractContent(response));
    }

    private static GeneratedPlan DeserializePlan(string content, string errorMessage)
    {
        try
        {
            // Parse the full AI response which now includes reasoning + plan fields
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // Parse reasoning (optional – if AI omits it, we continue without it)
            PlanReasoning? reasoning = null;
            if (root.TryGetProperty("reasoning", out var reasoningEl))
            {
                try { reasoning = JsonSerializer.Deserialize<PlanReasoning>(reasoningEl.GetRawText(), JsonOpts); }
                catch { /* reasoning parse failure is non-fatal */ }
            }

            // Parse core plan fields from the same root object
            var plan = JsonSerializer.Deserialize<GeneratedPlan>(content, JsonOpts)
                ?? throw new JsonException("Deserialized plan was null.");

            return plan with { Reasoning = reasoning };
        }
        catch (JsonException ex)
        {
            throw new AiResponseParseException(errorMessage, content, ex);
        }
    }

    public async Task<AiNutritionResult> GenerateNutritionAsync(AiNutritionPromptContext context, CancellationToken ct = default)
    {
        var systemPrompt = BuildNutritionSystemPrompt();
        var userMessage  = BuildNutritionUserMessage(context);

        var (promptSnapshot, content) = await CompleteJsonAsync(systemPrompt, userMessage, 0.4, 2400, ct);

        GeneratedNutrition nutrition;
        try
        {
            nutrition = JsonSerializer.Deserialize<GeneratedNutrition>(content, JsonOpts)
                ?? throw new JsonException("Deserialized nutrition was null.");
        }
        catch (JsonException ex)
        {
            throw new AiResponseParseException($"{_provider} returned invalid nutrition JSON.", content, ex);
        }

        return new AiNutritionResult(nutrition, promptSnapshot, content);
    }

    // Prompt builders

    private const string JsonOnlyRule = "Return only one valid JSON object. Do not use Markdown, code fences, or extra text. All text fields must be in English.";

    private const string CoachToneRule = """
        Coaching style:
        - Warm, energetic, specific, and practical, like a supportive personal trainer.
        - Explain the training logic without empty hype.
        - Avoid shame, fear, guilt, drill-sergeant language, or exaggerated phrases.
        """;

    private const string PlanSchema = """
        {
          "reasoning": {
            "goalAnalysis": {
              "primaryGoal": "string",
              "secondaryGoal": "string | null",
              "note": "string (1 sentence explaining how the goal affects today's training)"
            },
            "recoveryAnalysis": null or {
              "sleepHours": number,
              "energyLevel": number,
              "stressLevel": number,
              "recoveryScore": number,
              "summary": "string (1 sentence about current recovery)"
            },
            "riskAssessment": {
              "level": "low | moderate | high",
              "factors": ["string"]
            },
            "historyAnalysis": {
              "sessionsLast7Days": number,
              "lastMuscleGroup": "string | null",
              "summary": "string (1 sentence about recent training pattern)"
            },
            "decision": {
              "muscleGroup": "string",
              "action": "string (1 sentence with today's training direction)",
              "rationale": "string (1 sentence explaining the decision)"
            }
          },
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
        """;

    private const string PlanRules = """
        Plan rules:
        - muscleGroup: legs, chest, back, shoulders, arms, or full_body.
        - category: warmup, main, accessory, finisher, cooldown. Return exercises in that order.
        - unit: kg, lb, or null. sets and reps must be positive integers; use sets=1 and reps=1 for timed mobility, stretching, warmups, or cooldowns and explain timing/purpose in rationale.
        - Do not return id, sessionId, sortOrder, calories, or source.
        - Include 5-8 exercises: at least 1 warmup, exactly 1 main, 2-3 accessory, 0-2 finisher, and at least 1 cooldown.
        - durationMinutes should be close to the requested duration.
        - Use request.selectedMuscleGroup exactly as muscleGroup. If muscleGroupSource is auto_selected_by_backend, trust the backend choice.
        - If completedMuscleGroupsToday includes legs, avoid legs unless request.selectedMuscleGroup is legs.
        - Match experience level: beginner = simple/conservative, intermediate = moderate progressive overload, advanced = challenging but reasonable.
        - Match goal: muscle_gain = 6-12 reps and enough volume; fat_loss = denser strength work without becoming pure cardio; strength = lower reps and heavier main lift.
        - Gender may inform recovery or sizing but never use stereotypes; not_specified means general programming.
        - Use weightKg and recent history for starting loads. Prefer recent same-muscle or related exercise loads; avoid sudden jumps. Without weightKg, choose conservative beginner loads.
        - If heightCm and weightKg exist, use them to estimate body size and training density.
        - dayType should sound like a real trainer's title.
        - aiNote: 1-2 specific, encouraging sentences that explain the plan logic.
        - rationale: one sentence per exercise explaining why it is included.
        """;

    private static string BuildGenerateSystemPrompt() => $$"""
        You are an expert AI personal trainer. Analyze the user's goal, readiness, risk, and recent history, then generate today's workout plan.
        {{CoachToneRule}}
        {{JsonOnlyRule}}

        JSON schema:
        {{PlanSchema}}

        Reasoning rules:
        - If todayCheckIn is null, recoveryAnalysis must be null; otherwise use that check-in data.
        - riskAssessment.level must be low, moderate, or high; factors must contain 1-3 concise risk or positive factors.
        - historyAnalysis.sessionsLast7Days is counted from recentSessions over the last 7 days.
        - decision.muscleGroup must match the final muscleGroup.
        {{PlanRules}}
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
            },
            todayCheckIn = context.TodayCheckIn is null ? null : new
            {
                sleepHours = context.TodayCheckIn.SleepHours,
                energyLevel = context.TodayCheckIn.EnergyLevel,
                stressLevel = context.TodayCheckIn.StressLevel,
                weightKg = context.TodayCheckIn.WeightKg,
                recoveryScore = context.TodayCheckIn.RecoveryScore,
                recoveryStatus = context.TodayCheckIn.RecoveryStatus,
                notes = context.TodayCheckIn.Notes,
            },
            completedMuscleGroupsToday = context.CompletedMuscleGroupsToday,
            recentSessions = context.RecentSessions.Select(ToPromptSession),
        }, JsonOpts);
    }

    private static string BuildAdjustSystemPrompt() => $$"""
        You are an expert AI personal trainer. The user wants to adjust an existing workout. Return a complete replacement plan based on the original plan and the requested adjustment.
        {{CoachToneRule}}
        If the user reports low_energy or short_time, frame the adjustment as smart training and make the plan more realistic.
        {{JsonOnlyRule}}

        JSON schema:
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

        Adjustment rules:
        - Keep the original training goal unless the user clearly asks to change it.
        - short_time: reduce exercise count or sets while keeping main and the most valuable accessory work.
        - low_energy: reduce load or sets and avoid high-fatigue choices.
        - high_intensity: add sets or slightly increase load within reasonable progression.
        - swap: replace exercises while keeping goal and intensity similar.
        - custom: prioritize customMessage.
        {{PlanRules}}
        """;

    private static string BuildAdjustUserMessage(AiAdjustPromptContext context)
    {
        var adjustDesc = DescribeAdjustment(context.AdjustType, context.CustomMessage);

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
            },
            currentSession = ToPromptSession(context.CurrentSession),
            recentSessions = context.RecentSessions.Select(ToPromptSession),
        }, JsonOpts);
    }

    private static string BuildNutritionSystemPrompt() => $$"""
        You are an expert AI nutrition coach. Use the user's body data, goal, readiness, recent training frequency, and today's workout if present to produce daily nutrition guidance.
        {{JsonOnlyRule}}

        JSON schema:
        {
          "dailyCalories": number,
          "proteinG": number,
          "carbsG": number,
          "fatG": number,
          "goalNote": "string (1-2 sentences explaining how this supports the user's goal)",
          "mealSuggestions": [
            {
              "meal": "Breakfast | Lunch | Dinner | Snack",
              "suggestion": "string (specific food pairing, 1-2 sentences)",
              "caloriesApprox": number
            }
          ],
          "reasoning": "string (1-2 sentences explaining calorie and macro calculation)"
        }

        Nutrition rules:
        - Use weightKg or 70kg, heightCm or 170cm, age 28, and Mifflin-St Jeor BMR. For not_specified gender, average male and female BMR.
        - Activity factor by sessionsLast7Days: 0-1 = 1.2, 2-3 = 1.375, 4-5 = 1.55, 6-7 = 1.725.
        - Goal calories: fat_loss = TDEE x 0.80, muscle_gain = TDEE x 1.10, strength = TDEE x 1.00. Round to nearest 50 kcal.
        - Macros: fat_loss 2.0g protein/kg and 25% fat; muscle_gain 2.2g protein/kg and 25% fat; strength 2.0g protein/kg and 30% fat. Remaining calories are carbs. Protein/carbs = 4 kcal/g, fat = 9 kcal/g.
        - dailyCalories must be positive and roughly match macro calories within 5%.
        - Return 3-4 meals named Breakfast, Lunch, Dinner, and optional Snack. caloriesApprox totals should be close to dailyCalories.
        - If training frequency is high (4+ sessions/7 days), carbs may rise 5-10%.
        - If energyLevel <= 3 or stressLevel >= 8, suggest easier-to-digest foods and avoid very high fat/fiber meals.
        - Do not provide a shopping list or exact recipe; give concrete, practical food pairings.
        - If todayTrainingPlan exists, reflect it in goalNote: legs/full_body raises carbs 8-12%; chest/back/shoulders raises protein by 0.1g/kg; arms keeps standard macros and highlights amino acid support. If durationMinutes > 60, include a post-workout snack with protein and fast carbs.
        """;

    private static string BuildNutritionUserMessage(AiNutritionPromptContext context)
    {
        return JsonSerializer.Serialize(new
        {
            userProfile = context.Profile is null
                ? null
                : new
                {
                    goal            = context.Profile.Goal,
                    gender          = context.Profile.Gender,
                    heightCm        = context.Profile.HeightCm,
                    weightKg        = context.Profile.WeightKg,
                    experienceLevel = context.Profile.ExperienceLevel,
                },
            sessionsLast7Days = context.SessionsLast7Days,
            todayCheckIn = context.TodayCheckIn is null ? null : new
            {
                energyLevel   = context.TodayCheckIn.EnergyLevel,
                stressLevel   = context.TodayCheckIn.StressLevel,
                recoveryScore = context.TodayCheckIn.RecoveryScore,
            },
            todayTrainingPlan = context.TodayPlan is null ? null : new
            {
                muscleGroup     = context.TodayPlan.MuscleGroup,
                dayType         = context.TodayPlan.DayType,
                durationMinutes = context.TodayPlan.DurationMinutes,
                aiNote          = context.TodayPlan.AiNote,
            },
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
            _logger.LogError("{Provider} API error {Status}: {Body}", _provider, (int)response.StatusCode, json);
            throw new HttpRequestException(
                $"{_provider} API returned {(int)response.StatusCode}: {json}");
        }

        _logger.LogDebug("{Provider} raw response: {Body}", _provider, json);
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
        // Env vars take priority — ResolveAiProvider() in Program.cs sets them at startup
        // to override appsettings.json defaults when switching providers.
        foreach (var envName in envNames)
        {
            var envValue = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(envValue)) return envValue;
        }

        return cfg[$"Ai:{key}"];
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

    private static string DescribeAdjustment(string adjustType, string? customMessage)
    {
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
