using FitQuest.Api.Data;
using FitQuest.Api.Data.Entities;
using FitQuest.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitQuest.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/training-sessions")]
public class TrainingSessionsController : ControllerBase
{
    private static readonly string[] MuscleGroups = ["legs", "chest", "back", "shoulders", "arms", "full_body"];
    private static readonly string[] Categories = ["warmup", "main", "accessory", "finisher", "cooldown"];
    private static readonly string[] Units = ["kg", "lb"];

    private readonly AppDbContext _db;

    public TrainingSessionsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    public async Task<IActionResult> CreateCompletedSession([FromBody] CreateTrainingSessionRequest request, CancellationToken ct)
    {
        var validationError = ValidateCreateRequest(request);
        if (validationError is not null) return BadRequest(new { error = validationError });

        var userId = this.CurrentUserId();

        var session = new TrainingSession
        {
            UserId = userId,
            SessionDate = request.SessionDate,
            MuscleGroup = Normalize(request.MuscleGroup),
            DayType = request.DayType.Trim(),
            DurationMinutes = request.DurationMinutes,
            AiNote = request.AiNote,
        };

        var exercises = request.Exercises.Select((exercise, index) => new SessionExercise
        {
            ExerciseName = exercise.ExerciseName.Trim(),
            Category = Normalize(exercise.Category),
            Sets = exercise.Sets,
            Reps = exercise.Reps,
            Weight = exercise.Weight,
            Unit = string.IsNullOrWhiteSpace(exercise.Unit) ? null : Normalize(exercise.Unit),
            Rationale = exercise.Rationale,
            SortOrder = index + 1,
        }).ToList();

        session.Exercises = exercises;
        _db.TrainingSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            session = session.ToDto(),
            exercises = exercises.OrderBy(x => x.SortOrder).Select(x => x.ToDto()),
        });
    }

    [HttpGet("week")]
    public async Task<IActionResult> GetWeek([FromQuery(Name = "start_date")] DateOnly? startDate, CancellationToken ct)
    {
        var userId = this.CurrentUserId();
        var weekStart = startDate ?? StartOfWeek(DateOnly.FromDateTime(DateTime.Today));
        var weekEnd = weekStart.AddDays(7);

        var sessions = await _db.TrainingSessions
            .Where(x => x.UserId == userId && x.SessionDate >= weekStart && x.SessionDate < weekEnd)
            .OrderBy(x => x.SessionDate)
            .Select(x => new SessionSummaryDto(
                x.Id,
                x.SessionDate,
                x.MuscleGroup,
                x.DayType,
                x.DurationMinutes,
                x.AiNote,
                x.Exercises.Count))
            .ToListAsync(ct);

        return Ok(new
        {
            week_start = weekStart,
            sessions,
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var userId = this.CurrentUserId();
        var session = await _db.TrainingSessions
            .Include(x => x.Exercises)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

        if (session is null) return NotFound(new { error = "训练记录不存在" });

        return Ok(new
        {
            session = session.ToDto(),
            exercises = session.Exercises
                .OrderBy(x => x.SortOrder)
                .Select(x => x.ToDto()),
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetHistory([FromQuery] int? limit, CancellationToken ct)
    {
        var userId = this.CurrentUserId();
        var take = Math.Clamp(limit ?? 20, 1, 100);

        var sessions = await _db.TrainingSessions
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.SessionDate)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .Select(x => new SessionSummaryDto(
                x.Id,
                x.SessionDate,
                x.MuscleGroup,
                x.DayType,
                x.DurationMinutes,
                x.AiNote,
                x.Exercises.Count))
            .ToListAsync(ct);

        return Ok(new { sessions });
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-diff);
    }

    private static string? ValidateCreateRequest(CreateTrainingSessionRequest? request)
    {
        if (request is null) return "请求不能为空";

        var muscleGroup = Normalize(request.MuscleGroup);
        if (!MuscleGroups.Contains(muscleGroup))
            return "muscle_group 必须是 legs / chest / back / shoulders / arms / full_body";
        if (string.IsNullOrWhiteSpace(request.DayType))
            return "day_type 不能为空";
        if (request.DurationMinutes <= 0)
            return "duration_minutes 必须大于 0";
        if (request.Exercises is null || request.Exercises.Count == 0)
            return "exercises 不能为空";

        foreach (var exercise in request.Exercises)
        {
            if (string.IsNullOrWhiteSpace(exercise.ExerciseName))
                return "exercise_name 不能为空";
            if (!Categories.Contains(Normalize(exercise.Category)))
                return "category 必须是 warmup / main / accessory / finisher / cooldown";
            if (exercise.Sets <= 0 || exercise.Reps <= 0)
                return "sets 和 reps 必须大于 0";
            if (exercise.Weight.HasValue && exercise.Weight < 0)
                return "weight 不能小于 0";
            if (!string.IsNullOrWhiteSpace(exercise.Unit) && !Units.Contains(Normalize(exercise.Unit)))
                return "unit 必须是 kg / lb / null";
        }

        return null;
    }

    private static string Normalize(string? value)
        => (value ?? "").Trim().ToLowerInvariant();
}
