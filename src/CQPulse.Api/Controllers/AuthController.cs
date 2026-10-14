using CQPulse.Api.Auth;
using CQPulse.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CQPulse.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    TokenService tokenService,
    ILogger<AuthController> logger) : ControllerBase
{
    private static readonly object InvalidCredentials = new { message = "Invalid email or password." };

    /// <summary>
    /// Issues a JWT. Failed attempts count towards Identity lockout; a locked-out account gets the same response
    /// as wrong credentials so the endpoint does not reveal which accounts exist or are locked.
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting(LoginRateLimit.PolicyName)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null) return Unauthorized(InvalidCredentials);

        if (await userManager.IsLockedOutAsync(user))
        {
            logger.LogWarning("Sign-in rejected for locked-out user {UserId}.", user.Id);
            return Unauthorized(InvalidCredentials);
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            if (await userManager.IsLockedOutAsync(user))
                logger.LogWarning("User {UserId} locked out after repeated failed sign-ins.", user.Id);
            return Unauthorized(InvalidCredentials);
        }

        await userManager.ResetAccessFailedCountAsync(user);
        var token = await tokenService.CreateAsync(user);
        return Ok(new LoginResponse(token.Token, token.ExpiresAtUtc, new UserResponse(user.Id, user.Email, user.DisplayName)));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me()
    {
        var user = await userManager.GetUserAsync(User);
        return user is null ? Unauthorized() : Ok(new UserResponse(user.Id, user.Email, user.DisplayName));
    }
}

public sealed record LoginRequest(string Email, string Password);
public sealed record UserResponse(string Id, string? Email, string? DisplayName);
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, UserResponse User);
