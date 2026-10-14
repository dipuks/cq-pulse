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
    }
}
