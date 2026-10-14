using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CQPulse.IntegrationTests.Infrastructure;

/// <summary>
/// Deterministic test double for SmartDog's decision API contract (verified against SmartDog's DecisionsController and
/// README): <c>POST api/decisions/evaluate</c> with <c>X-Api-Key</c>, and anonymous <c>GET health</c>. The healthy
/// response ranks options by their first criterion so tests can predict it; the real weighted method is verified in
/// SmartDog's own tests and in the end-to-end run against a real instance.
/// </summary>
public sealed class FakeSmartDogHandler : HttpMessageHandler
{
    public enum Mode { Healthy, ServerError, Unauthorized, BadRequest, Malformed, Slow }

    public const string ApiKey = "fake-smartdog-key-0123456789";

    public Mode Behavior { get; set; } = Mode.Healthy;
    public bool Ready { get; set; } = true;
    /// <summary>Delays /health responses, e.g. to exercise status-check timeouts.</summary>
    public TimeSpan HealthDelay { get; set; } = TimeSpan.Zero;
    public int EvaluateCalls;
    public ConcurrentQueue<(JsonElement Body, string? ApiKey, string? CorrelationId)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        if (request.Method == HttpMethod.Get && path == "health")
        {
            await Task.Delay(HealthDelay, cancellationToken);
            return new HttpResponseMessage(Ready ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable);
        }
        if (request.Method != HttpMethod.Post || path != "api/decisions/evaluate")
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        Interlocked.Increment(ref EvaluateCalls);
        var key = request.Headers.TryGetValues("X-Api-Key", out var keys) ? keys.Single() : null;
        var correlation = request.Headers.TryGetValues("X-Correlation-ID", out var ids) ? ids.Single() : null;
        var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
        Requests.Enqueue((body, key, correlation));

        if (Behavior == Mode.Unauthorized || key != ApiKey)
            return Problem(HttpStatusCode.Unauthorized, new { title = "A valid API key is required." });
        switch (Behavior)
        {
            case Mode.ServerError: return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            case Mode.BadRequest:
                return Problem(HttpStatusCode.BadRequest, new { title = "The evaluation request is invalid.", errors = new Dictionary<string, string[]> { ["criteria[0].weight"] = ["Weight must be greater than 0 and at most 1000."] } });
            case Mode.Malformed: return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>oops</html>") };
            case Mode.Slow: await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken); break;
        }

        var criteria = body.GetProperty("criteria").EnumerateArray().ToList();
        var first = criteria[0].GetProperty("id").GetString()!;
        var options = body.GetProperty("options").EnumerateArray()
            .Select(o => (Id: o.GetProperty("id").GetString()!, Name: o.GetProperty("name").GetString()!, Value: o.GetProperty("values").GetProperty(first).GetDouble()))
            .OrderByDescending(o => o.Value).ThenBy(o => o.Id, StringComparer.Ordinal).ToList();
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                evaluationId = Guid.NewGuid(),
                status = "Ranked",
                question = body.GetProperty("question").GetString(),
                method = "Fake: ordered by the first criterion.",
                criteria = criteria.Select(c => new
                {
                    id = c.GetProperty("id").GetString(), name = c.GetProperty("name").GetString(), direction = "maximize",
                    weight = c.GetProperty("weight").GetDouble(), normalizedWeight = 0.25, minimumValue = 0.0, maximumValue = 1.0
                }),
                ranking = options.Select((o, i) => new
                {
                    rank = i + 1, optionId = o.Id, name = o.Name, score = 1.0 - i * 0.1,
                    contributions = new[] { new { criterionId = first, rawValue = o.Value, normalizedValue = 1.0, normalizedWeight = 1.0, contribution = 1.0 } },
                    explanation = $"Rank {i + 1}.", tiedWithOptionIds = Array.Empty<string>()
                }),
                infeasible = Array.Empty<object>(),
                notes = new[] { "fake note" },
                limitations = "Not a prediction."
            })
        };
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, object body) => new(status) { Content = JsonContent.Create(body) };
}
