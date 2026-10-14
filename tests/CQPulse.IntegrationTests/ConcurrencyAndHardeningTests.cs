using System.Net;
using System.Net.Http.Json;
using CQPulse.Domain;
using CQPulse.Domain.Entities;
using CQPulse.Infrastructure.Identity;
using CQPulse.Infrastructure.Persistence;
using CQPulse.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CQPulse.IntegrationTests.Infrastructure.ApiTestHelpers;

namespace CQPulse.IntegrationTests;

/// <summary>M5: optimistic concurrency, single active run per target, execution logs, and sign-in hardening.</summary>
[Collection(ApiCollection.Name)]
public sealed class ConcurrencyAndHardeningTests(ApiFixture fixture)
{
    [Fact]
    public async Task Run_rowversion_detects_concurrent_updates()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var run = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 5));

        await using var first = fixture.Database.CreateDbContext();
        await using var second = fixture.Database.CreateDbContext();
        var a = await first.TestRuns.SingleAsync(x => x.Id == run.Id);
        var b = await second.TestRuns.SingleAsync(x => x.Id == run.Id);
        a.RunLabel = "first writer";
        await first.SaveChangesAsync();
        b.RunLabel = "second writer";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_rejects_a_second_active_run_for_the_same_target()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();

        await using var db = fixture.Database.CreateDbContext();
        db.TestRuns.AddRange(NewRun(project.Id), NewRun(project.Id));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(CQPulseDbContext.ActiveRunIndexName, exception.InnerException?.Message);
    }

    [Fact]
    public async Task Terminal_runs_do_not_block_new_runs_for_the_same_target()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();

        await client.RunToCompletionAsync(NewRequest(project.Id, 5));
        var second = await client.RunToCompletionAsync(NewRequest(project.Id, 5));

        Assert.True(second.Status.IsTerminal());
    }

    [Fact]
    public async Task Concurrent_submissions_for_one_target_queue_exactly_one_run()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        var request = NewRequest(project.Id, 300) with { MaxConcurrency = 2, MinimumDurationMs = 5, MaximumDurationMs = 5 };

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.PostJsonAsync("/api/simulator/runs", request)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var runId = (await responses.Single(r => r.StatusCode == HttpStatusCode.Accepted).ReadAsync<QueuedRun>()).RunId;
        await client.PostAsync($"/api/simulator/runs/{runId}/cancel", null);
        await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 5));
    }

    /// <summary>
    /// Whenever cancel returns 204 the run must end Cancelled, even if the worker was finishing at that moment.
    /// Cancels are issued at varied points, including right around completion, to exercise the race.
    /// </summary>
    [Fact]
    public async Task Accepted_cancellation_is_never_overwritten_by_completion()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var accepted = 0;
        for (var i = 0; i < 12; i++)
        {
            var project = await client.CreateProjectAsync();
            var runId = await client.QueueRunAsync(NewRequest(project.Id, 120) with { MaxConcurrency = 8, MinimumDurationMs = 1, MaximumDurationMs = 3 });
            await Task.Delay(i * 15);

            var cancel = await client.PostAsync($"/api/simulator/runs/{runId}/cancel", null);
            var run = await client.WaitForTerminalAsync(runId);
            await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 1)); // worker finished the run
            run = await client.GetRunAsync(runId);

            if (cancel.StatusCode == HttpStatusCode.NoContent)
            {
                accepted++;
                Assert.Equal(RunStatus.Cancelled, run.Status);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
                Assert.NotEqual(RunStatus.Cancelled, run.Status);
            }
            var rows = await fixture.WithDbContextAsync(db => db.TestCaseResults.CountAsync(x => x.TestRunId == runId));
            Assert.Equal(rows, run.CompletedTests);
        }
        Assert.True(accepted > 0, "No cancellation was accepted; the test did not exercise the race.");
    }

    [Fact]
    public async Task Runs_record_lifecycle_and_failed_test_logs()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var run = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 200));

        var logs = await client.GetJsonAsync<Application.Catalog.PageResponse<Application.Runs.RunLogDto>>($"/api/runs/{run.Id}/logs?pageSize=100");
        var all = await fixture.WithDbContextAsync(db => db.ExecutionLogs.Where(x => x.TestRunId == run.Id).ToListAsync());

        Assert.Equal(run.FailedCount + 2, all.Count);
        Assert.Contains(all, x => x.Message.StartsWith("Run started: 200 tests"));
        Assert.Contains(all, x => x.Message.StartsWith("Run completed with status CompletedWithFailures"));
        Assert.Equal(run.FailedCount, all.Count(x => x.Level == LogLevel.Error && x.TestCaseResultId != null));
        Assert.Equal(all.Count, logs.TotalCount);
        Assert.Equal(Math.Min(100, all.Count), logs.Items.Count);
        Assert.Equal(all.Max(x => x.TimestampUtc), logs.Items[0].TimestampUtc); // newest first
    }

    [Fact]
    public async Task Run_records_the_requesting_user_id()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var run = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 3));

        var (requestedBy, adminId) = await fixture.WithDbContextAsync(async db =>
            (await db.TestRuns.Where(x => x.Id == run.Id).Select(x => x.RequestedByUserId).SingleAsync(),
             await db.Users.Where(x => x.Email == fixture.Administrator.Email).Select(x => x.Id).SingleAsync()));

        Assert.Equal(adminId, requestedBy);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account()
    {
        var user = await fixture.CreateUserAsync("lockout", ApiFixture.AdministratorRole);
        using var client = fixture.CreateAnonymousClient();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, user.Email, "Wrong-password-1!")).StatusCode);
        var withCorrectPassword = await Login(client, user.Email, user.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, withCorrectPassword.StatusCode);
        var lockoutEnd = await fixture.WithDbContextAsync(db => db.Users.Where(x => x.Email == user.Email).Select(x => x.LockoutEnd).SingleAsync());
        Assert.True(lockoutEnd > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Successful_sign_in_resets_the_failed_attempt_count()
    {
        var user = await fixture.CreateUserAsync("reset", ApiFixture.AdministratorRole);
        using var client = fixture.CreateAnonymousClient();

        for (var i = 0; i < 4; i++) await Login(client, user.Email, "Wrong-password-1!");
        Assert.Equal(HttpStatusCode.OK, (await Login(client, user.Email, user.Password)).StatusCode);
        for (var i = 0; i < 4; i++) await Login(client, user.Email, "Wrong-password-1!");

        Assert.Equal(HttpStatusCode.OK, (await Login(client, user.Email, user.Password)).StatusCode);
    }

    [Fact]
    public async Task Sign_in_is_rate_limited()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:Login:PermitLimit", "3");
            builder.UseSetting("RateLimiting:Login:WindowSeconds", "60");
        });
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++) statuses.Add((await Login(client, "nobody@cqpulse.test", "irrelevant")).StatusCode);

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
            HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], statuses);
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private static TestRun NewRun(Guid projectId) => new()
    {
        ProjectId = projectId, RunLabel = "Direct insert", TotalTests = 1, ConfigurationSnapshot = "{}"
    };

    private sealed record QueuedRun(Guid RunId);
}
