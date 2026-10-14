using System.Net;
using System.Text.Json;
using CQPulse.Application.Catalog;
using CQPulse.IntegrationTests.Infrastructure;
using static CQPulse.IntegrationTests.Infrastructure.ApiTestHelpers;

namespace CQPulse.IntegrationTests;

/// <summary>Expected failures return Problem Details with 400/404/409, never 500.</summary>
[Collection(ApiCollection.Name)]
public sealed class ErrorMappingTests(ApiFixture fixture)
{
    [Fact]
    public async Task Invalid_simulation_request_returns_400_with_field_errors()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();

        var response = await client.PostJsonAsync("/api/simulator/preview",
            NewRequest(project.Id) with { TestCount = 0, PassPercentage = 10 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var errors = await ReadErrorsAsync(response);
        Assert.Contains("TestCount", errors);
        Assert.Contains("Percentages", errors);
    }

    [Fact]
    public async Task Simulation_for_unknown_project_returns_404()
    {
        using var client = await fixture.CreateAdministratorClientAsync();

        var preview = await client.PostJsonAsync("/api/simulator/preview", NewRequest(Guid.NewGuid()));
        var queue = await client.PostJsonAsync("/api/simulator/runs", NewRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, queue.StatusCode);
    }

    [Fact]
    public async Task Simulation_with_suite_from_another_project_returns_404()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        var other = await client.CreateProjectAsync();
        var suite = await (await client.PostJsonAsync($"/api/projects/{other.Id}/suites", new SaveTestSuiteRequest("Other suite", null)))
            .ReadAsync<TestSuiteDto>();

        var response = await client.PostJsonAsync("/api/simulator/preview", NewRequest(project.Id) with { TestSuiteId = suite.Id });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cancelling_unknown_run_returns_404()
    {
        using var client = await fixture.CreateAdministratorClientAsync();

        var response = await client.PostAsync($"/api/simulator/runs/{Guid.NewGuid()}/cancel", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cancelling_finished_run_returns_409_and_leaves_it_unchanged()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        var run = await client.RunToCompletionAsync(NewRequest(project.Id, testCount: 20));

        var response = await client.PostAsync($"/api/simulator/runs/{run.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(run.Status, (await client.GetRunAsync(run.Id)).Status);
    }

    [Fact]
    public async Task Duplicate_suite_name_returns_409()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        await (await client.PostJsonAsync($"/api/projects/{project.Id}/suites", new SaveTestSuiteRequest("Checkout", null))).ReadAsync<TestSuiteDto>();

        var response = await client.PostJsonAsync($"/api/projects/{project.Id}/suites", new SaveTestSuiteRequest(" Checkout ", null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Renaming_suite_to_existing_name_returns_409()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        await (await client.PostJsonAsync($"/api/projects/{project.Id}/suites", new SaveTestSuiteRequest("Login", null))).ReadAsync<TestSuiteDto>();
        var second = await (await client.PostJsonAsync($"/api/projects/{project.Id}/suites", new SaveTestSuiteRequest("Search", null)))
            .ReadAsync<TestSuiteDto>();

        var response = await client.PutAsync($"/api/suites/{second.Id}",
            JsonContent(new SaveTestSuiteRequest("Login", null)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_project_name_returns_400(string name)
    {
        using var client = await fixture.CreateAdministratorClientAsync();

        var response = await client.PostJsonAsync("/api/projects", new SaveProjectRequest(name, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Name", await ReadErrorsAsync(response));
    }

    [Fact]
    public async Task Test_case_with_out_of_range_duration_returns_400()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        var suite = await (await client.PostJsonAsync($"/api/projects/{project.Id}/suites", new SaveTestSuiteRequest("Cases", null)))
            .ReadAsync<TestSuiteDto>();

        var response = await client.PostJsonAsync($"/api/suites/{suite.Id}/cases",
            new SaveTestCaseRequest("Slow", null, true, Domain.TestPriority.Low, Domain.SimulatedOutcome.Random, 60_001));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SimulatedDurationMs", await ReadErrorsAsync(response));
    }

    private static async Task<IReadOnlyCollection<string>> ReadErrorsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("errors").EnumerateObject().Select(x => x.Name).ToList();
    }

    private static HttpContent JsonContent<T>(T body) => System.Net.Http.Json.JsonContent.Create(body, options: Json);
}
