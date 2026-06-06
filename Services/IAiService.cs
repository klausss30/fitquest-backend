using FitQuest.Api.Models;

namespace FitQuest.Api.Services;

public interface IAiService
{
    string Provider { get; }
    string Model { get; }

    Task<AiPlanResult> GeneratePlanAsync(AiPlanPromptContext context, CancellationToken ct = default);

    Task<AiPlanResult> AdjustPlanAsync(AiAdjustPromptContext context, CancellationToken ct = default);

    Task<AiNutritionResult> GenerateNutritionAsync(AiNutritionPromptContext context, CancellationToken ct = default);
}

public record AiPlanResult(
    GeneratedPlan Plan,
    string PromptSnapshot,
    string ResponseSnapshot);

public record AiNutritionResult(
    GeneratedNutrition Nutrition,
    string PromptSnapshot,
    string ResponseSnapshot);

public record AiNutritionPromptContext(
    UserSnapshot User,
    ProfileSnapshot? Profile,
    string OutputLanguage,
    CheckInSnapshot? TodayCheckIn,
    int SessionsLast7Days,
    TodayPlanSnapshot? TodayPlan = null);

public record TodayPlanSnapshot(
    string MuscleGroup,
    string DayType,
    int DurationMinutes,
    string? AiNote);

public class AiResponseParseException : InvalidOperationException
{
    public AiResponseParseException(string message, string responseSnapshot, Exception innerException)
        : base(message, innerException)
    {
        ResponseSnapshot = responseSnapshot;
    }

    public string ResponseSnapshot { get; }
}

public record CheckInSnapshot(
    double SleepHours,
    int EnergyLevel,
    int StressLevel,
    double? WeightKg,
    int RecoveryScore,
    string RecoveryStatus,
    string? Notes);

public record AiPlanPromptContext(
    UserSnapshot User,
    ProfileSnapshot? Profile,
    DateOnly SessionDate,
    string SelectedMuscleGroup,
    string MuscleGroupSource,
    int DurationMinutes,
    string OutputLanguage,
    List<string> CompletedMuscleGroupsToday,
    List<SessionSnapshot> RecentSessions,
    CheckInSnapshot? TodayCheckIn = null);

public record AiAdjustPromptContext(
    UserSnapshot User,
    ProfileSnapshot? Profile,
    string AdjustType,
    string? CustomMessage,
    string OutputLanguage,
    SessionSnapshot CurrentSession,
    List<SessionSnapshot> RecentSessions);

public record UserSnapshot(int Id);

public record ProfileSnapshot(
    string ExperienceLevel,
    string Goal,
    string Gender,
    double? HeightCm,
    double? WeightKg);

public record SessionSnapshot(
    int Id,
    DateOnly SessionDate,
    string MuscleGroup,
    string DayType,
    int DurationMinutes,
    string? AiNote,
    List<ExerciseSnapshot> Exercises);

public record ExerciseSnapshot(
    string ExerciseName,
    string Category,
    int Sets,
    int Reps,
    double? Weight,
    string? Unit,
    string? Rationale,
    int SortOrder);
