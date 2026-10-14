using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CQPulse.IntegrationTests.Infrastructure;

/// <summary>
/// Deterministic test double for LogTriageAssistant's HTTP contract (verified against its Program.cs): anonymous
/// <c>GET api/auth/csrf</c>, CSRF-protected <c>POST api/auth/login</c> and <c>POST api/analyze</c> (tokens are bound to
/// the session, so a token issued before sign-in is rejected afterwards), and <c>GET health/ready</c>.
/// It replaces only the network handler; CQ Pulse's real client, resilience pipeline and worker run unchanged.
/// </summary>
public sealed class FakeLogTriageHandler : HttpMessageHandler
{
    public enum Mode { Healthy, ServerError, Malformed, Slow, ExpireSessionOnce, RejectCredentials }

    public const string Username = "cqpulse-service";
    public const string Password = "Service-account-password-1!";

    private readonly object _gate = new();
    private int _tokenCounter;
    private bool _signedIn;
    private string? _sessionToken;
    private bool _expiredOnce;

    public Mode Behavior { get; set; } = Mode.Healthy;
    public TimeSpan SlowDelay { get; set; } = TimeSpan.FromSeconds(5);
    public bool Ready { get; set; } = true;
    public int LoginCalls;
    public int AnalyzeCalls;
    public ConcurrentQueue<(string Body, string? CorrelationId)> AnalyzeRequests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        var csrf = request.Headers.TryGetValues("X-CSRF-TOKEN", out var values) ? values.Single() : null;
        switch (request.Method.Method, path)
        {
            case ("GET", "health/ready"):
                return new HttpResponseMessage(Ready ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable);

            case ("GET", "api/auth/csrf"):
                lock (_gate)
                {
                    var token = $"csrf-{++_tokenCounter}-{(_signedIn ? "user" : "anon")}";
                    if (_signedIn) _sessionToken = token;
                    return Json(new { token });
                }

            case ("POST", "api/auth/login"):
            {
                Interlocked.Increment(ref LoginCalls);
                if (csrf is null || !csrf.EndsWith("-anon")) return Error(HttpStatusCode.BadRequest, "A valid CSRF token is required.");
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement;
                var ok = Behavior != Mode.RejectCredentials &&
                         body.GetProperty("username").GetString() == Username && body.GetProperty("password").GetString() == Password;
                if (!ok) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                lock (_gate) { _signedIn = true; _sessionToken = null; }
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            case ("POST", "api/analyze"):
            {
                Interlocked.Increment(ref AnalyzeCalls);
                lock (_gate)
                {
                    if (Behavior == Mode.ExpireSessionOnce && !_expiredOnce)
                    {
                        _expiredOnce = true;
                        _signedIn = false;
                        _sessionToken = null;
                    }
                    if (!_signedIn) return Error(HttpStatusCode.Unauthorized, "Authentication is required.");
                    if (csrf is null || csrf != _sessionToken) return Error(HttpStatusCode.BadRequest, "A valid CSRF token is required.");
                }
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement;
                var logText = body.GetProperty("logText").GetString()!;
                AnalyzeRequests.Enqueue((logText, request.Headers.TryGetValues("X-Correlation-ID", out var ids) ? ids.Single() : null));

                switch (Behavior)
                {
                    case Mode.ServerError:
                        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                    case Mode.Malformed:
                        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>not json</html>") };
                    case Mode.Slow:
                        await Task.Delay(SlowDelay, cancellationToken);
                        break;
                }
                var lines = logText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var errors = lines.Count(l => l.Contains("\"level\":\"ERROR\""));
                return Json(new
                {
                    id = Guid.NewGuid().ToString("N"),
                    analyzedAt = DateTimeOffset.UtcNow,
                    summary = $"Detected {errors} actionable findings with the strongest signal in Timeout. Overall severity is HIGH.",
                    severity = errors > 0 ? "HIGH" : "INFO",
                    confidence = 82, // LTA reports a 0–100 heuristic signal score
                    findingsTruncated = false,
                    statistics = new { totalLines = lines.Length, errorLikeLines = errors, uniqueCategories = 1, topCategory = "Timeout" },
                    findings = lines.Select((l, i) => (l, i)).Where(x => x.l.Contains("\"level\":\"ERROR\"")).Take(3).Select(x => new
                    {
                        lineNumber = x.i + 1, endLineNumber = x.i + 1, timestamp = (string?)null,
                        message = "Test failed", severity = "HIGH", category = "Timeout",
                        evidence = x.l.Length > 120 ? x.l[..120] : x.l, suggestion = "Check downstream latency and timeouts."
                    }),
                    recommendations = new[] { "Check downstream latency and timeouts." }
                });
            }

            default:
                return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private static HttpResponseMessage Error(HttpStatusCode status, string error) => new(status) { Content = JsonContent.Create(new { error }) };
}
