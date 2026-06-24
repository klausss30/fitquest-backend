using FitQuest.Api.Data;
using FitQuest.Api.Data.Entities;
using FitQuest.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitQuest.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/week-plan")]
public class WeekPlanController : ControllerBase
{
    private readonly AppDbContext _db;

    public WeekPlanController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetWeekPlan([FromQuery(Name = "start_date")] DateOnly? startDate, CancellationToken ct)
    {
        var userId = this.CurrentUserId();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var weekStart = StartOfWeek(startDate ?? today);
        var weekEnd = weekStart.AddDays(7);
        var responseStart = today > weekStart ? today : weekStart;

        if (responseStart >= weekEnd)
        {
            return Ok(new
            {
                week_start = weekStart,
                days = Array.Empty<WeekPlanDayDto>(),
            });
        }

        var user = await _db.Users
            .Include(x => x.Profile)
            .FirstOrDefaultAsync(x => x.Id == userId, ct);

        if (user is null) return Unauthorized(new { error = "User not found or session expired" });

        var historyStart = weekStart.AddDays(-7);
        var sessions = await _db.TrainingSessions
            .Where(x => x.UserId == userId && x.SessionDate >= historyStart && x.SessionDate < weekEnd)
            .OrderBy(x => x.SessionDate)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        var days = BuildWeekPlan(user.Profile, sessions, weekStart, weekEnd, responseStart);

        return Ok(new
        {
            week_start = weekStart,
            days,
        });
    }

    private static List<WeekPlanDayDto> BuildWeekPlan(
        UserProfile? profile,
        List<TrainingSession> sessions,
        DateOnly weekStart,
        DateOnly weekEnd,
        DateOnly responseStart)
    {
        var result = new List<WeekPlanDayDto>();
        var completedThisWeek = sessions
            .Where(x => x.SessionDate >= weekStart && x.SessionDate < weekEnd)
            .ToList();
        var completedByDate = completedThisWeek
            .GroupBy(x => x.SessionDate)
            .ToDictionary(x => x.Key, x => x.Last());

        var weeklyTarget = DetermineWeeklyTarget(profile);
        var plannedTrainingDays = completedThisWeek.Count;
        for (var date = responseStart; date < weekEnd; date = date.AddDays(1))
        {
            if (completedByDate.TryGetValue(date, out var completedSession))
            {
                result.Add(new WeekPlanDayDto(
                    date,
                    completedSession.MuscleGroup,
                    completedSession.DayType,
                    "You already completed a session on this day, so the plan uses your actual training record."));
                continue;
            }

            var remainingDaysIncludingToday = CountDays(date, weekEnd);
            var remainingTrainingNeeded = Math.Max(0, weeklyTarget - plannedTrainingDays);
            var shouldTrain = remainingTrainingNeeded > 0 && ShouldTrainToday(result, date, remainingDaysIncludingToday, remainingTrainingNeeded);

            if (!shouldTrain)
            {
                result.Add(RestDay(date));
                continue;
            }

            var muscleGroup = ChooseMuscleGroup(sessions, result, date);
            result.Add(TrainingDay(date, muscleGroup, profile));
            plannedTrainingDays++;
        }

        return result;
    }

    private static int DetermineWeeklyTarget(UserProfile? profile)
    {
        var baseTarget = profile?.ExperienceLevel switch
        {
            "advanced" => 5,
            "intermediate" => 4,
            _ => 3,
        };

        return profile?.Goal switch
        {
            "strength" => Math.Max(3, baseTarget),
            "fat_loss" => Math.Min(5, baseTarget + 1),
            _ => baseTarget,
        };
    }

    private static bool ShouldTrainToday(List<WeekPlanDayDto> plannedDays, DateOnly date, int remainingDays, int remainingTrainingNeeded)
    {
        if (remainingTrainingNeeded >= remainingDays) return true;

        var previousDay = plannedDays.LastOrDefault();
        if (previousDay is not null
            && previousDay.SessionDate == date.AddDays(-1)
            && previousDay.MuscleGroup != "rest"
            && remainingTrainingNeeded < remainingDays)
        {
            return false;
        }

        return true;
    }

    private static string ChooseMuscleGroup(List<TrainingSession> sessions, List<WeekPlanDayDto> plannedDays, DateOnly date)
    {
        var alreadyPlannedOrCompletedThisWeek = sessions
            .Where(x => x.SessionDate >= StartOfWeek(date) && x.SessionDate <= date)
            .Select(x => x.MuscleGroup)
            .Concat(plannedDays.Where(x => x.MuscleGroup != "rest").Select(x => x.MuscleGroup))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = ControllerHelpers.MuscleGroups
            .Where(x => !alreadyPlannedOrCompletedThisWeek.Contains(x))
            .ToList();

        if (candidates.Count == 0)
            candidates = ControllerHelpers.MuscleGroups.ToList();

        if (sessions.Count == 0)
            return candidates.OrderBy(DefaultRank).First();

        return candidates
            .Select(group => new
            {
                Group = group,
                LastDate = sessions
                    .Where(x => string.Equals(x.MuscleGroup, group, StringComparison.OrdinalIgnoreCase))
                    .Select(x => (DateOnly?)x.SessionDate)
                    .Max(),
            })
            .OrderBy(x => x.LastDate.HasValue ? 1 : 0)
            .ThenBy(x => x.LastDate ?? DateOnly.MinValue)
            .ThenBy(x => DefaultRank(x.Group))
            .First()
            .Group;
    }

    private static WeekPlanDayDto TrainingDay(DateOnly date, string muscleGroup, UserProfile? profile)
    {
        var dayType = GetDayType(muscleGroup, profile?.Goal);
        var reason = GetTrainingReason(muscleGroup, profile);
        return new WeekPlanDayDto(date, muscleGroup, dayType, reason);
    }

    private static WeekPlanDayDto RestDay(DateOnly date)
    {
        return new WeekPlanDayDto(
            date,
            "rest",
            "Rest Day",
            "This day is set aside for recovery so your body can absorb the work and keep the next sessions strong.");
    }

    private static string GetDayType(string muscleGroup, string? goal)
    {
        var focus = goal switch
        {
            "strength" => "Strength Day",
            "fat_loss" => "Efficient Training Day",
            _ => "Steady Progress Day",
        };

        return muscleGroup switch
        {
            "legs" => $"Lower Body - {focus}",
            "chest" => $"Chest - {focus}",
            "back" => $"Back - {focus}",
            "shoulders" => $"Shoulders - {focus}",
            "arms" => $"Arms - {focus}",
            "full_body" => $"Full Body - {focus}",
            _ => "Rest Day",
        };
    }

    private static string GetTrainingReason(string muscleGroup, UserProfile? profile)
    {
        var goalReason = profile?.Goal switch
        {
            "strength" => "Based on your strength goal, today should focus on controlled intensity and clean movement quality.",
            "fat_loss" => "Based on your fat-loss goal, this direction keeps the session efficient while leaving room for recovery.",
            "muscle_gain" => "Based on your muscle-gain goal, this gives you a useful stimulus without stacking unnecessary fatigue.",
            _ => "Based on your profile and recent history, this direction keeps the week moving in a sustainable way.",
        };

        return muscleGroup == "full_body"
            ? $"{goalReason} A full-body session helps you build momentum for the week."
            : $"{goalReason} This muscle group has a more reasonable recovery window for this day.";
    }

    private static int CountDays(DateOnly start, DateOnly end)
    {
        var count = 0;
        for (var date = start; date < end; date = date.AddDays(1)) count++;
        return count;
    }

    private static DateOnly StartOfWeek(DateOnly date) =>
        ControllerHelpers.StartOfWeek(date);

    private static int DefaultRank(string muscleGroup)
    {
        var rank = Array.IndexOf(ControllerHelpers.DefaultMuscleGroupOrder, muscleGroup);
        return rank < 0 ? int.MaxValue : rank;
    }
}
