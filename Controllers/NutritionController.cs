using FitQuest.Api.Data;
using FitQuest.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace FitQuest.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/nutrition")]
public class NutritionController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAiService   _ai;
    private readonly ILogger<NutritionController> _logger;

    public NutritionController(AppDbContext db, IAiService ai, ILogger<NutritionController> logger)
    {
        _db     = db;
        _ai     = ai;
        _logger = logger;
    }

    [HttpGet]
    [EnableRateLimiting("ai")]
    public async Task<IActionResult> GetNutrition(CancellationToken ct)
    {
        var userId = this.CurrentUserId();
        var today  = DateOnly.FromDateTime(DateTime.Today);

        var user = await _db.Users
            .Include(x => x.Profile)
            .FirstOrDefaultAsync(x => x.Id == userId, ct);

        if (user is null) return Unauthorized(new { error = "User not found or session expired" });

        // Sessions in the last 7 days
        var sevenDaysAgo    = today.AddDays(-6);
        var sessionsLast7   = await _db.TrainingSessions
            .Where(x => x.UserId == userId && x.SessionDate >= sevenDaysAgo && x.SessionDate <= today)
            .CountAsync(ct);

        var todayCheckIn = await _db.DailyCheckIns
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Date == today, ct);

        // Today's training session (if already completed) — feeds multi-agent coordination
        var todaySession = await _db.TrainingSessions
            .FirstOrDefaultAsync(x => x.UserId == userId && x.SessionDate == today, ct);

        var profile = user.Profile;

        var context = new AiNutritionPromptContext(
            new UserSnapshot(userId),
            profile is null ? null : new ProfileSnapshot(
                profile.ExperienceLevel,
                profile.Goal,
                profile.Gender,
                profile.HeightCm,
                profile.WeightKg),
            todayCheckIn is null ? null : new CheckInSnapshot(
                todayCheckIn.SleepHours,
                todayCheckIn.EnergyLevel,
                todayCheckIn.StressLevel,
                todayCheckIn.WeightKg,
                todayCheckIn.RecoveryScore,
                ControllerHelpers.DescribeRecoveryStatus(todayCheckIn.RecoveryScore),
                todayCheckIn.Notes),
            sessionsLast7,
            todaySession is null ? null : new TodayPlanSnapshot(
                todaySession.MuscleGroup,
                todaySession.DayType,
                todaySession.DurationMinutes,
                todaySession.AiNote));

        try
        {
            var result = await _ai.GenerateNutritionAsync(context, ct);
            var n = result.Nutrition;

            return Ok(new
            {
                daily_calories    = n.DailyCalories,
                protein_g         = n.ProteinG,
                carbs_g           = n.CarbsG,
                fat_g             = n.FatG,
                goal_note         = n.GoalNote,
                meal_suggestions  = n.MealSuggestions.Select(m => new
                {
                    meal             = m.Meal,
                    suggestion       = m.Suggestion,
                    calories_approx  = m.CaloriesApprox,
                }),
                reasoning = n.Reasoning,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate nutrition for user {UserId}", userId);
            return StatusCode(500, new { error = "Failed to generate nutrition advice. Please try again." });
        }
    }

}
