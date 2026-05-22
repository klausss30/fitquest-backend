using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FitQuest.Api.Data.Entities;
using Microsoft.IdentityModel.Tokens;

namespace FitQuest.Api.Services;

public class JwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string CreateToken(User user)
    {
        var secret = GetJwtSecret(_configuration);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email),
        };

        var token = new JwtSecurityToken(
            issuer: GetJwtIssuer(_configuration),
            audience: GetJwtAudience(_configuration),
            claims: claims,
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string GetJwtSecret(IConfiguration configuration)
        => Environment.GetEnvironmentVariable("JWT_SECRET")
            ?? configuration["Jwt:Secret"]
            ?? "dev-only-secret-change-me-dev-only-secret";

    public static string GetJwtIssuer(IConfiguration configuration)
        => Environment.GetEnvironmentVariable("JWT_ISSUER")
            ?? configuration["Jwt:Issuer"]
            ?? "FitQuest.Api";

    public static string GetJwtAudience(IConfiguration configuration)
        => Environment.GetEnvironmentVariable("JWT_AUDIENCE")
            ?? configuration["Jwt:Audience"]
            ?? "FitQuest.Client";
}
