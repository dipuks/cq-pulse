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
        var cases = await LoadCatalogAsync(request, cancellationToken);
        var workload = new SimulationWorkload(request, request.RandomSeed ?? 0, cases);
        var averageDurationMs = cases is null
            ? (request.MinimumDurationMs + request.MaximumDurationMs) / 2.0
            : cases.Average(x => x.DurationMs);
        var estimatedSeconds = request.TestCount * averageDurationMs / Math.Min(request.MaxConcurrency, request.TestCount) / 1_000;

        return new SimulationPreview(
            request.TestCount,
            workload.Allocation.Passed,
            workload.Allocation.Failed,
            workload.Allocation.Skipped,
            request.MaxConcurrency,
            request.TestCount >= Limits.LargeWorkloadThreshold
                ? "Large workload: progress updates are throttled and results are persisted in batches."
                : "Workload is within the standard simulator range.",
            projectName,
            suiteName,
            cases is null ? request.MinimumDurationMs : cases.Min(x => x.DurationMs),
            cases is null ? request.MaximumDurationMs : cases.Max(x => x.DurationMs),
            request.FailurePercentage > 0 ? request.FailureCategories?.Distinct().ToList() ?? [] : [],
            request.RandomSeed,
            string.IsNullOrWhiteSpace(request.RunLabel) ? null : request.RunLabel.Trim(),
            Math.Round(estimatedSeconds, 1),
            request.WorkloadSource,
            cases?.Count ?? 0,
            DescribeWorkload(request, cases));
    }

    public async Task<Guid> QueueAsync(SimulationRequest request, string? userId, CancellationToken cancellationToken) =>
        await QueueAsync(request, userId, rerunOfRunId: null, cancellationToken);

    public async Task<Guid> RerunAsync(Guid runId, string? userId, CancellationToken cancellationToken)
    {
        var original = await dbContext.TestRuns.AsNoTracking().Where(x => x.Id == runId)
            .Select(x => new { x.ConfigurationSnapshot, x.RunLabel })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("The requested run does not exist.");

        SimulationRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<SimulationRequest>(original.ConfigurationSnapshot);
        }
        catch (JsonException)
        {
            request = null;
        }
        if (request is null)
            throw new ConflictException("The original run's configuration cannot be read, so it cannot be rerun.");

        var label = $"Rerun of {original.RunLabel}";
        if (label.Length > SimulationRequestValidator.MaxRunLabelLength) label = label[..SimulationRequestValidator.MaxRunLabelLength];
        return await QueueAsync(request with { RunLabel = label }, userId, runId, cancellationToken);
    }

    private async Task<Guid> QueueAsync(SimulationRequest request, string? userId, Guid? rerunOfRunId, CancellationToken cancellationToken)
    {
        SimulationRequestValidator.Validate(request, Limits);
        await ResolveTargetAsync(request, cancellationToken);
        await LoadCatalogAsync(request, cancellationToken); // rejects catalog runs with no enabled cases

        var activeRunExists = await dbContext.TestRuns.AnyAsync(
            x => x.ProjectId == request.ProjectId && x.TestSuiteId == request.TestSuiteId &&
                 (x.Status == RunStatus.Queued || x.Status == RunStatus.Running),
            cancellationToken);
        if (activeRunExists)
            throw new ConflictException(ActiveRunConflictMessage);

        var run = new TestRun
        {
            ProjectId = request.ProjectId,
            TestSuiteId = request.TestSuiteId,
            RerunOfRunId = rerunOfRunId,
            RunLabel = string.IsNullOrWhiteSpace(request.RunLabel)
                ? $"Simulation {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC"
                : request.RunLabel.Trim(),
            TotalTests = request.TestCount,
            ConfigurationSnapshot = JsonSerializer.Serialize(request),
            RandomSeed = request.RandomSeed,
            RequestedByUserId = userId
        };
        dbContext.TestRuns.Add(run);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsActiveRunIndexViolation(exception))
        {
            // Two submissions raced past the check above; the filtered unique index admits only one.
            throw new ConflictException(ActiveRunConflictMessage);
        }
        await queue.QueueAsync(run.Id, cancellationToken);
        return run.Id;
    }

    /// <summary>Null for synthetic workloads; the enabled cases for catalog workloads (never empty).</summary>
    private async Task<List<CatalogCase>?> LoadCatalogAsync(SimulationRequest request, CancellationToken cancellationToken)
    {
        if (request.WorkloadSource != WorkloadSource.Catalog) return null;
        var cases = await CatalogWorkloadLoader.LoadAsync(dbContext, request.ProjectId, request.TestSuiteId, cancellationToken);
        if (cases.Count == 0)
            throw ValidationException.For(nameof(request.WorkloadSource),
                "The selected target has no enabled test cases. Add or enable test cases, or use a synthetic workload.");
        return cases;
    }

    private static string DescribeWorkload(SimulationRequest request, IReadOnlyList<CatalogCase>? cases)
    {
        if (cases is null)
            return $"Synthetic workload: {request.TestCount:N0} generated tests with outcomes from the configured percentages.";
        var fixedCount = cases.Count(x => x.Outcome != SimulatedOutcome.Random);
        var cycles = request.TestCount / (double)cases.Count;
        return $"Catalog workload: {cases.Count:N0} enabled test case(s) executed {cycles:0.##}× to reach {request.TestCount:N0} tests. " +
               $"{fixedCount:N0} case(s) have a fixed simulated outcome; the rest use the configured percentages. Each case uses its configured duration.";
    }


    public async Task CancelAsync(Guid runId, CancellationToken cancellationToken)
    {
        // The worker updates progress counters on the same row, so retry on rowversion conflicts and
        // re-check the status each time: a run that finished meanwhile can no longer be cancelled.
        for (var attempt = 1; ; attempt++)
        {
            dbContext.ChangeTracker.Clear();
            var run = await dbContext.TestRuns.SingleOrDefaultAsync(x => x.Id == runId, cancellationToken)
                ?? throw new NotFoundException("The requested run does not exist.");
            if (run.Status.IsTerminal())
                throw new ConflictException($"The run has already finished with status {run.Status}.");

            run.Cancel(DateTimeOffset.UtcNow, "Cancelled by user.");
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxCancelAttempts)
            {
                await Task.Delay(attempt * 10, cancellationToken);
            }
        }

        cancellations.Cancel(runId);
    }

    private const int MaxCancelAttempts = 10;
    private const string ActiveRunConflictMessage = "An active simulation already exists for this project and suite.";

    private static bool IsActiveRunIndexViolation(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains(CQPulseDbContext.ActiveRunIndexName, StringComparison.Ordinal) == true;

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
