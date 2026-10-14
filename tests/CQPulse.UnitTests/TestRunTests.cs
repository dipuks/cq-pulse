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

    [Fact]
    public void Complete_rejects_a_queued_run()
    {
        var run = CreateRun();

        Assert.Throws<InvalidOperationException>(() => run.Complete(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Fail_records_summary_and_completion_time()
    {
        var run = CreateRun();
        var now = DateTimeOffset.UtcNow;
        run.Start(now);

        run.Fail(now.AddSeconds(2), "Result persistence failed");

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("Result persistence failed", run.ErrorSummary);
        Assert.Equal(now.AddSeconds(2), run.CompletedAtUtc);
    }

    [Fact]
    public void Cancel_is_allowed_while_queued_or_running()
    {
        var queued = CreateRun();
        var running = CreateRun();
        running.Start(DateTimeOffset.UtcNow);

        queued.Cancel(DateTimeOffset.UtcNow, "user");
        running.Cancel(DateTimeOffset.UtcNow, "user");

        Assert.Equal(RunStatus.Cancelled, queued.Status);
        Assert.Equal(RunStatus.Cancelled, running.Status);
        Assert.Equal("user", running.CancellationReason);
    }

    public static TheoryData<Action<TestRun>> TerminalTransitions => new()
    {
        run => { run.Start(DateTimeOffset.UtcNow); run.Complete(DateTimeOffset.UtcNow); },
        run => run.Fail(DateTimeOffset.UtcNow, "failed"),
        run => run.Cancel(DateTimeOffset.UtcNow)
    };

    [Theory]
    [MemberData(nameof(TerminalTransitions))]
    public void Terminal_runs_reject_every_further_transition(Action<TestRun> makeTerminal)
    {
        var run = CreateRun();
        makeTerminal(run);
        var status = run.Status;

        Assert.True(status.IsTerminal());
        Assert.Throws<InvalidOperationException>(() => run.Start(DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => run.Complete(DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => run.Fail(DateTimeOffset.UtcNow, "again"));
        Assert.Throws<InvalidOperationException>(() => run.Cancel(DateTimeOffset.UtcNow));
        Assert.Equal(status, run.Status);
    }

    private static TestRun CreateRun() => new()
    {
        ProjectId = Guid.NewGuid(),
        RunLabel = "Unit test",
        ConfigurationSnapshot = "{}",
        TotalTests = 1
    };
}
