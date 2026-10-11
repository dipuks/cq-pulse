using System.Text.Json;
using CQPulse.Application.Simulation;
using CQPulse.Domain.Entities;
using CQPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Infrastructure.Simulator;

internal sealed class SimulationService(CQPulseDbContext dbContext, SimulationQueue queue) : ISimulationService
{
    private const int MaximumTests = 50_000;
    private const int MaximumConcurrency = 128;

    public SimulationPreview Preview(SimulationRequest request)
    {
        Validate(request);
        return new SimulationPreview(
            request.TestCount,
            request.TestCount * request.PassPercentage / 100,
            request.TestCount * request.FailurePercentage / 100,
            request.TestCount * request.SkippedPercentage / 100,
            request.MaxConcurrency,
            request.TestCount >= 10_000
                ? "Large workload: progress updates are throttled and result retrieval is paginated."
                : "Workload is within the standard simulator range.");
    }

    public async Task<Guid> QueueAsync(
        SimulationRequest request,
        string? userId,
        CancellationToken cancellationToken)
    {
        Validate(request);
        if (!await dbContext.Projects.AnyAsync(x => x.Id == request.ProjectId, cancellationToken))
        {
            throw new KeyNotFoundException("The selected project does not exist.");
        }

        if (request.TestSuiteId is not null &&
            !await dbContext.TestSuites.AnyAsync(
                x => x.Id == request.TestSuiteId && x.ProjectId == request.ProjectId,
                cancellationToken))
        {
            throw new KeyNotFoundException("The selected test suite does not belong to the project.");
        }

        var activeRunExists = await dbContext.TestRuns.AnyAsync(
            x => x.ProjectId == request.ProjectId && x.TestSuiteId == request.TestSuiteId &&
                 (x.Status == Domain.RunStatus.Queued || x.Status == Domain.RunStatus.Running),
            cancellationToken);
        if (activeRunExists)
        {
            throw new InvalidOperationException("An active simulation already exists for this target.");
        }

        var run = new TestRun
        {
            ProjectId = request.ProjectId,
            TestSuiteId = request.TestSuiteId,
            RunLabel = string.IsNullOrWhiteSpace(request.RunLabel)
                ? $"Simulation {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}"
                : request.RunLabel.Trim(),
            TotalTests = request.TestCount,
            ConfigurationSnapshot = JsonSerializer.Serialize(request),
            RandomSeed = request.RandomSeed,
            RequestedByUserId = userId
        };
        dbContext.TestRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        await queue.QueueAsync(run.Id, cancellationToken);
        return run.Id;
    }

    public async Task CancelAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await dbContext.TestRuns.SingleOrDefaultAsync(x => x.Id == runId, cancellationToken)
            ?? throw new KeyNotFoundException("The requested run does not exist.");
        run.Cancel(DateTimeOffset.UtcNow, "Cancelled by user.");
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void Validate(SimulationRequest request)
    {
        if (request.ProjectId == Guid.Empty) throw new ArgumentException("A project is required.");
        if (request.TestCount is < 1 or > MaximumTests) throw new ArgumentOutOfRangeException(nameof(request.TestCount));
        if (request.MaxConcurrency is < 1 or > MaximumConcurrency) throw new ArgumentOutOfRangeException(nameof(request.MaxConcurrency));
        if (request.MinimumDurationMs < 0 || request.MinimumDurationMs > request.MaximumDurationMs ||
            request.MaximumDurationMs > 60_000)
            throw new ArgumentException("Simulation durations are invalid.");
        if (request.PassPercentage + request.FailurePercentage + request.SkippedPercentage != 100)
            throw new ArgumentException("Outcome percentages must total 100.");
    }
}
