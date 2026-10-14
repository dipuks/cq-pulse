using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CQPulse.Application.Decisions;
using Microsoft.Extensions.Options;

namespace CQPulse.Infrastructure.Integrations.SmartDog;

/// <summary>
/// "Integrations:SmartDog" configuration. The API key belongs in user secrets or environment variables. The integration
/// is disabled until both BaseUrl and ApiKey are set.
/// </summary>
public sealed class SmartDogOptions
{
    public const string SectionName = "Integrations:SmartDog";

    /// <summary>e.g. https://localhost:7018</summary>
    public string? BaseUrl { get; set; }
    /// <summary>One of SmartDog's DecisionApi:ApiKeys values.</summary>
    public string? ApiKey { get; set; }
    public int AttemptTimeoutSeconds { get; set; } = 10;
    /// <summary>Retries for transient failures only; evaluation is stateless, so repeating it is safe.</summary>
    public int RetryCount { get; set; } = 2;
    public int RetryBaseDelayMilliseconds { get; set; } = 300;

    public bool IsConfigured => Uri.TryCreate(BaseUrl, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>SmartDog could not be reached, timed out, or is not able to evaluate right now; retrying later may succeed.</summary>
public sealed class SmartDogUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>SmartDog answered but rejected the request or returned something that does not match its contract.</summary>
public sealed class SmartDogContractException(string message) : Exception(message);

/// <summary>Client for SmartDog's structured decision API (<c>POST /api/decisions/evaluate</c>, <c>GET /health</c>).</summary>
internal sealed class SmartDogClient(HttpClient http, IOptions<SmartDogOptions> options)
{
    public const string ApiKeyHeader = "X-Api-Key";
    public const string CorrelationHeader = "X-Correlation-ID";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <returns>The response body as received and its parsed form.</returns>
    public async Task<(string RawJson, DecisionResultDto Result)> EvaluateAsync(object request, string correlationId, CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured) throw new SmartDogUnavailableException("SmartDog is not configured.");
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/decisions/evaluate")
        {
            Content = JsonContent.Create(request, options: Json)
        };
        message.Headers.Add(ApiKeyHeader, options.Value.ApiKey);
        message.Headers.Add(CorrelationHeader, correlationId);

        using var response = await SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var status = (int)response.StatusCode;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new SmartDogContractException("SmartDog rejected the configured API key (401).");
        if (response.StatusCode == HttpStatusCode.BadRequest)
            throw new SmartDogContractException($"SmartDog rejected the evaluation request (400): {ValidationSummary(body)}");
        if (status is 408 or 429 or >= 500)
            throw new SmartDogUnavailableException($"SmartDog returned {status}. {ProblemDetail(body)}".Trim());
        if (!response.IsSuccessStatusCode)
            throw new SmartDogContractException($"SmartDog returned {status}. {ProblemDetail(body)}".Trim());

        DecisionResultDto? result;
        try
        {
            result = JsonSerializer.Deserialize<DecisionResultDto>(body, Json);
        }
        catch (JsonException exception)
        {
            throw new SmartDogContractException($"SmartDog returned malformed JSON: {exception.Message}");
        }
        if (result is null || string.IsNullOrWhiteSpace(result.Status) || result.Ranking is null || result.Criteria is null ||
            result.Infeasible is null || string.IsNullOrWhiteSpace(result.Method))
            throw new SmartDogContractException("SmartDog's response is missing status, method, criteria, ranking or infeasible options.");
        return (body, result);
    }

    public async Task<(bool Healthy, string Detail)> CheckHealthAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured) return (false, "Not configured.");
        try
        {
            using var response = await http.GetAsync("health", cancellationToken);
            return (response.IsSuccessStatusCode, $"/health returned {(int)response.StatusCode}");
        }
        catch (Exception exception) when (IsTransport(exception, cancellationToken) || exception is TaskCanceledException)
        {
            return (false, ProbeDetail.Unreachable(exception));
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(message, cancellationToken);
        }
        catch (Exception exception) when (IsTransport(exception, cancellationToken))
        {
            throw new SmartDogUnavailableException($"SmartDog is unavailable: {exception.GetBaseException().Message}", exception);
        }
    }

    private static bool IsTransport(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or TimeoutException or Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static string ValidationSummary(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                return string.Join(" ", errors.EnumerateObject().Select(e => $"{e.Name}: {string.Join(" ", e.Value.EnumerateArray().Select(v => v.GetString()))}"));
        }
        catch (JsonException) { }
        return Truncate(body);
    }

    private static string ProblemDetail(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("detail", out var detail)) return detail.GetString() ?? string.Empty;
            if (document.RootElement.TryGetProperty("title", out var title)) return title.GetString() ?? string.Empty;
        }
        catch (JsonException) { }
        return Truncate(body);
    }

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300];
}
