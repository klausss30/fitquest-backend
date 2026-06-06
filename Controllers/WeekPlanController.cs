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

        var language = ResolveOutputLanguage(Request.Headers.AcceptLanguage.ToString(), user.Profile?.Language);
        var days = BuildWeekPlan(user.Profile, sessions, weekStart, weekEnd, responseStart, language);

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
        DateOnly responseStart,
        string language)
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
        var lastMuscleGroupByDate = sessions
            .GroupBy(x => x.MuscleGroup)
            .ToDictionary(x => x.Key, x => x.Max(s => s.SessionDate));

        for (var date = responseStart; date < weekEnd; date = date.AddDays(1))
        {
            if (completedByDate.TryGetValue(date, out var completedSession))
            {
                result.Add(new WeekPlanDayDto(
                    date,
                    completedSession.MuscleGroup,
                    completedSession.DayType,
                    Text(language,
                        "这天已经有完成记录，周安排以你的实际训练为准。",
                        "You already completed a session on this day, so the plan uses your actual training record.")));
                continue;
            }

            var remainingDaysIncludingToday = CountDays(date, weekEnd);
            var remainingTrainingNeeded = Math.Max(0, weeklyTarget - plannedTrainingDays);
            var shouldTrain = remainingTrainingNeeded > 0 && ShouldTrainToday(result, date, remainingDaysIncludingToday, remainingTrainingNeeded);

            if (!shouldTrain)
            {
                result.Add(RestDay(date, language));
                continue;
            }

            var muscleGroup = ChooseMuscleGroup(sessions, result, date);
            result.Add(TrainingDay(date, muscleGroup, profile, language));
            plannedTrainingDays++;
            lastMuscleGroupByDate[muscleGroup] = date;
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

    private static WeekPlanDayDto TrainingDay(DateOnly date, string muscleGroup, UserProfile? profile, string language)
    {
        var dayType = GetDayType(muscleGroup, profile?.Goal, language);
        var reason = GetTrainingReason(muscleGroup, profile, language);
        return new WeekPlanDayDto(date, muscleGroup, dayType, reason);
    }

    private static WeekPlanDayDto RestDay(DateOnly date, string language)
    {
        return new WeekPlanDayDto(
            date,
            "rest",
            Text(language, "恢复日", "Rest Day"),
            Text(language,
                "这天安排恢复，让身体把前面的训练吸收掉，后面的训练质量会更稳。",
                "This day is set aside for recovery so your body can absorb the work and keep the next sessions strong."));
    }

    private static string GetDayType(string muscleGroup, string? goal, string language)
    {
        if (language == "zh")
        {
            var focus = goal switch
            {
                "strength" => "力量日",
                "fat_loss" => "高效训练日",
                _ => "稳步提升日",
            };

            return muscleGroup switch
            {
                "legs" => $"腿部 · {focus}",
                "chest" => $"胸部 · {focus}",
                "back" => $"背部 · {focus}",
                "shoulders" => $"肩部 · {focus}",
                "arms" => $"手臂 · {focus}",
                "full_body" => $"全身 · {focus}",
                _ => "恢复日",
            };
        }

        var enFocus = goal switch
        {
            "strength" => "Strength Day",
            "fat_loss" => "Efficient Training Day",
            _ => "Steady Progress Day",
        };

        return muscleGroup switch
        {
            "legs" => $"Lower Body · {enFocus}",
            "chest" => $"Chest · {enFocus}",
            "back" => $"Back · {enFocus}",
            "shoulders" => $"Shoulders · {enFocus}",
            "arms" => $"Arms · {enFocus}",
            "full_body" => $"Full Body · {enFocus}",
            _ => "Rest Day",
        };
    }

    private static string GetTrainingReason(string muscleGroup, UserProfile? profile, string language)
    {
        if (language == "zh")
        {
            var goalText = profile?.Goal switch
            {
                "strength" => "结合你的力量目标，今天适合把重点放在可控强度和动作质量上。",
                "fat_loss" => "结合你的减脂目标，今天安排更高效的训练方向，同时保留恢复空间。",
                "muscle_gain" => "结合你的增肌目标，今天安排足够刺激但不过度堆量的训练方向。",
                _ => "根据你的训练画像和最近记录，今天安排这个方向更利于持续推进。",
            };

            return muscleGroup == "full_body"
                ? $"{goalText} 全身训练能帮你稳稳进入节奏。"
                : $"{goalText} 这个部位近期恢复压力更合理，适合安排在这一天。";
        }

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

    private static string ResolveOutputLanguage(string? acceptLanguage, string? profileLanguage) =>
        ControllerHelpers.ResolveOutputLanguage(acceptLanguage, profileLanguage);

    private static string Text(string language, string zh, string en)
        => language == "zh" ? zh : en;

    private static int DefaultRank(string muscleGroup)
    {
        var rank = Array.IndexOf(ControllerHelpers.DefaultMuscleGroupOrder, muscleGroup);
        return rank < 0 ? int.MaxValue : rank;
    }
}
