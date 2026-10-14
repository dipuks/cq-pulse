using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CQPulse.Application.Catalog;
using CQPulse.Application.Runs;
using CQPulse.Application.Simulation;
using CQPulse.Domain;

namespace CQPulse.IntegrationTests.Infrastructure;

public static class ApiTestHelpers
{
    /// <summary>Matches the API: web defaults with enums as strings.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public static async Task<T> GetJsonAsync<T>(this HttpClient client, string url) => await (await client.GetAsync(url)).ReadAsync<T>();

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, Json);

    public static async Task<ProjectDto> CreateProjectAsync(this HttpClient client, string? name = null) =>
        await (await client.PostJsonAsync("/api/projects", new SaveProjectRequest(name ?? $"Project {Guid.NewGuid():N}", "Integration test project")))
            .ReadAsync<ProjectDto>();

    public static SimulationRequest NewRequest(Guid projectId, int testCount = 200, int? seed = 42) =>
        new(projectId, null, testCount, 16, 0, 0, 70, 20, 10,
            [FailureCategory.UiElement, FailureCategory.ApiError, FailureCategory.Timeout], seed, "Integration run");

    public static async Task<Guid> QueueRunAsync(this HttpClient client, SimulationRequest request) =>
        (await (await client.PostJsonAsync("/api/simulator/runs", request)).ReadAsync<QueuedRun>()).RunId;

    public static Task<RunDetailDto> GetRunAsync(this HttpClient client, Guid runId) =>
        client.GetJsonAsync<RunDetailDto>($"/api/runs/{runId}");

    /// <summary>Polls the persisted run until it reaches a terminal status, failing the test on timeout.</summary>
    public static async Task<RunDetailDto> WaitForTerminalAsync(this HttpClient client, Guid runId, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (true)
        {
            var run = await client.GetRunAsync(runId);
            if (run.Status.IsTerminal()) return run;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Run {runId} still {run.Status} after {(timeout ?? TimeSpan.FromSeconds(30)).TotalSeconds}s.");
            await Task.Delay(100);
        }
    }

    public static async Task<RunDetailDto> RunToCompletionAsync(this HttpClient client, SimulationRequest request) =>
        await client.WaitForTerminalAsync(await client.QueueRunAsync(request));

    private sealed record QueuedRun(Guid RunId);
}
