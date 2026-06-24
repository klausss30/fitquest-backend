using FitQuest.Api.Data;
using FitQuest.Api.Data.Entities;
using FitQuest.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitQuest.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/profile")]
public class ProfileController : ControllerBase
{
    private static readonly string[] ExperienceLevels = ["beginner", "intermediate", "advanced"];
    private static readonly string[] Goals = ["muscle_gain", "fat_loss", "strength"];
    private static readonly string[] Genders = ["male", "female", "not_specified"];

    private readonly AppDbContext _db;

    public ProfileController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPut]
    public async Task<IActionResult> Upsert([FromBody] UpsertProfileRequest req, CancellationToken ct)
    {
        var experienceLevel = (req.ExperienceLevel ?? "").Trim().ToLowerInvariant();
        var goal = (req.Goal ?? "").Trim().ToLowerInvariant();

        if (!ExperienceLevels.Contains(experienceLevel))
            return BadRequest(new { error = "experience_level must be one of: beginner / intermediate / advanced" });
        if (!Goals.Contains(goal))
            return BadRequest(new { error = "goal must be one of: muscle_gain / fat_loss / strength" });
        var gender = string.IsNullOrWhiteSpace(req.Gender) ? "not_specified" : req.Gender.Trim().ToLowerInvariant();
        if (!Genders.Contains(gender))
            return BadRequest(new { error = "gender must be one of: male / female / not_specified" });
        if (req.HeightCm.HasValue && req.HeightCm <= 0)
            return BadRequest(new { error = "height_cm must be greater than 0 or null" });
        if (req.WeightKg.HasValue && req.WeightKg <= 0)
            return BadRequest(new { error = "weight_kg must be greater than 0 or null" });

        var userId = this.CurrentUserId();
        var profile = await _db.UserProfiles.FirstOrDefaultAsync(x => x.UserId == userId, ct);

        if (profile is null)
        {
            profile = new UserProfile { UserId = userId };
            _db.UserProfiles.Add(profile);
        }

        profile.ExperienceLevel = experienceLevel;
        profile.Goal = goal;
        profile.Gender = gender;
        profile.HeightCm = req.HeightCm;
        profile.WeightKg = req.WeightKg;

        await _db.SaveChangesAsync(ct);

        return Ok(new { profile = profile.ToDto() });
    }
}
