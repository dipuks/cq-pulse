using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CQPulse.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CQPulse.Api.Auth;

public sealed class TokenService(
    UserManager<ApplicationUser> userManager,
    IOptions<JwtOptions> options)
{
    public async Task<(string Token, DateTimeOffset ExpiresAtUtc)> CreateAsync(ApplicationUser user)
    {
        var jwt = options.Value;
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(jwt.ExpirationMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? user.Id)
        };
        claims.AddRange((await userManager.GetRolesAsync(user)).Select(role => new Claim(ClaimTypes.Role, role)));
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(jwt.Issuer, jwt.Audience, claims, expires: expiresAtUtc.UtcDateTime, signingCredentials: credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
