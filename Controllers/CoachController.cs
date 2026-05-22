using FitQuest.Api.Data;
using FitQuest.Api.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitQuest.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/coach")]
public class CoachController : ControllerBase
{
    private static readonly string[] MuscleGroups = ["legs", "chest", "back", "shoulders", "arms", "full_body"];
    private static readonly string[] DefaultOrder = ["full_body", "chest", "back", "shoulders", "arms", "legs"];

    private readonly AppDbContext _db;

    public CoachController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("week-message")]
    public async Task<IActionResult> GetWeekMessage(CancellationToken ct)
    {
        var userId = this.CurrentUserId();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var yesterday = today.AddDays(-1);
        var weekStart = StartOfWeek(today);
        var weekEnd = weekStart.AddDays(7);
        var lastWeekStart = weekStart.AddDays(-7);

        var user = await _db.Users
            .Include(x => x.Profile)
            .FirstOrDefaultAsync(x => x.Id == userId, ct);

        if (user is null) return Unauthorized(new { error = "用户不存在或登录已失效" });

        var sessions = await _db.TrainingSessions
            .Where(x => x.UserId == userId && x.SessionDate >= lastWeekStart && x.SessionDate < weekEnd)
            .OrderBy(x => x.SessionDate)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        var language = ResolveOutputLanguage(Request.Headers.AcceptLanguage.ToString(), user.Profile?.Language);
        var yesterdaySession = sessions.LastOrDefault(x => x.SessionDate == yesterday);
        var todaySession = sessions.LastOrDefault(x => x.SessionDate == today);
        var thisWeekSessions = sessions
            .Where(x => x.SessionDate >= weekStart && x.SessionDate < weekEnd)
            .ToList();
        var lastWeekSessions = sessions
            .Where(x => x.SessionDate >= lastWeekStart && x.SessionDate < weekStart)
            .ToList();

        var todayFocus = todaySession?.MuscleGroup ?? ChooseTodayFocus(user.Profile, sessions, thisWeekSessions);
        var message = BuildMessage(
            user.Profile,
            yesterdaySession,
            todaySession,
            thisWeekSessions.Count,
            lastWeekSessions.Count,
            todayFocus,
            language);

        return Ok(new { message });
    }

    private static string BuildMessage(
        UserProfile? profile,
        TrainingSession? yesterdaySession,
        TrainingSession? todaySession,
        int thisWeekCount,
        int lastWeekCount,
        string todayFocus,
        string language)
    {
        if (language == "zh")
        {
            var goal = GoalText(profile?.Goal, language);
            var focus = MuscleGroupText(todayFocus, language);

            if (todaySession is not null)
                return $"今天{MuscleGroupText(todaySession.MuscleGroup, language)}已完成，节奏很稳，接下来好好恢复。";

            if (yesterdaySession is not null)
                return $"昨天{MuscleGroupText(yesterdaySession.MuscleGroup, language)}完成了，今天把{focus}接上，稳稳推进。";

            if (thisWeekCount > 0)
                return $"本周已完成{thisWeekCount}练，今天聚焦{focus}，继续把{goal}节奏拉起来。";

            if (lastWeekCount > 0)
                return $"上周完成{lastWeekCount}练，今天从{focus}开始，把{goal}节奏接回来。";

            return $"从今天开始建立{goal}节奏，先完成一练，身体会跟上来。";
        }

        var enGoal = GoalText(profile?.Goal, language);
        var enFocus = MuscleGroupText(todayFocus, language);

        if (todaySession is not null)
            return $"You finished {MuscleGroupText(todaySession.MuscleGroup, language)} today. Recover well and keep the rhythm alive.";

        if (yesterdaySession is not null)
            return $"Yesterday's {MuscleGroupText(yesterdaySession.MuscleGroup, language)} work is in. Today, let's move into {enFocus} with control.";

        if (thisWeekCount > 0)
            return $"You have {thisWeekCount} session this week. Today, focus on {enFocus} and keep building {enGoal}.";

        if (lastWeekCount > 0)
            return $"Last week had {lastWeekCount} session. Start today with {enFocus} and rebuild the rhythm.";

        return $"Start the week with {enFocus}. One solid session is enough to build momentum.";
    }

    private static string ChooseTodayFocus(UserProfile? profile, List<TrainingSession> allSessions, List<TrainingSession> thisWeekSessions)
    {
        var weeklyTarget = DetermineWeeklyTarget(profile);
        if (thisWeekSessions.Count >= weeklyTarget) return "rest";

        var completedThisWeek = thisWeekSessions
            .Select(x => x.MuscleGroup)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = MuscleGroups
            .Where(x => !completedThisWeek.Contains(x))
            .ToList();

        if (candidates.Count == 0)
            candidates = MuscleGroups.ToList();

        if (allSessions.Count == 0)
            return candidates.OrderBy(DefaultRank).First();

        return candidates
            .Select(group => new
            {
                Group = group,
                LastDate = allSessions
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
            "fat_loss" => Math.Min(5, baseTarget + 1),
            "strength" => Math.Max(3, baseTarget),
            _ => baseTarget,
        };
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-diff);
    }

    private static string ResolveOutputLanguage(string? acceptLanguage, string? profileLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage))
            return ResolveStoredLanguage(profileLanguage);

        var firstLanguage = acceptLanguage
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()
            ?.ToLowerInvariant();

        if (firstLanguage is null) return "zh";
        return firstLanguage.StartsWith("zh") ? "zh" : "en";
    }

    private static string ResolveStoredLanguage(string? profileLanguage)
    {
        return profileLanguage switch
        {
            "zh-CN" => "zh",
            "en-US" => "en",
            _ => "zh",
        };
    }

    private static string GoalText(string? goal, string language)
    {
        if (language == "zh")
        {
            return goal switch
            {
                "strength" => "力量",
                "fat_loss" => "减脂",
                "muscle_gain" => "增肌",
                "general_fitness" => "健康训练",
                _ => "训练",
            };
        }

        return goal switch
        {
            "strength" => "strength",
            "fat_loss" => "fat loss",
            "muscle_gain" => "muscle gain",
            "general_fitness" => "fitness",
            _ => "training",
        };
    }

    private static string MuscleGroupText(string muscleGroup, string language)
    {
        if (language == "zh")
        {
            return muscleGroup switch
            {
                "legs" => "腿部",
                "chest" => "胸部",
                "back" => "背部",
                "shoulders" => "肩部",
                "arms" => "手臂",
                "full_body" => "全身",
                "rest" => "恢复",
                _ => "训练",
            };
        }

        return muscleGroup switch
        {
            "legs" => "lower body",
            "chest" => "chest",
            "back" => "back",
            "shoulders" => "shoulders",
            "arms" => "arms",
            "full_body" => "full-body",
            "rest" => "recovery",
            _ => "training",
        };
    }

    private static int DefaultRank(string muscleGroup)
    {
        var rank = Array.IndexOf(DefaultOrder, muscleGroup);
        return rank < 0 ? int.MaxValue : rank;
    }
}
