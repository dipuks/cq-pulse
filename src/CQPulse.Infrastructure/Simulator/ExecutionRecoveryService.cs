using CQPulse.Domain;
using CQPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CQPulse.Infrastructure.Simulator;

internal sealed class ExecutionRecoveryService(
    IServiceScopeFactory scopeFactory,
    ILogger<ExecutionRecoveryService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CQPulseDbContext>();
        var interruptedRuns = await dbContext.TestRuns
            .Where(x => x.Status == RunStatus.Queued || x.Status == RunStatus.Running)
            .ToListAsync(cancellationToken);
        if (interruptedRuns.Count == 0) return;

        var now = DateTimeOffset.UtcNow;
        foreach (var run in interruptedRuns)
        {
            run.Fail(now, "Execution was interrupted by an application restart.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogWarning("Marked {RunCount} interrupted simulation runs as failed during startup recovery.", interruptedRuns.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
