namespace CQPulse.Domain.Entities;

public sealed class TestCase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TestSuiteId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public TestPriority Priority { get; set; } = TestPriority.Medium;
    public SimulatedOutcome SimulatedOutcome { get; set; } = SimulatedOutcome.Random;
    public int SimulatedDurationMs { get; set; } = 1_000;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public TestSuite TestSuite { get; set; } = null!;
    public ICollection<TestCaseResult> Results { get; } = new List<TestCaseResult>();
}
