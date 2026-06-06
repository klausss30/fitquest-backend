using FitQuest.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitQuest.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/stats")]
public class StatsController : ControllerBase
{
    private readonly AppDbContext _db;

    public StatsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var userId = this.CurrentUserId();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var sevenDaysAgo = today.AddDays(-6);

        // All distinct training dates (for streak + weekly count)
        var sessionDates = await _db.TrainingSessions
            .Where(x => x.UserId == userId)
            .Select(x => x.SessionDate)
            .Distinct()
            .OrderByDescending(x => x)
            .ToListAsync(ct);

        var sessionsThisWeek = sessionDates.Count(d => d >= sevenDaysAgo && d <= today);

        var todayCheckIn = await _db.DailyCheckIns
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Date == today, ct);

        return Ok(new
        {
            streak              = CalculateStreak(sessionDates, today),
            sessions_this_week  = sessionsThisWeek,
            recovery_score      = todayCheckIn?.RecoveryScore,
            recovery_status     = todayCheckIn is not null
                ? DescribeRecoveryStatus(todayCheckIn.RecoveryScore)
                : null,
        });
    }

    /// <summary>
    /// Counts consecutive days (going back from today or yesterday) that have
    /// at least one completed training session.  If the user hasn't trained
    /// today yet we look back from yesterday so the streak doesn't break mid-day.
    /// </summary>
    private static int CalculateStreak(List<DateOnly> sessionDates, DateOnly today)
    {
        if (sessionDates.Count == 0) return 0;

        var dateSet = sessionDates.ToHashSet();
        var cursor  = today;

        // If nothing logged today, start counting from yesterday
        if (!dateSet.Contains(cursor))
            cursor = cursor.AddDays(-1);

        var streak = 0;
        while (dateSet.Contains(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }

        return streak;
    }

    private static string DescribeRecoveryStatus(int score) =>
        ControllerHelpers.DescribeRecoveryStatus(score);
}
