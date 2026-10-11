namespace CQPulse.Domain.Entities;

public sealed class TestCaseResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TestRunId { get; set; }
    public Guid? TestCaseId { get; set; }
    public required string TestCaseNameSnapshot { get; set; }
    public TestOutcome Outcome { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
    public int DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public FailureCategory? FailureCategory { get; set; }
    public TestRun TestRun { get; set; } = null!;
    public TestCase? TestCase { get; set; }
    public ICollection<ExecutionLog> Logs { get; } = new List<ExecutionLog>();
}
