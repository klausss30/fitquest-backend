namespace FitQuest.Api.Data.Entities;

public interface ICreatedAt
{
    DateTimeOffset CreatedAt { get; set; }
}

public interface IUpdatedAt
{
    DateTimeOffset UpdatedAt { get; set; }
}

public class User : ICreatedAt, IUpdatedAt
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public UserProfile? Profile { get; set; }
    public List<TrainingSession> TrainingSessions { get; set; } = [];
    public List<AiPlanRequest> AiPlanRequests { get; set; } = [];
}

public class UserProfile : ICreatedAt, IUpdatedAt
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string ExperienceLevel { get; set; } = "";
    public string Goal { get; set; } = "";
    public string Gender { get; set; } = "not_specified";
    public double? HeightCm { get; set; }
    public double? WeightKg { get; set; }
    public string Language { get; set; } = "system";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }
}

public class TrainingSession : ICreatedAt, IUpdatedAt
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly SessionDate { get; set; }
    public string MuscleGroup { get; set; } = "";
    public string DayType { get; set; } = "";
    public int DurationMinutes { get; set; }
    public string? AiNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }
    public List<SessionExercise> Exercises { get; set; } = [];
}

public class SessionExercise : ICreatedAt, IUpdatedAt
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string ExerciseName { get; set; } = "";
    public string Category { get; set; } = "";
    public int Sets { get; set; }
    public int Reps { get; set; }
    public double? Weight { get; set; }
    public string? Unit { get; set; }
    public string? Rationale { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public TrainingSession? Session { get; set; }
}

public class AiPlanRequest : ICreatedAt
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string RequestType { get; set; } = "";
    public string PromptSnapshot { get; set; } = "";
    public string? ResponseSnapshot { get; set; }
    public string Status { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }
}
