using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CQPulse.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API in memory against an isolated <see cref="TestDatabase"/>.
/// Runs in the "Testing" environment, so user secrets and the development seeders are never loaded.
/// </summary>
public sealed class CQPulseApiFactory(TestDatabase database) : WebApplicationFactory<Program>
{
    public const string EnvironmentName = "Testing";

    /// <summary>A per-fixture random signing key; never a real secret.</summary>
    public string JwtSigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("ConnectionStrings:CQPulse", database.ConnectionString);
        builder.UseSetting("Jwt:SigningKey", JwtSigningKey);
        // In-memory requests share one rate-limit partition (no client IP); tests that need the limit set it explicitly.
        builder.UseSetting("RateLimiting:Login:PermitLimit", "100000");
        // This shared host has no LogTriageAssistant configured; it must not sweep up Pending analyses that a test's own
        // configured host is about to process (tests that exercise the sweep configure their host's interval).
        builder.UseSetting("Integrations:LogTriage:PendingSweepSeconds", "86400");
    }
}
