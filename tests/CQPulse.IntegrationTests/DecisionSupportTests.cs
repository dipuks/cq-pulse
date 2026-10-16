using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CQPulse.Application.Analysis;
using CQPulse.Application.Catalog;
using CQPulse.Application.Decisions;
using CQPulse.Application.Runs;
using CQPulse.Domain;
using CQPulse.Domain.Entities;
using CQPulse.Infrastructure.Integrations.LogTriage;
using CQPulse.Infrastructure.Integrations.SmartDog;
using CQPulse.Infrastructure.Persistence;
using CQPulse.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CQPulse.IntegrationTests.Infrastructure.ApiTestHelpers;

namespace CQPulse.IntegrationTests;

/// <summary>SmartDog decision support over SmartDog's HTTP contract, with LogTriageAssistant evidence where available.</summary>
[Collection(ApiCollection.Name)]
public sealed class DecisionSupportTests(ApiFixture fixture)
{
    [Fact]
    public async Task Evaluation_submits_persisted_evidence_including_triage_severity_and_records_the_result()
    {
        var (lta, smartDog) = (new FakeLogTriageHandler(), new FakeSmartDogHandler());
        await using var factory = WithIntegrations(lta, smartDog);
        using var client = await AdminClientAsync(factory);
        var run = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 120));
        var analysis = await AnalyzeAsync(client, run.Id);
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "decision-test-0001");

        var evaluation = await (await client.PostJsonAsync($"/api/runs/{run.Id}/decision-support", new { })).ReadAsync<DecisionEvaluationDto>();

        Assert.Equal(AnalysisStatus.Succeeded, evaluation.Status);
        Assert.Equal(analysis.Id, evaluation.FailureAnalysisId);
        Assert.Equal("decision-test-0001", evaluation.CorrelationId);
        Assert.Equal(DecisionCriteria.All, evaluation.Criteria.Select(c => c.Id));
        Assert.All(evaluation.Criteria, c => Assert.False(string.IsNullOrWhiteSpace(c.Source)));
        Assert.Equal(3, evaluation.Evidence.Count); // UiElement, ApiError, Timeout
        Assert.Equal(run.FailedCount, evaluation.Evidence.Sum(e => e.OccurrencesInRun));
        Assert.All(evaluation.Evidence, e => Assert.Equal(e.OccurrencesInRun, e.Recurrence30Days)); // new project: only this run
        Assert.Equal("CRITICAL", evaluation.Evidence.Single(e => e.Category == FailureCategory.ApiError).TriageSeverity);
        Assert.Equal("HIGH", evaluation.Evidence.Single(e => e.Category == FailureCategory.Timeout).TriageSeverity);
        Assert.Equal("MEDIUM", evaluation.Evidence.Single(e => e.Category == FailureCategory.UiElement).TriageSeverity);
        Assert.Contains(evaluation.EvidenceNotes, n => n.Contains("synthetic workload"));
        Assert.Contains(evaluation.EvidenceNotes, n => n.StartsWith("Triage severity comes from LogTriageAssistant analysis"));
        Assert.NotNull(evaluation.Result);
        Assert.Equal("Ranked", evaluation.Result!.Status);
        Assert.Equal(3, evaluation.Result.Ranking.Count);
        Assert.NotNull(evaluation.ExternalEvaluationId);

        var (body, key, correlation) = Assert.Single(smartDog.Requests);
        Assert.Equal((FakeSmartDogHandler.ApiKey, "decision-test-0001"), (key, correlation));
        var apiErrorOption = body.GetProperty("options").EnumerateArray()
            .Single(o => o.GetProperty("id").GetString() == evaluation.Evidence.Single(e => e.Category == FailureCategory.ApiError).OptionId);
        Assert.Equal(4, apiErrorOption.GetProperty("values").GetProperty(DecisionCriteria.TriageSeverity).GetDouble());
        Assert.Equal(0, body.GetProperty("constraints").GetArrayLength());

        var runAfter = await client.GetRunAsync(run.Id);
        Assert.Equal((run.Status, run.FailedCount, run.CompletedAtUtc), (runAfter.Status, runAfter.FailedCount, runAfter.CompletedAtUtc));
    }

    [Fact]
    public async Task Without_a_triage_analysis_severity_is_left_out_not_zeroed()
    {
        var smartDog = new FakeSmartDogHandler();
        await using var factory = WithIntegrations(new FakeLogTriageHandler(), smartDog);
        using var client = await AdminClientAsync(factory);
        var run = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 60));

        var evaluation = await (await client.PostJsonAsync($"/api/runs/{run.Id}/decision-support",
            new { weights = new Dictionary<string, double> { [DecisionCriteria.TriageSeverity] = 5 } })).ReadAsync<DecisionEvaluationDto>();

        Assert.DoesNotContain(evaluation.Criteria, c => c.Id == DecisionCriteria.TriageSeverity);
        Assert.All(evaluation.Evidence, e => Assert.Null(e.TriageSeverity));
        Assert.Contains("Triage severity was not used: this run has no completed LogTriageAssistant analysis.", evaluation.EvidenceNotes);
        Assert.Contains(evaluation.EvidenceNotes, n => n.Contains("weight given to triage severity was not applied"));
        Assert.Null(evaluation.FailureAnalysisId);
        var sent = Assert.Single(smartDog.Requests).Body;
        Assert.All(sent.GetProperty("options").EnumerateArray(), o => Assert.False(o.GetProperty("values").TryGetProperty(DecisionCriteria.TriageSeverity, out _)));
    }

    [Fact]
    public async Task Severity_from_a_truncated_analysis_says_which_results_it_covers()
    {
        await using var factory = WithIntegrations(new FakeLogTriageHandler(), new FakeSmartDogHandler(), ("Integrations:LogTriage:MaxFailures", "5"));
        using var client = await AdminClientAsync(factory);
        var run = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 60) with { FailureCategories = [FailureCategory.ApiError] });
        var analysis = await AnalyzeAsync(client, run.Id);
        Assert.True(analysis.InputTruncated);

        var evaluation = await (await client.PostJsonAsync($"/api/runs/{run.Id}/decision-support", new { })).ReadAsync<DecisionEvaluationDto>();

        Assert.Equal("CRITICAL", Assert.Single(evaluation.Evidence).TriageSeverity);
        Assert.Contains($"That analysis covered 5 of the run's {run.FailedCount} failed results: LogTriageAssistant reports findings for at most 500 lines, " +
            "so CQ Pulse submits a sample (Integrations:LogTriage document limits) that includes each signature, most frequent first. " +
            "Each severity reflects the sampled results only.", evaluation.EvidenceNotes);
    }

    /// <summary>
    /// Regression for the 2,000-line document versus LogTriageAssistant's 500 findings: signatures that first fail after line
    /// 500 used to get no severity, so severity was dropped for every option.
    /// </summary>
    [Fact]
    public async Task Severity_covers_every_signature_when_a_run_has_more_failures_than_LogTriageAssistant_reports_on()
    {
        await using var factory = WithIntegrations(new FakeLogTriageHandler(), new FakeSmartDogHandler());
        using var client = await AdminClientAsync(factory);
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        var rows = Enumerable.Range(0, 600).Select(i => new SeedResult(start.AddSeconds(i), TestOutcome.Failed, FailureCategory.Timeout, "Simulated Timeout failure."))
            .Concat(Enumerable.Range(0, 3).Select(i => new SeedResult(start.AddSeconds(700 + i), TestOutcome.Failed, FailureCategory.ApiError, "Simulated ApiError failure.")))
            .Append(new SeedResult(start.AddSeconds(800), TestOutcome.Failed, FailureCategory.UiElement, "Simulated UiElement failure."))
            .ToList();
        var runId = await fixture.InsertCompletedRunAsync((await client.CreateProjectAsync()).Id, rows);
        var (timeout, apiError, uiElement) = (FailureSignature.Compute(FailureCategory.Timeout, "Simulated Timeout failure."),
            FailureSignature.Compute(FailureCategory.ApiError, "Simulated ApiError failure."), FailureSignature.Compute(FailureCategory.UiElement, "Simulated UiElement failure."));
        try
        {
            // Round-robin across signatures (most frequent first), written in completion order, and deterministic.
            var small = await fixture.WithDbContextAsync(db => AnalysisDocumentBuilder.BuildAsync(db, runId, new LogTriageOptions { MaxFailures = 4 }, default));
            Assert.Equal([timeout, timeout, apiError, uiElement], small.LineSignatures);
            var full = await fixture.WithDbContextAsync(db => AnalysisDocumentBuilder.BuildAsync(db, runId, new LogTriageOptions(), default));
            var again = await fixture.WithDbContextAsync(db => AnalysisDocumentBuilder.BuildAsync(db, runId, new LogTriageOptions(), default));
            Assert.Equal((500, true, 496), (full.FailureCount, full.Truncated, full.LineSignatures.Count(s => s == timeout)));
            Assert.Equal(full.Digest, again.Digest);

            var analysis = await AnalyzeAsync(client, runId);
            Assert.Equal((500, true), (analysis.InputFailureCount, analysis.InputTruncated));
            var evaluation = await (await client.PostJsonAsync($"/api/runs/{runId}/decision-support", new { })).ReadAsync<DecisionEvaluationDto>();

            Assert.Contains(evaluation.Criteria, c => c.Id == DecisionCriteria.TriageSeverity);
            Assert.Equal(("HIGH", "CRITICAL", "MEDIUM"), (evaluation.Evidence.Single(e => e.Signature == timeout).TriageSeverity,
                evaluation.Evidence.Single(e => e.Signature == apiError).TriageSeverity, evaluation.Evidence.Single(e => e.Signature == uiElement).TriageSeverity));
            Assert.Contains("That analysis covered 500 of the run's 604 failed results: LogTriageAssistant reports findings for at most 500 lines, " +
                "so CQ Pulse submits a sample (Integrations:LogTriage document limits) that includes each signature, most frequent first. " +
                "Each severity reflects the sampled results only.", evaluation.EvidenceNotes);
        }
        finally
        {
            await fixture.DeleteRunsAsync(runId);
        }
    }

    [Fact]
    public async Task A_document_limit_above_what_LogTriageAssistant_reports_on_is_rejected_at_startup()
    {
        await using var factory = WithIntegrations(new FakeLogTriageHandler(), new FakeSmartDogHandler(), ("Integrations:LogTriage:MaxFailures", "501"));

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("MaxFailures must be 1-500", error.ToString());
    }

    [Fact]
    public async Task Stale_or_incomplete_severity_evidence_is_not_used()
    {
        await using var factory = WithIntegrations(new FakeLogTriageHandler(), new FakeSmartDogHandler());
        using var client = await AdminClientAsync(factory);
        var run = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 60));
        var signatures = (await (await client.PostJsonAsync($"/api/runs/{run.Id}/decision-support", new { })).ReadAsync<DecisionEvaluationDto>())
            .Evidence.Select(e => e.Signature).ToList();
        var digest = await fixture.WithDbContextAsync(async db => (await AnalysisDocumentBuilder.BuildAsync(db, run.Id, new LogTriageOptions(), default)).Digest);

        await InsertAnalysisAsync(run.Id, new string('0', 64), signatures.ToDictionary(s => s, _ => "HIGH"));
        var stale = await (await client.PostJsonAsync($"/api/runs/{run.Id}/decision-support", new { })).ReadAsync<DecisionEvaluationDto>();
        await InsertAnalysisAsync(run.Id, digest, signatures.Skip(1).ToDictionary(s => s, _ => "HIGH"));
        var incomplete = await (await client.PostJsonAsync($"/api/runs/{run.Id}/decision-support", new { })).ReadAsync<DecisionEvaluationDto>();
        await InsertAnalysisAsync(run.Id, digest, signatures.ToDictionary(s => s, _ => "MEDIUM"));
        var complete = await (await client.PostJsonAsync($"/api/runs/{run.Id}/decision-support", new { })).ReadAsync<DecisionEvaluationDto>();

        Assert.Contains(stale.EvidenceNotes, n => n.Contains("made from different input"));
        Assert.DoesNotContain(stale.Criteria, c => c.Id == DecisionCriteria.TriageSeverity);
        Assert.Contains(incomplete.EvidenceNotes, n => n.Contains($"findings for {signatures.Count - 1} of {signatures.Count} ranked signatures"));
        Assert.DoesNotContain(incomplete.Criteria, c => c.Id == DecisionCriteria.TriageSeverity);
        Assert.Contains(complete.Criteria, c => c.Id == DecisionCriteria.TriageSeverity);
        Assert.All(complete.Evidence, e => Assert.Equal("MEDIUM", e.TriageSeverity));
    }

    [Fact]
    public async Task Weights_and_minimum_occurrences_are_applied_and_validated()
    {
        var smartDog = new FakeSmartDogHandler();
        await using var factory = WithIntegrations(new FakeLogTriageHandler(), smartDog);
        using var client = await AdminClientAsync(factory);
        var run = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 60));

        var evaluation = await (await client.PostJsonAsync($"/api/runs/{run.Id}/decision-support", new
        {
            weights = new Dictionary<string, double> { [DecisionCriteria.OccurrencesInRun] = 2, [DecisionCriteria.AffectedTestCases] = 0.5 },
            minimumOccurrences = 3
        })).ReadAsync<DecisionEvaluationDto>();
        var unknown = await client.PostJsonAsync($"/api/runs/{run.Id}/decision-support", new { weights = new Dictionary<string, double> { ["effort"] = 1 } });
        var zero = await client.PostJsonAsync($"/api/runs/{run.Id}/decision-support", new { weights = new Dictionary<string, double> { [DecisionCriteria.OccurrencesInRun] = 0 } });
        var badMinimum = await client.PostJsonAsync($"/api/runs/{run.Id}/decision-support", new { minimumOccurrences = 0 });

        Assert.Equal(2, evaluation.Criteria.Single(c => c.Id == DecisionCriteria.OccurrencesInRun).Weight);
        Assert.Equal(0.5, evaluation.Criteria.Single(c => c.Id == DecisionCriteria.AffectedTestCases).Weight);
        Assert.Equal(0.25, evaluation.Criteria.Single(c => c.Id == DecisionCriteria.Recurrence30Days).Weight);
        Assert.Equal(3, evaluation.MinimumOccurrences);
        var constraint = Assert.Single(smartDog.Requests).Body.GetProperty("constraints")[0];
        Assert.Equal((DecisionCriteria.OccurrencesInRun, ">=", 3.0),
            (constraint.GetProperty("criterionId").GetString(), constraint.GetProperty("operator").GetString(), constraint.GetProperty("value").GetDouble()));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("Unknown criterion 'effort'", await unknown.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badMinimum.StatusCode);
        Assert.Single(smartDog.Requests); // invalid requests never reach SmartDog
    }

    [Theory]
    [InlineData(FakeSmartDogHandler.Mode.ServerError, AnalysisStatus.Unavailable, "503", 2)]
    [InlineData(FakeSmartDogHandler.Mode.Slow, AnalysisStatus.Unavailable, "SmartDog is unavailable: timed out waiting for a response.", 2)]
    [InlineData(FakeSmartDogHandler.Mode.Unauthorized, AnalysisStatus.Failed, "rejected the configured API key", 1)]
    [InlineData(FakeSmartDogHandler.Mode.BadRequest, AnalysisStatus.Failed, "criteria[0].weight", 1)]
    [InlineData(FakeSmartDogHandler.Mode.Malformed, AnalysisStatus.Failed, "malformed JSON", 1)]
    public async Task SmartDog_failures_are_recorded_explicitly_without_results(
        FakeSmartDogHandler.Mode mode, AnalysisStatus expected, string reason, int calls)
    {
        var smartDog = new FakeSmartDogHandler { Behavior = mode };
        await using var factory = WithIntegrations(new FakeLogTriageHandler(), smartDog);
        using var client = await AdminClientAsync(factory);
        var run = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 30));

        var evaluation = await (await client.PostJsonAsync($"/api/runs/{run.Id}/decision-support", new { })).ReadAsync<DecisionEvaluationDto>();

        Assert.Equal(expected, evaluation.Status);
        Assert.Contains(reason, evaluation.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.Null(evaluation.Result);
        Assert.NotEmpty(evaluation.Evidence); // the submitted evidence is still recorded
        Assert.Equal(calls, smartDog.EvaluateCalls); // transient failures retried once (RetryCount 1); rejections are not retried
        Assert.Equal(evaluation.Id, (await client.GetJsonAsync<DecisionEvaluationDto>($"/api/decision-evaluations/{evaluation.Id}")).Id);
    }

    [Fact]
    public async Task Unconfigured_SmartDog_records_an_unavailable_evaluation_and_core_workflows_continue()
    {
        using var client = await fixture.CreateAdministratorClientAsync(); // shared fixture: no integrations configured
        var run = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 30));

        var evaluation = await (await client.PostJsonAsync($"/api/runs/{run.Id}/decision-support", new { })).ReadAsync<DecisionEvaluationDto>();
        var status = await client.GetJsonAsync<List<IntegrationStatusDto>>("/api/integrations/status");

        Assert.Equal(AnalysisStatus.Unavailable, evaluation.Status);
        Assert.Contains("SmartDog is not configured", evaluation.LastError);
        Assert.Equal(RunStatus.CompletedWithFailures, run.Status);
        Assert.Equal("NotConfigured", status.Single(s => s.Name == "SmartDog").Status);
    }

    [Fact]
    public async Task Requests_are_validated_and_history_is_newest_first()
    {
        await using var factory = WithIntegrations(new FakeLogTriageHandler(), new FakeSmartDogHandler());
        using var client = await AdminClientAsync(factory);
        var project = await client.CreateProjectAsync();
        var run = await client.RunToCompletionAsync(NewRequest(project.Id, 30));
        var passing = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 5) with
        {
            PassPercentage = 100, FailurePercentage = 0, SkippedPercentage = 0, FailureCategories = []
        });

        var first = await (await client.PostJsonAsync($"/api/runs/{run.Id}/decision-support", new { })).ReadAsync<DecisionEvaluationDto>();
        var second = await (await client.PostJsonAsync($"/api/runs/{run.Id}/decision-support", new { })).ReadAsync<DecisionEvaluationDto>();
        var history = await client.GetJsonAsync<List<DecisionEvaluationDto>>($"/api/runs/{run.Id}/decision-support");

        Assert.Equal([second.Id, first.Id], history.Select(h => h.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostJsonAsync($"/api/runs/{Guid.NewGuid()}/decision-support", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostJsonAsync($"/api/runs/{passing.Id}/decision-support", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/decision-evaluations/{Guid.NewGuid()}")).StatusCode);
    }

    /// <summary>Recurrence used to be refused above 10,000 distinct messages in 30 days; it is now counted exactly in SQL.</summary>
    [Fact]
    public async Task Recurrence_is_exact_for_more_distinct_messages_than_the_old_in_memory_limit()
    {
        var smartDog = new FakeSmartDogHandler();
        await using var factory = WithIntegrations(new FakeLogTriageHandler(), smartDog);
        using var client = await AdminClientAsync(factory);
        var project = await client.CreateProjectAsync();
        var at = DateTimeOffset.UtcNow.AddMinutes(-5);
        List<SeedResult> Orders(int first, int count) => Enumerable.Range(first, count)
            .Select(i => new SeedResult(at.AddMilliseconds(-i), TestOutcome.Failed, FailureCategory.ApiError, $"Order {i} failed for customer {Guid.NewGuid()}")).ToList();
        var runId = await fixture.InsertCompletedRunAsync(project.Id, Orders(0, 10_001));
        var otherId = await fixture.InsertCompletedRunAsync(project.Id, [.. Orders(20_000, 2_000), new SeedResult(at, TestOutcome.Failed, FailureCategory.Timeout, "Timeout after 5 ms")]);
        try
        {
            var evaluation = await (await client.PostJsonAsync($"/api/runs/{runId}/decision-support", new { })).ReadAsync<DecisionEvaluationDto>();

            var option = Assert.Single(evaluation.Evidence);
            Assert.Equal((FailureSignature.Compute(FailureCategory.ApiError, "Order 1 failed for customer 3f2504e0-4f89-11d3-9a0c-0305e82c3301"), 10_001, 12_001),
                (option.Signature, option.OccurrencesInRun, option.Recurrence30Days));
            Assert.Equal(AnalysisStatus.Succeeded, evaluation.Status);
            Assert.Single(smartDog.Requests);
        }
        finally
        {
            await fixture.DeleteRunsAsync(runId, otherId);
        }
    }

    [Fact]
    public async Task Recurrence_waits_for_the_signature_backfill_instead_of_undercounting()
    {
        var smartDog = new FakeSmartDogHandler();
        await using var factory = WithIntegrations(new FakeLogTriageHandler(), smartDog);
        using var client = await AdminClientAsync(factory);
        var project = await client.CreateProjectAsync();
        var run = await client.RunToCompletionAsync(NewRequest(project.Id, 30));
        var other = await client.RunToCompletionAsync(NewRequest(project.Id, 40, seed: 7));
        await fixture.ClearSignaturesAsync(other.Id);
        try
        {
            var refused = await client.PostJsonAsync($"/api/runs/{run.Id}/decision-support", new { });

            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Contains($"{other.FailedCount} failed results from the last 30 days are still waiting for the failure signature backfill",
                await refused.Content.ReadAsStringAsync());
            Assert.Empty(smartDog.Requests);
            Assert.Empty(await client.GetJsonAsync<List<DecisionEvaluationDto>>($"/api/runs/{run.Id}/decision-support"));
        }
        finally
        {
            await factory.Services.GetRequiredService<FailureSignatureBackfill>().RunAsync(100, TimeSpan.Zero, default);
        }

        var evaluation = await (await client.PostJsonAsync($"/api/runs/{run.Id}/decision-support", new { })).ReadAsync<DecisionEvaluationDto>();
        var expected = await fixture.WithDbContextAsync(db => db.TestCaseResults
            .Where(x => (x.TestRunId == run.Id || x.TestRunId == other.Id) && x.Outcome == TestOutcome.Failed)
            .GroupBy(x => x.Signature!).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count));
        Assert.All(evaluation.Evidence, e => Assert.Equal(expected[e.Signature], e.Recurrence30Days));
    }

    [Fact]
    public async Task Ranked_options_link_to_their_failed_results()
    {
        await using var factory = WithIntegrations(new FakeLogTriageHandler(), new FakeSmartDogHandler());
        using var client = await AdminClientAsync(factory);
        var run = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 90));
        var evaluation = await (await client.PostJsonAsync($"/api/runs/{run.Id}/decision-support", new { })).ReadAsync<DecisionEvaluationDto>();

        foreach (var option in evaluation.Evidence)
        {
            var results = await client.GetJsonAsync<PageResponse<RunResultDto>>($"/api/runs/{run.Id}/results?signature={option.Signature}&pageSize=100");
            Assert.Equal(option.OccurrencesInRun, results.TotalCount);
            Assert.All(results.Items, r => Assert.Equal((TestOutcome.Failed, option.Category), (r.Outcome, r.FailureCategory)));
        }
        Assert.Equal(0, (await client.GetJsonAsync<PageResponse<RunResultDto>>($"/api/runs/{run.Id}/results?signature=000000000000")).TotalCount);
    }

    [Fact]
    public async Task Integration_status_reports_SmartDog_health()
    {
        var smartDog = new FakeSmartDogHandler();
        await using var factory = WithIntegrations(new FakeLogTriageHandler(), smartDog);
        using var client = await AdminClientAsync(factory);

        var healthy = await client.GetJsonAsync<List<IntegrationStatusDto>>("/api/integrations/status");
        smartDog.Ready = false;
        var down = await client.GetJsonAsync<List<IntegrationStatusDto>>("/api/integrations/status");

        Assert.Equal(("Healthy", true), (healthy.Single(s => s.Name == "SmartDog").Status, healthy.Single(s => s.Name == "SmartDog").Configured));
        Assert.Equal("Unavailable", down.Single(s => s.Name == "SmartDog").Status);
        Assert.Equal("Healthy", down.Single(s => s.Name == "LogTriageAssistant").Status);

        smartDog.HealthDelay = TimeSpan.FromSeconds(30);
        var slow = (await client.GetJsonAsync<List<IntegrationStatusDto>>("/api/integrations/status")).Single(s => s.Name == "SmartDog");
        Assert.Equal(("Unavailable", "Unreachable: timed out waiting for a response."), (slow.Status, slow.Detail));
    }

    [Fact]
    public async Task Decision_endpoints_require_the_administrator_role()
    {
        using var anonymous = fixture.CreateAnonymousClient();
        using var viewer = await fixture.CreateClientAsync(fixture.NonAdministrator);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"/api/runs/{Guid.NewGuid()}/decision-support", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/runs/{Guid.NewGuid()}/decision-support", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/api/decision-evaluations/{Guid.NewGuid()}")).StatusCode);
    }

    private WebApplicationFactory<Program> WithIntegrations(FakeLogTriageHandler lta, FakeSmartDogHandler smartDog, params (string Key, string Value)[] settings) =>
        fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Integrations:LogTriage:BaseUrl", "https://logtriage.test");
            builder.UseSetting("Integrations:LogTriage:Username", FakeLogTriageHandler.Username);
            builder.UseSetting("Integrations:LogTriage:Password", FakeLogTriageHandler.Password);
            builder.UseSetting("Integrations:LogTriage:AutoAnalyzeFailedRuns", "false");
            builder.UseSetting("Integrations:LogTriage:RetryCount", "1");
            builder.UseSetting("Integrations:LogTriage:RetryBaseDelayMilliseconds", "10");
            builder.UseSetting("Integrations:SmartDog:BaseUrl", "https://smartdog.test");
            builder.UseSetting("Integrations:SmartDog:ApiKey", FakeSmartDogHandler.ApiKey);
            builder.UseSetting("Integrations:SmartDog:RetryCount", "1");
            builder.UseSetting("Integrations:SmartDog:RetryBaseDelayMilliseconds", "10");
            builder.UseSetting("Integrations:SmartDog:AttemptTimeoutSeconds", "1");
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<LogTriageClient>().ConfigurePrimaryHttpMessageHandler(() => lta);
                services.AddHttpClient<SmartDogClient>().ConfigurePrimaryHttpMessageHandler(() => smartDog);
            });
        });

    private async Task<HttpClient> AdminClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", await fixture.GetTokenAsync(fixture.Administrator));
        return client;
    }

    private static async Task<FailureAnalysisDto> AnalyzeAsync(HttpClient client, Guid runId)
    {
        var requested = await (await client.PostAsync($"/api/runs/{runId}/failure-analyses", null)).ReadAsync<FailureAnalysisDto>();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            var analysis = await client.GetJsonAsync<FailureAnalysisDto>($"/api/failure-analyses/{requested.Id}");
            if (analysis.Status == AnalysisStatus.Succeeded) return analysis;
            if (analysis.Status is AnalysisStatus.Failed or AnalysisStatus.Unavailable || DateTime.UtcNow > deadline)
                throw new InvalidOperationException($"Analysis ended as {analysis.Status}: {analysis.LastError}");
            await Task.Delay(50);
        }
    }

    private Task InsertAnalysisAsync(Guid runId, string digest, Dictionary<string, string> severities) => fixture.WithDbContextAsync(async db =>
    {
        var analysis = new FailureAnalysis
        {
            TestRunId = runId, Provider = "LogTriageAssistant", InputDigest = digest, CorrelationId = Guid.NewGuid().ToString("N"),
            RequestedAtUtc = DateTimeOffset.UtcNow
        };
        analysis.Start(DateTimeOffset.UtcNow);
        analysis.Succeed(DateTimeOffset.UtcNow.AddMilliseconds(1), new AnalysisOutcome(
            "ext", "HIGH", "fixture", 50, "General", "[]", "[]", JsonSerializer.Serialize(severities)));
        db.FailureAnalyses.Add(analysis);
        await db.SaveChangesAsync();
        await Task.Delay(5); // keep CompletedAtUtc ordering strict between inserts
        return 0;
    });
}
