using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CQPulse.Infrastructure.Integrations.LogTriage;

/// <summary>The service could not be reached or did not answer in time; retrying later may succeed.</summary>
public sealed class LogTriageUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The service answered, but the request was rejected or the response did not match the contract.</summary>
public sealed class LogTriageContractException(string message) : Exception(message);

/// <summary>Holds the CSRF token for the current LogTriageAssistant session (the session cookie lives in the handler).</summary>
internal sealed class LogTriageSession
{
    public SemaphoreSlim Lock { get; } = new(1, 1);
    public string? CsrfToken { get; set; }
}

/// <summary>
/// Client for LogTriageAssistant's existing browser API: CSRF token + cookie sign-in with a dedicated account, then
/// <c>POST /api/analyze</c>. Re-authenticates once when the session or CSRF token is rejected. Transient failures are
/// retried by the resilience pipeline configured at registration.
/// </summary>
internal sealed class LogTriageClient(
    HttpClient http,
    LogTriageSession session,
    IOptions<LogTriageOptions> options,
    ILogger<LogTriageClient> logger)
{
    public const string CorrelationHeader = "X-Correlation-ID";
    private const string CsrfHeader = "X-CSRF-TOKEN";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<LogTriageAnalysis> AnalyzeAsync(string logText, string fileName, string correlationId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        for (var attempt = 1; ; attempt++)
        {
            var token = await EnsureSessionAsync(forceLogin: attempt > 1, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/analyze")
            {
                Content = JsonContent.Create(new { logText, fileName }, options: Json)
            };
            request.Headers.Add(CsrfHeader, token);
            request.Headers.Add(CorrelationHeader, correlationId);

            using var response = await SendAsync(request, cancellationToken);
            if (attempt == 1 && IsSessionRejected(response, await PeekAsync(response, cancellationToken)))
            {
                logger.LogInformation("LogTriageAssistant session rejected ({Status}); signing in again.", (int)response.StatusCode);
                continue;
            }

            await ThrowIfFailedAsync(response, "Analysis", cancellationToken);
            LogTriageAnalysis? analysis;
            try
            {
                analysis = await response.Content.ReadFromJsonAsync<LogTriageAnalysis>(Json, cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new LogTriageContractException($"LogTriageAssistant returned malformed JSON: {exception.Message}");
            }
            return Validate(analysis);
        }
    }

    /// <summary>Calls the anonymous readiness endpoint. Returns (healthy, detail).</summary>
    public async Task<(bool Healthy, string Detail)> CheckReadinessAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        try
        {
            using var response = await http.GetAsync("health/ready", cancellationToken);
            return (response.IsSuccessStatusCode, $"/health/ready returned {(int)response.StatusCode}");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or TimeoutException
                                          or Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException)
        {
            return (false, ProbeDetail.Unreachable(exception));
        }
    }

    private async Task<string> EnsureSessionAsync(bool forceLogin, CancellationToken cancellationToken)
    {
        await session.Lock.WaitAsync(cancellationToken);
        try
        {
            if (!forceLogin && session.CsrfToken is { } existing) return existing;
            session.CsrfToken = null;

            // Antiforgery tokens are bound to the user identity, so fetch one before and one after signing in.
            var anonymousToken = await GetCsrfTokenAsync(cancellationToken);
            using var login = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
            {
                Content = JsonContent.Create(new { username = options.Value.Username, password = options.Value.Password }, options: Json)
            };
            login.Headers.Add(CsrfHeader, anonymousToken);
            using var loginResponse = await SendAsync(login, cancellationToken);
            if (loginResponse.StatusCode == HttpStatusCode.Unauthorized)
                throw new LogTriageContractException("LogTriageAssistant rejected the configured service account credentials.");
            await ThrowIfFailedAsync(loginResponse, "Sign-in", cancellationToken);

            session.CsrfToken = await GetCsrfTokenAsync(cancellationToken);
            return session.CsrfToken;
        }
        finally
        {
            session.Lock.Release();
        }
    }

    private async Task<string> GetCsrfTokenAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, "api/auth/csrf"), cancellationToken);
        await ThrowIfFailedAsync(response, "CSRF token request", cancellationToken);
        try
        {
            var body = await response.Content.ReadFromJsonAsync<CsrfResponse>(Json, cancellationToken);
            return string.IsNullOrWhiteSpace(body?.Token)
                ? throw new LogTriageContractException("LogTriageAssistant returned an empty CSRF token.")
                : body.Token;
        }
        catch (JsonException)
        {
            throw new LogTriageContractException("LogTriageAssistant returned a malformed CSRF response.");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or Polly.CircuitBreaker.BrokenCircuitException
                                          or Polly.Timeout.TimeoutRejectedException
                                          || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new LogTriageUnavailableException($"LogTriageAssistant is unavailable: {ProbeDetail.Reason(exception)}", exception);
        }
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var status = (int)response.StatusCode;
        var body = await PeekAsync(response, cancellationToken);
        if (status is 408 or 429 or >= 500)
            throw new LogTriageUnavailableException($"{operation} failed: LogTriageAssistant returned {status}. {body}".Trim());
        throw new LogTriageContractException($"{operation} was rejected by LogTriageAssistant ({status}). {body}".Trim());
    }

    private static bool IsSessionRejected(HttpResponseMessage response, string body) =>
        response.StatusCode == HttpStatusCode.Unauthorized ||
        (response.StatusCode == HttpStatusCode.BadRequest && body.Contains("CSRF", StringComparison.OrdinalIgnoreCase));

    private static async Task<string> PeekAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return string.Empty;
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return text.Length <= 300 ? text : text[..300];
    }

    private static LogTriageAnalysis Validate(LogTriageAnalysis? analysis)
    {
        if (analysis is null || string.IsNullOrWhiteSpace(analysis.Severity) || analysis.Summary is null || analysis.Findings is null)
            throw new LogTriageContractException("LogTriageAssistant's response is missing severity, summary or findings.");
        return analysis;
    }

    private void EnsureConfigured()
    {
        if (!options.Value.IsConfigured) throw new LogTriageUnavailableException("LogTriageAssistant is not configured.");
    }

    private sealed record CsrfResponse(string? Token);
}

/// <summary>Subset of LogTriageAssistant's LogAnalysisResult that CQ Pulse consumes.</summary>
public sealed record LogTriageAnalysis(
    string? Id, DateTimeOffset? AnalyzedAt, string? Summary, string? Severity, double? Confidence, bool FindingsTruncated,
    LogTriageStatistics? Statistics, List<LogTriageFinding>? Findings, List<string>? Recommendations);

public sealed record LogTriageStatistics(int TotalLines, int ErrorLikeLines, int UniqueCategories, string? TopCategory);

public sealed record LogTriageFinding(
    int LineNumber, int EndLineNumber, string? Timestamp, string? Message, string? Severity, string? Category, string? Evidence, string? Suggestion);
