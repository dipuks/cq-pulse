using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace CQPulse.Api.Auth;

/// <summary>Fixed-window limit on sign-in attempts per client IP ("RateLimiting:Login" configuration section).</summary>
public sealed class LoginRateLimit
{
    public const string PolicyName = "login";
    public const string SectionName = "RateLimiting:Login";

    public int PermitLimit { get; init; } = 10;
    public int WindowSeconds { get; init; } = 60;

    public static void Configure(RateLimiterOptions options, LoginRateLimit settings)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy(PolicyName, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = settings.PermitLimit,
                Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                QueueLimit = 0
            }));
    }
}
