using System.Net;
using System.Text.Json;
using CQPulse.Application.Analysis;
using CQPulse.Application.Runs;
using CQPulse.Domain;
using CQPulse.Domain.Entities;
using CQPulse.Infrastructure.Integrations.LogTriage;
using CQPulse.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static CQPulse.IntegrationTests.Infrastructure.ApiTestHelpers;

namespace CQPulse.IntegrationTests;

/// <summary>LogTriageAssistant integration through its real HTTP contract, using <see cref="FakeLogTriageHandler"/>.</summary>
[Collection(ApiCollection.Name)]
public sealed class FailureAnalysisTests(ApiFixture fixture)
{
    [Fact]
    public async Task Analysis_succeeds_and_is_traceable_to_the_run_and_request()
    {
        var fake = new FakeLogTriageHandler();
        await using var factory = WithLogTriage(fake);
        using var client = await AdminClientAsync(factory);
        var run = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 50));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "ui-request-0001");

        var response = await client.PostAsync($"/api/runs/{run.Id}/failure-analyses", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("ui-request-0001", response.Headers.GetValues("X-Correlation-ID").Single());
        var analysis = await WaitForFinishedAsync(client, (await response.ReadAsync<FailureAnalysisDto>()).Id);

        Assert.Equal(AnalysisStatus.Succeeded, analysis.Status);
        Assert.Equal(("HIGH", "Timeout", 1), (analysis.Severity, analysis.TopCategory, analysis.Attempts));
        Assert.Equal(82, analysis.SignalScore);
        Assert.Equal(run.FailedCount, analysis.InputFailureCount);
        Assert.NotEmpty(analysis.Findings);
        Assert.Equal(["Check downstream latency and timeouts."], analysis.Recommendations);
        Assert.Equal("ui-request-0001", analysis.CorrelationId);

        var (document, correlationId) = Assert.Single(fake.AnalyzeRequests);
        Assert.Equal("ui-request-0001", correlationId);
        var errorLines = document.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Equal(run.FailedCount, errorLines.Count);
        Assert.All(errorLines, l => Assert.Equal(run.Id, l.GetProperty("runId").GetGuid()));
        Assert.All(errorLines, l => Assert.Equal(12, l.GetProperty("signature").GetString()!.Length));
        Assert.Equal(run, await client.GetRunAsync(run.Id) with { RerunIds = run.RerunIds }); // run untouched
    }

    [Fact]
    public async Task Repeated_requests_for_unchanged_input_reuse_the_analysis()
    {
        var fake = new FakeLogTriageHandler();
        await using var factory = WithLogTriage(fake);
        using var client = await AdminClientAsync(factory);
        var run = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 20));

        var first = await (await client.PostAsync($"/api/runs/{run.Id}/failure-analyses", null)).ReadAsync<FailureAnalysisDto>();
        await WaitForFinishedAsync(client, first.Id);
        var second = await client.PostAsync($"/api/runs/{run.Id}/failure-analyses", null);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(first.Id, (await second.ReadAsync<FailureAnalysisDto>()).Id);
        Assert.Equal(1, fake.AnalyzeCalls);
        Assert.Single(await client.GetJsonAsync<List<FailureAnalysisDto>>($"/api/runs/{run.Id}/failure-analyses"));
    }

    [Fact]
    public async Task Outage_is_retried_a_bounded_number_of_times_then_recorded_and_safely_retried()
    {
        var fake = new FakeLogTriageHandler { Behavior = FakeLogTriageHandler.Mode.ServerError };
        await using var factory = WithLogTriage(fake);
        using var client = await AdminClientAsync(factory);
        var run = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 30));

        var requested = await (await client.PostAsync($"/api/runs/{run.Id}/failure-analyses", null)).ReadAsync<FailureAnalysisDto>();
        var unavailable = await WaitForFinishedAsync(client, requested.Id);

        Assert.Equal(AnalysisStatus.Unavailable, unavailable.Status);
        Assert.Contains("503", unavailable.LastError);
        Assert.Equal(2, fake.AnalyzeCalls); // 1 attempt + RetryCount (1)

        fake.Behavior = FakeLogTriageHandler.Mode.Healthy;
        var retry = await client.PostAsync($"/api/failure-analyses/{requested.Id}/retry", null);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        var succeeded = await WaitForFinishedAsync(client, requested.Id);

        Assert.Equal((AnalysisStatus.Succeeded, 2), (succeeded.Status, succeeded.Attempts));
        Assert.Null(succeeded.LastError);
        var runAfter = await client.GetRunAsync(run.Id);
        Assert.Equal((run.Status, run.TotalTests, run.FailedCount, run.CompletedAtUtc), (runAfter.Status, runAfter.TotalTests, runAfter.FailedCount, runAfter.CompletedAtUtc));
    }

    [Theory]
    [InlineData(FakeLogTriageHandler.Mode.Malformed, AnalysisStatus.Failed, "malformed JSON", 1)]
    [InlineData(FakeLogTriageHandler.Mode.RejectCredentials, AnalysisStatus.Failed, "service account credentials", 0)]
    [InlineData(FakeLogTriageHandler.Mode.Slow, AnalysisStatus.Unavailable, "unavailable", 2)]
    public async Task Bad_responses_and_timeouts_are_recorded_without_fabricated_results(
        FakeLogTriageHandler.Mode mode, AnalysisStatus expected, string reason, int analyzeCalls)
    {
        var fake = new FakeLogTriageHandler { Behavior = mode };
        await using var factory = WithLogTriage(fake);
        using var client = await AdminClientAsync(factory);
        var run = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 20));

        var requested = await (await client.PostAsync($"/api/runs/{run.Id}/failure-analyses", null)).ReadAsync<FailureAnalysisDto>();
        var analysis = await WaitForFinishedAsync(client, requested.Id, TimeSpan.FromSeconds(30));

        Assert.Equal(expected, analysis.Status);
        Assert.Contains(reason, analysis.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.Null(analysis.Severity);
        Assert.Empty(analysis.Findings);
        Assert.Equal(analyzeCalls, fake.AnalyzeCalls);
    }

    [Fact]
    public async Task Expired_session_signs_in_again_once()
    {
        var fake = new FakeLogTriageHandler { Behavior = FakeLogTriageHandler.Mode.ExpireSessionOnce };
        await using var factory = WithLogTriage(fake);
        using var client = await AdminClientAsync(factory);
        var run = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 20));

        var requested = await (await client.PostAsync($"/api/runs/{run.Id}/failure-analyses", null)).ReadAsync<FailureAnalysisDto>();
        var analysis = await WaitForFinishedAsync(client, requested.Id);

        Assert.Equal(AnalysisStatus.Succeeded, analysis.Status);
        Assert.Equal((2, 2), (fake.LoginCalls, fake.AnalyzeCalls));
    }

    [Fact]
    public async Task Unconfigured_integration_records_an_unavailable_analysis_and_sends_nothing()
    {
        using var client = await fixture.CreateAdministratorClientAsync(); // shared fixture: integration not configured
        var run = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 20));

        var analysis = await (await client.PostAsync($"/api/runs/{run.Id}/failure-analyses", null)).ReadAsync<FailureAnalysisDto>();
        var status = await client.GetJsonAsync<List<IntegrationStatusDto>>("/api/integrations/status");

        Assert.Equal(AnalysisStatus.Unavailable, analysis.Status);
        Assert.Contains("not configured", analysis.LastError);
        Assert.Equal("NotConfigured", status.Single(x => x.Name == "LogTriageAssistant").Status);
        Assert.Equal("NotConfigured", status.Single(x => x.Name == "SmartDog").Status);
    }

    [Fact]
    public async Task Runs_with_failures_are_analysed_automatically_and_passing_runs_are_not()
    {
        var fake = new FakeLogTriageHandler();
        await using var factory = WithLogTriage(fake, autoAnalyze: true);
        using var client = await AdminClientAsync(factory);
        var failing = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 40));
        var passing = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 10) with
        {
            PassPercentage = 100, FailurePercentage = 0, SkippedPercentage = 0, FailureCategories = []
        });

        var analyses = await WaitUntilAsync(() => client.GetJsonAsync<List<FailureAnalysisDto>>($"/api/runs/{failing.Id}/failure-analyses"),
            list => list.Count == 1 && list[0].Status == AnalysisStatus.Succeeded);

        Assert.False(string.IsNullOrWhiteSpace(analyses[0].CorrelationId));
        Assert.Empty(await client.GetJsonAsync<List<FailureAnalysisDto>>($"/api/runs/{passing.Id}/failure-analyses"));
        Assert.Equal(RunStatus.CompletedWithFailures, (await client.GetRunAsync(failing.Id)).Status);
    }

    [Fact]
    public async Task Requests_are_validated()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        var passing = await client.RunToCompletionAsync(NewRequest(project.Id, 5) with
        {
            PassPercentage = 100, FailurePercentage = 0, SkippedPercentage = 0, FailureCategories = []
        });
        var active = await client.QueueRunAsync(NewRequest((await client.CreateProjectAsync()).Id, 300) with { MaxConcurrency = 1, MinimumDurationMs = 10, MaximumDurationMs = 10 });

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/runs/{Guid.NewGuid()}/failure-analyses", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/runs/{passing.Id}/failure-analyses", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/runs/{active}/failure-analyses", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/failure-analyses/{Guid.NewGuid()}/retry", null)).StatusCode);

        await client.PostAsync($"/api/simulator/runs/{active}/cancel", null);
        await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 1));
    }

    [Fact]
    public async Task Analysis_endpoints_require_the_administrator_role()
    {
        using var anonymous = fixture.CreateAnonymousClient();
        using var viewer = await fixture.CreateClientAsync(fixture.NonAdministrator);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/runs/{Guid.NewGuid()}/failure-analyses", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/runs/{Guid.NewGuid()}/failure-analyses", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/integrations/status")).StatusCode);
    }

    [Fact]
    public async Task Integration_status_reports_configured_service_health()
    {
        var fake = new FakeLogTriageHandler();
        await using var factory = WithLogTriage(fake);
        using var client = await AdminClientAsync(factory);

        var healthy = await client.GetJsonAsync<List<IntegrationStatusDto>>("/api/integrations/status");
        fake.Ready = false;
        var down = await client.GetJsonAsync<List<IntegrationStatusDto>>("/api/integrations/status");

        Assert.Equal(("Healthy", true), (healthy[0].Status, healthy[0].Configured));
        Assert.Equal("Unavailable", down[0].Status);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode); // CQ Pulse itself stays ready
    }

    [Fact]
    public async Task Startup_recovery_marks_interrupted_analyses_unavailable_and_resumes_pending_ones()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var run = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 20));
        var (runningId, pendingId) = await fixture.WithDbContextAsync(async db =>
        {
            var running = NewAnalysis(run.Id);
            running.Start(DateTimeOffset.UtcNow);
            var pending = NewAnalysis(run.Id);
            db.FailureAnalyses.AddRange(running, pending);
            await db.SaveChangesAsync();
            return (running.Id, pending.Id);
        });

        var fake = new FakeLogTriageHandler();
        await using var factory = WithLogTriage(fake);
        using var restarted = await AdminClientAsync(factory);

        var interrupted = await WaitForFinishedAsync(restarted, runningId);
        var resumed = await WaitForFinishedAsync(restarted, pendingId);
        Assert.Equal(AnalysisStatus.Unavailable, interrupted.Status);
        Assert.Contains("restart", interrupted.LastError);
        Assert.Equal(AnalysisStatus.Succeeded, resumed.Status);
    }

    private WebApplicationFactory<Program> WithLogTriage(FakeLogTriageHandler fake, bool autoAnalyze = false) =>
        fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Integrations:LogTriage:BaseUrl", "https://logtriage.test");
            builder.UseSetting("Integrations:LogTriage:Username", FakeLogTriageHandler.Username);
            builder.UseSetting("Integrations:LogTriage:Password", FakeLogTriageHandler.Password);
            builder.UseSetting("Integrations:LogTriage:AutoAnalyzeFailedRuns", autoAnalyze.ToString());
            builder.UseSetting("Integrations:LogTriage:RetryCount", "1");
            builder.UseSetting("Integrations:LogTriage:RetryBaseDelayMilliseconds", "10");
            builder.UseSetting("Integrations:LogTriage:AttemptTimeoutSeconds", "1");
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<LogTriageClient>().ConfigurePrimaryHttpMessageHandler(() => fake));
        });

    private async Task<HttpClient> AdminClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", await fixture.GetTokenAsync(fixture.Administrator));
        return client;
    }

    private static async Task<FailureAnalysisDto> WaitForFinishedAsync(HttpClient client, Guid analysisId, TimeSpan? timeout = null) =>
        await WaitUntilAsync(() => client.GetJsonAsync<FailureAnalysisDto>($"/api/failure-analyses/{analysisId}"),
            a => a.Status is not (AnalysisStatus.Pending or AnalysisStatus.Running), timeout);

    private static async Task<T> WaitUntilAsync<T>(Func<Task<T>> read, Func<T, bool> done, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (true)
        {
            var value = await read();
            if (done(value)) return value;
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Condition not met; last value: {JsonSerializer.Serialize(value)}");
            await Task.Delay(50);
        }
    }

    private static FailureAnalysis NewAnalysis(Guid runId) => new()
    {
        TestRunId = runId, Provider = "LogTriageAssistant", InputDigest = new string('a', 64), CorrelationId = Guid.NewGuid().ToString("N")
    };
}
