using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using CQPulse.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using static CQPulse.IntegrationTests.Infrastructure.ApiTestHelpers;

namespace CQPulse.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class RealtimeTests(ApiFixture fixture)
{
    [Fact]
    public async Task Administrator_receives_progress_with_string_statuses_for_a_joined_run()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        await using var connection = await ConnectAsync(await fixture.GetTokenAsync(fixture.Administrator));
        var messages = new ConcurrentQueue<JsonElement>();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>("RunProgressUpdated", message =>
        {
            messages.Enqueue(message);
            if (message.GetProperty("status").GetString() is "Passed" or "CompletedWithFailures") finished.TrySetResult();
        });

        var runId = await client.QueueRunAsync(NewRequest(project.Id, 300) with { MaxConcurrency = 2, MinimumDurationMs = 5, MaximumDurationMs = 5 });
        await connection.InvokeAsync("JoinRun", runId);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.All(messages, m => Assert.Equal(JsonValueKind.String, m.GetProperty("status").ValueKind));
        Assert.All(messages, m => Assert.Equal(runId, m.GetProperty("runId").GetGuid()));
        var final = messages.Last();
        Assert.Equal(300, final.GetProperty("completedTests").GetInt32());
        Assert.Equal(300, final.GetProperty("passedCount").GetInt32() + final.GetProperty("failedCount").GetInt32()
            + final.GetProperty("skippedCount").GetInt32());
    }

    [Fact]
    public async Task Anonymous_hub_connection_is_rejected()
    {
        await using var connection = Build(accessToken: null);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task Non_administrator_hub_connection_is_forbidden()
    {
        await using var connection = Build(await fixture.GetTokenAsync(fixture.NonAdministrator));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task Cors_allows_credentialed_negotiate_from_the_client_origin()
    {
        using var client = fixture.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/hubs/runs/negotiate?negotiateVersion=1");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,x-requested-with");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:4200", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
    }

    [Fact]
    public async Task Cors_does_not_allow_unknown_origins()
    {
        using var client = fixture.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/hubs/runs/negotiate?negotiateVersion=1");
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private async Task<HubConnection> ConnectAsync(string token)
    {
        var connection = Build(token);
        await connection.StartAsync();
        return connection;
    }

    private HubConnection Build(string? accessToken) => new HubConnectionBuilder()
        .WithUrl("https://localhost/hubs/runs", options =>
        {
            // TestServer has no real sockets; long polling exercises the same negotiate, auth and hub pipeline.
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => fixture.Factory.Server.CreateHandler();
            if (accessToken is not null) options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
        })
        .Build();
}
