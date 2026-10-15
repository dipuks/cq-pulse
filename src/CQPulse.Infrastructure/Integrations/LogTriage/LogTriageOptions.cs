namespace CQPulse.Infrastructure.Integrations.LogTriage;

/// <summary>
/// "Integrations:LogTriage" configuration. Credentials belong in user secrets or environment variables, never in
/// appsettings files. The integration is disabled until BaseUrl, Username and Password are all set.
/// </summary>
public sealed class LogTriageOptions
{
    public const string SectionName = "Integrations:LogTriage";

    /// <summary>HTTPS base URL, e.g. https://localhost:7291 (LTA's session cookie is Secure-only).</summary>
    public string? BaseUrl { get; set; }
    /// <summary>A dedicated LogTriageAssistant account used only by CQ Pulse.</summary>
    public string? Username { get; set; }
    public string? Password { get; set; }
    /// <summary>Queue an analysis automatically when a run finishes with failures.</summary>
    public bool AutoAnalyzeFailedRuns { get; set; } = true;
    /// <summary>Upper bound for the submitted JSONL document (LTA accepts up to 2 MB).</summary>
    public int MaxDocumentBytes { get; set; } = 1_500_000;
    /// <summary>Maximum failed results included in one document; more are reported as truncated.</summary>
    public int MaxFailures { get; set; } = 2_000;
    public int AttemptTimeoutSeconds { get; set; } = 20;
    /// <summary>In-memory analysis queue size. When it is full, new analyses wait as Pending in the database.</summary>
    public int QueueCapacity { get; set; } = 500;
    /// <summary>How often Pending analyses that are not in the queue (it was full) are queued.</summary>
    public int PendingSweepSeconds { get; set; } = 15;
    /// <summary>Retries for transient failures only (network errors, 408, 429, 5xx).</summary>
    public int RetryCount { get; set; } = 2;
    public int RetryBaseDelayMilliseconds { get; set; } = 500;

    public bool IsValid => QueueCapacity is >= 1 and <= 10_000 && PendingSweepSeconds is >= 1 and <= 86_400;

    public bool IsConfigured =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}
