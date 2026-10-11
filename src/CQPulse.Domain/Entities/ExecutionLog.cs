namespace CQPulse.Domain.Entities;

public sealed class ExecutionLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TestRunId { get; set; }
    public Guid? TestCaseResultId { get; set; }
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public LogLevel Level { get; set; } = LogLevel.Information;
    public required string Message { get; set; }
    public string? MetadataJson { get; set; }
    public TestRun TestRun { get; set; } = null!;
    public TestCaseResult? TestCaseResult { get; set; }
}
