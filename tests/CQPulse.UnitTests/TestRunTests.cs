using CQPulse.Domain;
using CQPulse.Domain.Entities;

namespace CQPulse.UnitTests;

public sealed class TestRunTests
{
    [Fact]
    public void Complete_TransitionsRunningRunToPassedWhenThereAreNoFailures()
    {
        var run = CreateRun();
        var now = DateTimeOffset.UtcNow;

        run.Start(now);
        run.Complete(now.AddSeconds(1));

        Assert.Equal(RunStatus.Passed, run.Status);
        Assert.Equal(now.AddSeconds(1), run.CompletedAtUtc);
    }

    [Fact]
    public void Complete_TransitionsRunningRunToCompletedWithFailures()
    {
        var run = CreateRun();
        run.FailedCount = 1;

        run.Start(DateTimeOffset.UtcNow);
        run.Complete(DateTimeOffset.UtcNow);

        Assert.Equal(RunStatus.CompletedWithFailures, run.Status);
    }

    [Fact]
    public void Start_RejectsTerminalRun()
    {
        var run = CreateRun();
        run.Cancel(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => run.Start(DateTimeOffset.UtcNow));
    }

    private static TestRun CreateRun() => new()
    {
        ProjectId = Guid.NewGuid(),
        RunLabel = "Unit test",
        ConfigurationSnapshot = "{}",
        TotalTests = 1
    };
}
