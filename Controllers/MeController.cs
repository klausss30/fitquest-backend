using FitQuest.Api.Data;
using FitQuest.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitQuest.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/me")]
public class MeController : ControllerBase
{
    private readonly AppDbContext _db;

    public MeController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var userId = this.CurrentUserId();
        var user = await _db.Users
            .Include(x => x.Profile)
            .FirstOrDefaultAsync(x => x.Id == userId, ct);

        if (user is null) return Unauthorized(new { error = "User not found or session expired" });

        return Ok(new
        {
            user = user.ToDto(),
            profile = user.Profile?.ToDto(),
        });
    }
}
