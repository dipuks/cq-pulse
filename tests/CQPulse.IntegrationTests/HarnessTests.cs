using System.Net;
using System.Net.Http.Json;
using CQPulse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.IntegrationTests;

/// <summary>Verifies the integration harness itself: isolation, environment, and authentication helpers.</summary>
[Collection(ApiCollection.Name)]
public sealed class HarnessTests(ApiFixture fixture)
{
    [Fact]
    public async Task Api_uses_the_isolated_test_database()
    {
        var (databaseName, pendingMigrations) = await fixture.WithDbContextAsync(async db =>
            (db.Database.GetDbConnection().Database, (await db.Database.GetPendingMigrationsAsync()).ToList()));

        Assert.Equal(fixture.Database.Name, databaseName);
        Assert.True(TestDatabase.IsTestDatabaseName(databaseName));
        Assert.NotEqual("CQPulse", databaseName, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(pendingMigrations);
    }

    [Fact]
    public async Task Development_seed_data_is_not_loaded()
    {
        // Other tests in the collection create their own projects, so check for the dev seeder's records specifically.
        string[] seededProjects = ["Web Portal", "Mobile App", "API Services"];
        var (seededProjectCount, seededSuiteCount) = await fixture.WithDbContextAsync(async db =>
            (await db.Projects.CountAsync(x => seededProjects.Contains(x.Name)),
             await db.TestSuites.CountAsync(x => x.Name == "Login & Authentication")));

        Assert.Equal(0, seededProjectCount);
        Assert.Equal(0, seededSuiteCount);
    }

    [Fact]
    public async Task Health_endpoint_is_anonymous()
    {
        using var client = fixture.CreateAnonymousClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_request_to_protected_endpoint_is_rejected()
    {
        using var client = fixture.CreateAnonymousClient();

        var response = await client.GetAsync("/api/projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Administrator_client_is_authenticated_and_authorized()
    {
        using var client = await fixture.CreateAdministratorClientAsync();

        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        var projects = await client.GetAsync("/api/projects");

        Assert.Equal(fixture.Administrator.Email, me?.Email);
        Assert.Equal(HttpStatusCode.OK, projects.StatusCode);
    }

    [Fact]
    public async Task Non_administrator_is_authenticated_but_forbidden()
    {
        using var client = await fixture.CreateClientAsync(fixture.NonAdministrator);

        var me = await client.GetAsync("/api/auth/me");
        var projects = await client.GetAsync("/api/projects");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, projects.StatusCode);
    }

    [Fact]
    public async Task Login_with_wrong_password_is_rejected()
    {
        using var client = fixture.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { fixture.Administrator.Email, Password = fixture.Administrator.Password + "x" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record MeResponse(string Id, string Email, string? DisplayName);
}
