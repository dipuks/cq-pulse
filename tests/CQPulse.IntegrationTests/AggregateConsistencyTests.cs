using CQPulse.Application.Analytics;
using CQPulse.Application.Runs;
using CQPulse.IntegrationTests.Infrastructure;
using static CQPulse.IntegrationTests.Infrastructure.ApiTestHelpers;

namespace CQPulse.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AggregateConsistencyTests(ApiFixture fixture)
{
    /// <summary>Run history (dashboard counters) and analytics (persisted rows) must always agree.</summary>
    [Fact]
    public async Task Dashboard_and_analytics_totals_agree_after_runs()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        await client.RunToCompletionAsync(NewRequest(project.Id, 333));

        var dashboard = await client.GetJsonAsync<DashboardSummaryDto>("/api/dashboard/summary");
        var analytics = await client.GetJsonAsync<AnalyticsSummaryDto>("/api/analytics/summary");
        var projectAnalytics = await client.GetJsonAsync<AnalyticsSummaryDto>($"/api/analytics/summary?projectId={project.Id}");

        Assert.Equal((dashboard.Passed, dashboard.Failed, dashboard.Skipped), (analytics.Passed, analytics.Failed, analytics.Skipped));
        Assert.Equal(dashboard.PassRate, analytics.PassRate);
        Assert.Equal(333, projectAnalytics.Total);
        Assert.Equal((233, 67, 33), (projectAnalytics.Passed, projectAnalytics.Failed, projectAnalytics.Skipped));
    }
}
