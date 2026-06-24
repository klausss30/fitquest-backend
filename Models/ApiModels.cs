using FitQuest.Api.Data.Entities;

namespace FitQuest.Api.Models;

public record UserDto(int Id, string Name, string Email);

public record ProfileDto(
    int Id,
    int UserId,
    string ExperienceLevel,
    string Goal,
    string Gender,
    double? HeightCm,
    double? WeightKg);

public record TrainingSessionDto(
    int Id,
    int UserId,
    DateOnly SessionDate,
    string MuscleGroup,
    string DayType,
    int DurationMinutes,
    string? AiNote);

public record SessionExerciseDto(
    int Id,
    int SessionId,
    string ExerciseName,
    string Category,
    int Sets,
    int Reps,
    double? Weight,
    string? Unit,
    string? Rationale,
    int SortOrder);

public record SessionSummaryDto(
    int Id,
    DateOnly SessionDate,
    string MuscleGroup,
    string DayType,
    int DurationMinutes,
    string? AiNote,
    int ExercisesCount);

public record WeekPlanDayDto(
    DateOnly SessionDate,
    string MuscleGroup,
    string DayType,
    string Reason);

public record TemporaryPlanDto(
    DateOnly SessionDate,
    string MuscleGroup,
    string DayType,
    int DurationMinutes,
    string? AiNote);

public record TemporaryExerciseDto(
    string ExerciseName,
    string Category,
    int Sets,
    int Reps,
    double? Weight,
    string? Unit,
    string? Rationale,
    int SortOrder);

public record RegisterRequest(string Name, string Email, string Password);

public record LoginRequest(string Email, string Password);

public record UpsertProfileRequest(
    string ExperienceLevel,
    string Goal,
    string? Gender,
    double? HeightCm,
    double? WeightKg);

public record GeneratePlanRequest(
    DateOnly? SessionDate,
    string? MuscleGroup,
    int? DurationMinutes);

public record AdjustPlanRequest(
    TemporaryPlanDto CurrentPlan,
    List<TemporaryExerciseDto> Exercises,
    string AdjustType,
    string? CustomMessage);

public record CreateTrainingSessionRequest(
    DateOnly SessionDate,
    string MuscleGroup,
    string DayType,
    int DurationMinutes,
    string? AiNote,
    List<CreateSessionExerciseRequest> Exercises);

public record CreateSessionExerciseRequest(
    string ExerciseName,
    string Category,
    int Sets,
    int Reps,
    double? Weight,
    string? Unit,
    string? Rationale);

// ── Reasoning models ─────────────────────────────────────────────────────────

public record ReasoningGoalAnalysis(
    string PrimaryGoal,
    string? SecondaryGoal,
    string Note);

public record ReasoningRecoveryAnalysis(
    double SleepHours,
    int EnergyLevel,
    int StressLevel,
    int RecoveryScore,
    string Summary);

public record ReasoningRiskAssessment(
    string Level,        // low | moderate | high
    List<string> Factors);

public record ReasoningHistoryAnalysis(
    int SessionsLast7Days,
    string? LastMuscleGroup,
    string Summary);

public record ReasoningDecision(
    string MuscleGroup,
    string Action,
    string Rationale);

public record PlanReasoning(
    ReasoningGoalAnalysis GoalAnalysis,
    ReasoningRecoveryAnalysis? RecoveryAnalysis,
    ReasoningRiskAssessment RiskAssessment,
    ReasoningHistoryAnalysis HistoryAnalysis,
    ReasoningDecision Decision);

public record GeneratedPlan(
    string MuscleGroup,
    string DayType,
    int DurationMinutes,
    string AiNote,
    List<GeneratedPlanExercise> Exercises,
    PlanReasoning? Reasoning = null);

public record GeneratedPlanExercise(
    string ExerciseName,
    string Category,
    int Sets,
    int Reps,
    double? Weight,
    string? Unit,
    string? Rationale);

// ── Nutrition models ──────────────────────────────────────────────────────────

public record NutritionMealSuggestion(
    string Meal,
    string Suggestion,
    int CaloriesApprox);

public record GeneratedNutrition(
    int DailyCalories,
    int ProteinG,
    int CarbsG,
    int FatG,
    string GoalNote,
    List<NutritionMealSuggestion> MealSuggestions,
    string Reasoning);

public static class ApiMapping
{
    public static UserDto ToDto(this User user) => new(user.Id, user.Name, user.Email);

    public static ProfileDto ToDto(this UserProfile profile) => new(
        profile.Id,
        profile.UserId,
        profile.ExperienceLevel,
        profile.Goal,
        profile.Gender,
        profile.HeightCm,
        profile.WeightKg);

    public static TrainingSessionDto ToDto(this TrainingSession session) => new(
        session.Id,
        session.UserId,
        session.SessionDate,
        session.MuscleGroup,
        session.DayType,
        session.DurationMinutes,
        session.AiNote);

    public static SessionExerciseDto ToDto(this SessionExercise exercise) => new(
        exercise.Id,
        exercise.SessionId,
        exercise.ExerciseName,
        exercise.Category,
        exercise.Sets,
        exercise.Reps,
        exercise.Weight,
        exercise.Unit,
        exercise.Rationale,
        exercise.SortOrder);
}
