using FitQuest.Api.Data;
using FitQuest.Api.Data.Entities;
using FitQuest.Api.Models;
using FitQuest.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace FitQuest.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _tokens;
    private readonly ILogger<AuthController> _logger;

    public AuthController(AppDbContext db, JwtTokenService tokens, ILogger<AuthController> logger)
    {
        _db = db;
        _tokens = tokens;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        var name = (req.Name ?? "").Trim();
        var email = (req.Email ?? "").Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { error = "Name is required" });
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return BadRequest(new { error = "Please enter a valid email address" });
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
            return BadRequest(new { error = "Password must be at least 6 characters" });
        _logger.LogInformation("Register validation passed");
        if (await _db.Users.AnyAsync(x => x.Email == email, ct))
            return BadRequest(new { error = "Email already registered" });
        _logger.LogInformation("Register email uniqueness check passed");

        var user = new User
        {
            Name = name,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
        };
        _logger.LogInformation("Register password hashed");

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Register user saved with id {UserId}", user.Id);

        var token = _tokens.CreateToken(user);
        _logger.LogInformation("Register token created for user {UserId}", user.Id);

        return Ok(new
        {
            user = user.ToDto(),
            token,
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(req.Password))
            return Unauthorized(new { error = "Incorrect email or password" });
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == email, ct);

        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { error = "Incorrect email or password" });

        return Ok(new
        {
            user = user.ToDto(),
            token = _tokens.CreateToken(user),
        });
    }
}
