namespace CQPulse.Domain.Entities;

public sealed class TestSuite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Project Project { get; set; } = null!;
    public ICollection<TestCase> TestCases { get; } = new List<TestCase>();
    public ICollection<TestRun> TestRuns { get; } = new List<TestRun>();
}
