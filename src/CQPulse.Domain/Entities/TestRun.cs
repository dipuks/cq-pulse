namespace CQPulse.Domain.Entities;

public sealed class TestRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid? TestSuiteId { get; set; }
    /// <summary>Set when this run was created by re-running another run with the same configuration.</summary>
    public Guid? RerunOfRunId { get; set; }
    public required string RunLabel { get; set; }
    public RunStatus Status { get; private set; } = RunStatus.Queued;
    public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public int TotalTests { get; set; }
    public int CompletedTests { get; set; }
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
    public required string ConfigurationSnapshot { get; set; }
    public int? RandomSeed { get; set; }
    public string? RequestedByUserId { get; set; }
    public string? ErrorSummary { get; private set; }
    public string? CancellationReason { get; private set; }
    public Project Project { get; set; } = null!;
    public TestSuite? TestSuite { get; set; }
    public ICollection<TestCaseResult> Results { get; } = new List<TestCaseResult>();
    public ICollection<ExecutionLog> Logs { get; } = new List<ExecutionLog>();

    public void Start(DateTimeOffset now)
    {
        if (Status != RunStatus.Queued) throw new InvalidOperationException("Only queued runs can start.");
        Status = RunStatus.Running;
        StartedAtUtc = now;
    }

    public void Complete(DateTimeOffset now)
    {
        if (Status != RunStatus.Running) throw new InvalidOperationException("Only running runs can complete.");
        Status = FailedCount == 0 ? RunStatus.Passed : RunStatus.CompletedWithFailures;
        CompletedAtUtc = now;
    }

    public void Fail(DateTimeOffset now, string summary)
    {
        if (Status is not (RunStatus.Queued or RunStatus.Running)) throw new InvalidOperationException("Terminal runs cannot fail.");
        Status = RunStatus.Failed;
        ErrorSummary = summary;
        CompletedAtUtc = now;
    }

    public void Cancel(DateTimeOffset now, string? reason = null)
    {
        if (Status is not (RunStatus.Queued or RunStatus.Running)) throw new InvalidOperationException("Terminal runs cannot be cancelled.");
        Status = RunStatus.Cancelled;
        CancellationReason = reason;
        CompletedAtUtc = now;
    }
}
