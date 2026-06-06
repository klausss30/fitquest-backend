using FitQuest.Api.Data;
using FitQuest.Api.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitQuest.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/checkin")]
public class CheckInController : ControllerBase
{
    private readonly AppDbContext _db;

    public CheckInController(AppDbContext db)
    {
        _db = db;
    }

    // POST /api/checkin
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] CheckInRequest request, CancellationToken ct)
    {
        var userId = this.CurrentUserId();

        if (request.SleepHours is < 0 or > 24)
            return BadRequest(new { error = "sleep_hours must be between 0 and 24" });
        if (request.EnergyLevel is < 1 or > 10)
            return BadRequest(new { error = "energy_level must be between 1 and 10" });
        if (request.StressLevel is < 1 or > 10)
            return BadRequest(new { error = "stress_level must be between 1 and 10" });

        var date = request.Date ?? DateOnly.FromDateTime(DateTime.Today);

        // Load recent training load for recovery score calculation
        var recentSessions = await _db.TrainingSessions
            .Where(x => x.UserId == userId && x.SessionDate >= date.AddDays(-3) && x.SessionDate < date)
            .ToListAsync(ct);

        var recoveryScore = ComputeRecoveryScore(
            request.SleepHours,
            request.EnergyLevel,
            request.StressLevel,
            recentSessions.Count);

        // Upsert: one check-in per day per user
        var existing = await _db.DailyCheckIns
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Date == date, ct);

        if (existing is not null)
        {
            existing.SleepHours = request.SleepHours;
            existing.EnergyLevel = request.EnergyLevel;
            existing.StressLevel = request.StressLevel;
            existing.WeightKg = request.WeightKg;
            existing.Notes = request.Notes?.Trim();
            existing.RecoveryScore = recoveryScore;
        }
        else
        {
            _db.DailyCheckIns.Add(new DailyCheckIn
            {
                UserId = userId,
                Date = date,
                SleepHours = request.SleepHours,
                EnergyLevel = request.EnergyLevel,
                StressLevel = request.StressLevel,
                WeightKg = request.WeightKg,
                Notes = request.Notes?.Trim(),
                RecoveryScore = recoveryScore,
            });
        }

        await _db.SaveChangesAsync(ct);

        return Ok(new CheckInResponse(
            date,
            request.SleepHours,
            request.EnergyLevel,
            request.StressLevel,
            request.WeightKg,
            request.Notes,
            recoveryScore,
            DescribeRecovery(recoveryScore)));
    }

    // GET /api/checkin/today
    [HttpGet("today")]
    public async Task<IActionResult> GetToday(CancellationToken ct)
    {
        var userId = this.CurrentUserId();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var checkIn = await _db.DailyCheckIns
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Date == today, ct);

        if (checkIn is null)
            return Ok(new { exists = false });

        return Ok(new
        {
            exists = true,
            checkin = ToResponse(checkIn),
        });
    }

    // GET /api/checkin/history?days=7
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int days = 7, CancellationToken ct = default)
    {
        var userId = this.CurrentUserId();
        var since = DateOnly.FromDateTime(DateTime.Today).AddDays(-Math.Abs(days));

        var checkIns = await _db.DailyCheckIns
            .Where(x => x.UserId == userId && x.Date >= since)
            .OrderByDescending(x => x.Date)
            .ToListAsync(ct);

        return Ok(new { checkins = checkIns.Select(ToResponse) });
    }

    // ── Recovery Score Algorithm ─────────────────────────────────────────────
    //
    // Four inputs weighted and summed into 0–100:
    //   Sleep (40%) — optimal ≥ 8h; penalised below 6h
    //   Energy (25%) — direct 1–10 scale
    //   Stress (25%) — inverted 1–10 (low stress = high recovery)
    //   Training load (10%) — penalised for sessions in last 3 days
    //
    private static int ComputeRecoveryScore(
        double sleepHours,
        int energyLevel,
        int stressLevel,
        int recentSessionCount)
    {
        // Sleep: 0–40 points
        double sleepScore = sleepHours switch
        {
            >= 8 => 40,
            >= 7 => 35,
            >= 6 => 25,
            >= 5 => 14,
            _ => 5,
        };

        // Energy: 0–25 points  (scale 1–10 → 0–25)
        double energyScore = (energyLevel - 1) / 9.0 * 25;

        // Stress: 0–25 points  (inverted: stress 1 → 25pts, stress 10 → 0pts)
        double stressScore = (10 - stressLevel) / 9.0 * 25;

        // Training load: 0–10 points  (no recent sessions = 10pts)
        double loadScore = recentSessionCount switch
        {
            0 => 10,
            1 => 7,
            2 => 3,
            _ => 0,
        };

        return (int)Math.Round(Math.Clamp(sleepScore + energyScore + stressScore + loadScore, 0, 100));
    }

    private static string DescribeRecovery(int score) => score switch
    {
        >= 80 => "excellent",
        >= 60 => "good",
        >= 40 => "moderate",
        >= 20 => "low",
        _ => "poor",
    };

    private static CheckInResponse ToResponse(DailyCheckIn c) => new(
        c.Date,
        c.SleepHours,
        c.EnergyLevel,
        c.StressLevel,
        c.WeightKg,
        c.Notes,
        c.RecoveryScore,
        DescribeRecovery(c.RecoveryScore));
}

// ── Request / Response models ─────────────────────────────────────────────────

public record CheckInRequest(
    DateOnly? Date,
    double SleepHours,
    int EnergyLevel,
    int StressLevel,
    double? WeightKg,
    string? Notes);

public record CheckInResponse(
    DateOnly Date,
    double SleepHours,
    int EnergyLevel,
    int StressLevel,
    double? WeightKg,
    string? Notes,
    int RecoveryScore,
    string RecoveryStatus);
