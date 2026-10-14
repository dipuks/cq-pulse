using System.Net.Http.Headers;
using System.Net.Http.Json;
using CQPulse.Infrastructure.Identity;
using CQPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CQPulse.IntegrationTests.Infrastructure;

public sealed record TestUser(string Email, string Password, string? Role);

/// <summary>
/// Shared per-collection fixture: one migrated test database, one in-memory API, and seeded users.
/// The database is dropped when the collection finishes, even if tests fail.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string AdministratorRole = "Administrator";
    private static readonly Uri BaseAddress = new("https://localhost");

    private TestDatabase? _database;
    private CQPulseApiFactory? _factory;

    public TestDatabase Database => _database ?? throw new InvalidOperationException("Fixture not initialized.");
    public CQPulseApiFactory Factory => _factory ?? throw new InvalidOperationException("Fixture not initialized.");

    /// <summary>Holds the Administrator role, as every data endpoint requires.</summary>
    public TestUser Administrator { get; } = NewUser("admin", AdministratorRole);

    /// <summary>Authenticated but without any role; used for 403 checks.</summary>
    public TestUser NonAdministrator { get; } = NewUser("viewer", null);

    public async Task InitializeAsync()
    {
        // Database must exist before the host starts: startup recovery queries TestRuns immediately.
        _database = await TestDatabase.CreateAsync();
        _factory = new CQPulseApiFactory(_database);
        await SeedUsersAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (_factory is not null) await _factory.DisposeAsync();
        }
        finally
        {
            if (_database is not null) await _database.DisposeAsync();
        }
    }

    public HttpClient CreateAnonymousClient() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = BaseAddress });

    /// <summary>Signs in through the real /api/auth/login endpoint and returns a client carrying the bearer token.</summary>
    public async Task<HttpClient> CreateClientAsync(TestUser user)
    {
        var client = CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(user));
        return client;
    }

    public Task<HttpClient> CreateAdministratorClientAsync() => CreateClientAsync(Administrator);

    public async Task<string> GetTokenAsync(TestUser user)
    {
        using var client = CreateAnonymousClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { user.Email, user.Password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException("Login returned an empty body.");
        return body.AccessToken;
    }

    /// <summary>Runs work against the API's own DI container (same database, same configuration).</summary>
    public async Task<T> WithDbContextAsync<T>(Func<CQPulseDbContext, Task<T>> work)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<CQPulseDbContext>());
    }

    private async Task SeedUsersAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (!await roles.RoleExistsAsync(AdministratorRole))
            Ensure(await roles.CreateAsync(new IdentityRole(AdministratorRole)));

        foreach (var testUser in new[] { Administrator, NonAdministrator })
        {
            var user = new ApplicationUser { UserName = testUser.Email, Email = testUser.Email, EmailConfirmed = true, DisplayName = testUser.Email };
            Ensure(await users.CreateAsync(user, testUser.Password));
            if (testUser.Role is not null) Ensure(await users.AddToRoleAsync(user, testUser.Role));
        }
    }

    private static TestUser NewUser(string name, string? role) =>
        // Random per fixture; satisfies the Identity password policy (length, digit, upper, lower, symbol).
        new($"{name}-{Guid.NewGuid():N}@cqpulse.test", $"Tt1!{Guid.NewGuid():N}", role);

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
    }

    private sealed record LoginResponse(string AccessToken);
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "API";
}
