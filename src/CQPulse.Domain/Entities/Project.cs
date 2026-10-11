namespace CQPulse.Domain.Entities;

public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Description { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Active;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<TestSuite> TestSuites { get; } = new List<TestSuite>();
    public ICollection<TestRun> TestRuns { get; } = new List<TestRun>();
}
