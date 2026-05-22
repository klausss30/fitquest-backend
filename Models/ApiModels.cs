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
    double? WeightKg,
    string Language);

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
    double? WeightKg,
    string? Language);

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

public record GeneratedPlan(
    string MuscleGroup,
    string DayType,
    int DurationMinutes,
    string AiNote,
    List<GeneratedPlanExercise> Exercises);

public record GeneratedPlanExercise(
    string ExerciseName,
    string Category,
    int Sets,
    int Reps,
    double? Weight,
    string? Unit,
    string? Rationale);

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
        profile.WeightKg,
        profile.Language);

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
