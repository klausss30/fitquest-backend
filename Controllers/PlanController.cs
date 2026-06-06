using System.Text.Json;
using FitQuest.Api.Data;
using FitQuest.Api.Data.Entities;
using FitQuest.Api.Models;
using FitQuest.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace FitQuest.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/plan")]
public class PlanController : ControllerBase
{
    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly AppDbContext _db;
    private readonly IAiService _ai;
    private readonly ILogger<PlanController> _logger;

    public PlanController(AppDbContext db, IAiService ai, ILogger<PlanController> logger)
    {
        _db = db;
        _ai = ai;
        _logger = logger;
    }

    [HttpPost("generate")]
    [EnableRateLimiting("ai")]
    public async Task<IActionResult> Generate([FromBody] GeneratePlanRequest request, CancellationToken ct)
    {
        var userId = this.CurrentUserId();
        var user = await _db.Users
            .Include(x => x.Profile)
            .FirstOrDefaultAsync(x => x.Id == userId, ct);

        if (user is null) return Unauthorized(new { error = "User not found or session expired" });

        var muscleGroup = NormalizeOptional(request.MuscleGroup);
        if (muscleGroup is not null && !ControllerHelpers.MuscleGroups.Contains(muscleGroup))
            return BadRequest(new { error = "muscle_group must be one of: legs / chest / back / shoulders / arms / full_body" });

        var durationMinutes = request.DurationMinutes ?? 60;
        if (durationMinutes <= 0) return BadRequest(new { error = "duration_minutes must be greater than 0" });

        var sessionDate = request.SessionDate ?? DateOnly.FromDateTime(DateTime.Today);
        var recentSessions = await LoadRecentSessions(userId, null, ct);
        var completedToday = recentSessions
            .Where(x => x.SessionDate == sessionDate)
            .Select(x => x.MuscleGroup)
            .Distinct()
            .ToList();
        var selectedMuscleGroup = muscleGroup ?? ChooseMuscleGroup(sessionDate, recentSessions);

        var todayCheckIn = await _db.DailyCheckIns
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Date == sessionDate, ct);

        var context = new AiPlanPromptContext(
            user.ToSnapshot(),
            user.Profile?.ToSnapshot(),
            sessionDate,
            selectedMuscleGroup,
            muscleGroup is null ? "auto_selected_by_backend" : "user_requested",
            durationMinutes,
            ResolveOutputLanguage(Request.Headers.AcceptLanguage.ToString(), user.Profile?.Language),
            completedToday,
            recentSessions,
            todayCheckIn is null ? null : new CheckInSnapshot(
                todayCheckIn.SleepHours,
                todayCheckIn.EnergyLevel,
                todayCheckIn.StressLevel,
                todayCheckIn.WeightKg,
                todayCheckIn.RecoveryScore,
                DescribeRecoveryStatus(todayCheckIn.RecoveryScore),
                todayCheckIn.Notes));

        var fallbackPrompt = JsonSerializer.Serialize(context, SnapshotJson);

        try
        {
            var aiResult = await _ai.GeneratePlanAsync(context, ct);
            await SaveAiRequest(
                userId,
                "generate_plan",
                aiResult.PromptSnapshot,
                aiResult.ResponseSnapshot,
                "success",
                null,
                ct);

            return Ok(ToTemporaryPlanResponse(context.SessionDate, aiResult.Plan));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate training plan for user {UserId}", userId);
            await SaveAiRequest(userId, "generate_plan", fallbackPrompt, ExtractFailedAiResponse(ex), "failed", ex.Message, ct);
            return StatusCode(500, new { error = "Failed to generate plan. Please try again." });
        }
    }

    [HttpPost("adjust")]
    [EnableRateLimiting("ai")]
    public async Task<IActionResult> Adjust([FromBody] AdjustPlanRequest request, CancellationToken ct)
    {
        var userId = this.CurrentUserId();
        var user = await _db.Users
            .Include(x => x.Profile)
            .FirstOrDefaultAsync(x => x.Id == userId, ct);

        if (user is null) return Unauthorized(new { error = "User not found or session expired" });

        if (string.IsNullOrWhiteSpace(request.AdjustType))
            return BadRequest(new { error = "adjust_type is required" });
        if (request.CurrentPlan is null)
            return BadRequest(new { error = "current_plan is required" });
        if (request.Exercises is null || request.Exercises.Count == 0)
            return BadRequest(new { error = "exercises cannot be empty" });

        var currentSnapshot = ToSnapshot(request.CurrentPlan, request.Exercises);
        var customMessage = request.CustomMessage?.Length > 500
            ? request.CustomMessage[..500]
            : request.CustomMessage;
        var context = new AiAdjustPromptContext(
            user.ToSnapshot(),
            user.Profile?.ToSnapshot(),
            (request.AdjustType ?? "").Trim().ToLowerInvariant(),
            customMessage,
            ResolveOutputLanguage(Request.Headers.AcceptLanguage.ToString(), user.Profile?.Language),
            currentSnapshot,
            await LoadRecentSessions(userId, null, ct));

        var fallbackPrompt = JsonSerializer.Serialize(context, SnapshotJson);

        try
        {
            var aiResult = await _ai.AdjustPlanAsync(context, ct);
            await SaveAiRequest(
                userId,
                "adjust_plan",
                aiResult.PromptSnapshot,
                aiResult.ResponseSnapshot,
                "success",
                null,
                ct);

            return Ok(ToTemporaryPlanResponse(context.CurrentSession.SessionDate, aiResult.Plan));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to adjust temporary training plan for user {UserId}", userId);
            await SaveAiRequest(userId, "adjust_plan", fallbackPrompt, ExtractFailedAiResponse(ex), "failed", ex.Message, ct);
            return StatusCode(500, new { error = "Failed to adjust plan. Please try again." });
        }
    }

    private async Task SaveAiRequest(
        int userId,
        string requestType,
        string promptSnapshot,
        string? responseSnapshot,
        string status,
        string? errorMessage,
        CancellationToken ct)
    {
        _db.AiPlanRequests.Add(new AiPlanRequest
        {
            UserId = userId,
            Provider = _ai.Provider,
            Model = _ai.Model,
            RequestType = requestType,
            PromptSnapshot = promptSnapshot,
            ResponseSnapshot = responseSnapshot,
            Status = status,
            ErrorMessage = errorMessage,
        });

        await _db.SaveChangesAsync(ct);
    }

    private async Task<List<SessionSnapshot>> LoadRecentSessions(int userId, int? excludeSessionId, CancellationToken ct)
    {
        var sessions = await _db.TrainingSessions
            .Include(x => x.Exercises)
            .Where(x => x.UserId == userId && (!excludeSessionId.HasValue || x.Id != excludeSessionId.Value))
            .OrderByDescending(x => x.SessionDate)
            .ThenByDescending(x => x.Id)
            .Take(5)
            .ToListAsync(ct);

        return sessions
            .OrderBy(x => x.SessionDate)
            .ThenBy(x => x.Id)
            .Select(x => x.ToSnapshot())
            .ToList();
    }

    private static object ToTemporaryPlanResponse(DateOnly sessionDate, GeneratedPlan plan)
    {
        return new
        {
            plan = new TemporaryPlanDto(
                sessionDate,
                plan.MuscleGroup,
                plan.DayType,
                plan.DurationMinutes,
                plan.AiNote),
            exercises = plan.Exercises.Select((exercise, index) => new TemporaryExerciseDto(
                exercise.ExerciseName,
                exercise.Category,
                exercise.Sets,
                exercise.Reps,
                exercise.Weight,
                exercise.Unit,
                exercise.Rationale,
                index + 1)),
            reasoning = plan.Reasoning,
        };
    }

    private static string DescribeRecoveryStatus(int score) =>
        ControllerHelpers.DescribeRecoveryStatus(score);

    private static SessionSnapshot ToSnapshot(TemporaryPlanDto plan, List<TemporaryExerciseDto> exercises)
    {
        return new SessionSnapshot(
            0,
            plan.SessionDate,
            plan.MuscleGroup,
            plan.DayType,
            plan.DurationMinutes,
            plan.AiNote,
            exercises
                .OrderBy(x => x.SortOrder)
                .Select(x => new ExerciseSnapshot(
                    x.ExerciseName,
                    x.Category,
                    x.Sets,
                    x.Reps,
                    x.Weight,
                    x.Unit,
                    x.Rationale,
                    x.SortOrder))
                .ToList());
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static string? ExtractFailedAiResponse(Exception ex)
        => ex is AiResponseParseException parseException ? parseException.ResponseSnapshot : null;

    private static string ChooseMuscleGroup(DateOnly sessionDate, List<SessionSnapshot> recentSessions)
    {
        var completedToday = recentSessions
            .Where(x => x.SessionDate == sessionDate)
            .Select(x => x.MuscleGroup)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = ControllerHelpers.MuscleGroups
            .Where(x => !completedToday.Contains(x))
            .ToList();

        if (candidates.Count == 0)
            candidates = ControllerHelpers.MuscleGroups.ToList();

        if (recentSessions.Count == 0)
            return candidates.OrderBy(x => Array.IndexOf(ControllerHelpers.DefaultMuscleGroupOrder, x)).First();

        return candidates
            .Select(group => new
            {
                Group = group,
                LastDate = recentSessions
                    .Where(x => string.Equals(x.MuscleGroup, group, StringComparison.OrdinalIgnoreCase))
                    .Select(x => (DateOnly?)x.SessionDate)
                    .Max(),
                DefaultRank = Array.IndexOf(ControllerHelpers.DefaultMuscleGroupOrder, group),
            })
            .OrderBy(x => x.LastDate.HasValue ? 1 : 0)
            .ThenBy(x => x.LastDate ?? DateOnly.MinValue)
            .ThenBy(x => x.DefaultRank < 0 ? int.MaxValue : x.DefaultRank)
            .First()
            .Group;
    }

    private static string ResolveOutputLanguage(string? acceptLanguage, string? profileLanguage) =>
        ControllerHelpers.ResolveOutputLanguage(acceptLanguage, profileLanguage);
}

file static class PlanControllerMapping
{
    public static UserSnapshot ToSnapshot(this User user) => new(user.Id);

    public static ProfileSnapshot ToSnapshot(this UserProfile profile) => new(
        profile.ExperienceLevel,
        profile.Goal,
        profile.Gender,
        profile.HeightCm,
        profile.WeightKg);

    public static SessionSnapshot ToSnapshot(this TrainingSession session) => new(
        session.Id,
        session.SessionDate,
        session.MuscleGroup,
        session.DayType,
        session.DurationMinutes,
        session.AiNote,
        session.Exercises
            .OrderBy(x => x.SortOrder)
            .Select(x => new ExerciseSnapshot(
                x.ExerciseName,
                x.Category,
                x.Sets,
                x.Reps,
                x.Weight,
                x.Unit,
                x.Rationale,
                x.SortOrder))
            .ToList());
}
