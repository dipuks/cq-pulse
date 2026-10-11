namespace CQPulse.Domain;

public static class RunStatusExtensions
{
    public static bool IsTerminal(this RunStatus status) =>
        status is RunStatus.Passed or RunStatus.CompletedWithFailures or RunStatus.Failed or RunStatus.Cancelled;
}
