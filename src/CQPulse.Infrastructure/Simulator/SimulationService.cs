using System.Text.Json;
using CQPulse.Application.Common;
using CQPulse.Application.Simulation;
using CQPulse.Domain;
using CQPulse.Domain.Entities;
using CQPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CQPulse.Infrastructure.Simulator;

internal sealed class SimulationService(
    CQPulseDbContext dbContext,
    SimulationQueue queue,
    RunCancellationRegistry cancellations,
    IOptions<SimulatorOptions> options) : ISimulationService
{
    private SimulatorOptions Limits => options.Value;

    public SimulatorLimitsDto GetLimits() =>
        new(Limits.MaxTestCount, Limits.MaxConcurrency, Limits.MaxDurationMs, Limits.LargeWorkloadThreshold);

    public async Task<SimulationPreview> PreviewAsync(SimulationRequest request, CancellationToken cancellationToken)
    {
        SimulationRequestValidator.Validate(request, Limits);
        var (projectName, suiteName) = await ResolveTargetAsync(request, cancellationToken);
        var allocation = SimulationWorkload.Allocate(
            request.TestCount, request.PassPercentage, request.FailurePercentage, request.SkippedPercentage);
        var averageDurationMs = (request.MinimumDurationMs + request.MaximumDurationMs) / 2.0;
        var estimatedSeconds = request.TestCount * averageDurationMs / Math.Min(request.MaxConcurrency, request.TestCount) / 1_000;

        return new SimulationPreview(
            request.TestCount,
            allocation.Passed,
            allocation.Failed,
            allocation.Skipped,
            request.MaxConcurrency,
            request.TestCount >= Limits.LargeWorkloadThreshold
                ? "Large workload: progress updates are throttled and results are persisted in batches."
                : "Workload is within the standard simulator range.",
            projectName,
            suiteName,
            request.MinimumDurationMs,
            request.MaximumDurationMs,
            request.FailurePercentage > 0 ? request.FailureCategories?.Distinct().ToList() ?? [] : [],
            request.RandomSeed,
            string.IsNullOrWhiteSpace(request.RunLabel) ? null : request.RunLabel.Trim(),
            Math.Round(estimatedSeconds, 1));
    }

    public async Task<Guid> QueueAsync(SimulationRequest request, string? userId, CancellationToken cancellationToken)
    {
        SimulationRequestValidator.Validate(request, Limits);
        await ResolveTargetAsync(request, cancellationToken);

        var activeRunExists = await dbContext.TestRuns.AnyAsync(
            x => x.ProjectId == request.ProjectId && x.TestSuiteId == request.TestSuiteId &&
                 (x.Status == RunStatus.Queued || x.Status == RunStatus.Running),
            cancellationToken);
        if (activeRunExists)
            throw new ConflictException("An active simulation already exists for this project and suite.");

        var run = new TestRun
        {
            ProjectId = request.ProjectId,
            TestSuiteId = request.TestSuiteId,
            RunLabel = string.IsNullOrWhiteSpace(request.RunLabel)
                ? $"Simulation {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC"
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
            ?? throw new NotFoundException("The requested run does not exist.");
        if (run.Status.IsTerminal())
            throw new ConflictException($"The run has already finished with status {run.Status}.");

        run.Cancel(DateTimeOffset.UtcNow, "Cancelled by user.");
        await dbContext.SaveChangesAsync(cancellationToken);
        cancellations.Cancel(runId);
    }

    private async Task<(string ProjectName, string? SuiteName)> ResolveTargetAsync(
        SimulationRequest request, CancellationToken cancellationToken)
    {
        var projectName = await dbContext.Projects.Where(x => x.Id == request.ProjectId)
            .Select(x => x.Name).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("The selected project does not exist.");
        if (request.TestSuiteId is null) return (projectName, null);

        var suiteName = await dbContext.TestSuites
            .Where(x => x.Id == request.TestSuiteId && x.ProjectId == request.ProjectId)
            .Select(x => x.Name).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("The selected test suite does not belong to the project.");
        return (projectName, suiteName);
    }
}
