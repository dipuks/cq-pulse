using CQPulse.Application.Analysis;
using CQPulse.Application.Common;
using CQPulse.Application.Insights;
using CQPulse.Domain;
using CQPulse.Domain.Entities;
using CQPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Api.Controllers;

/// <summary>
/// Historical quality intelligence computed from persisted results. Windows are UTC and half-open: the analysed window
/// is the last <c>days</c> whole UTC days up to and including today, compared with the equally long window before it.
/// </summary>
[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/insights")]
public sealed class InsightsController(CQPulseDbContext dbContext) : ControllerBase
{
    /// <summary>
    /// Top recurring failure signatures, grouped in SQL by the signature stored on every failed result, so every count is
    /// exact however many distinct raw messages there are. Failures still waiting for the signature backfill are reported
    /// separately (<see cref="FailureSignatureReportDto.UnsignedFailures"/>), never silently left out.
    /// </summary>
    [HttpGet("failure-signatures")]
    public async Task<FailureSignatureReportDto> FailureSignatures(Guid? projectId, int days = 30, int top = 20, CancellationToken cancellationToken = default)
    {
        var window = Window(days);
        var current = Results(projectId, window.FromUtc, window.ToUtc).Where(x => x.Outcome == TestOutcome.Failed);
        var previous = Results(projectId, window.PreviousFromUtc, window.FromUtc).Where(x => x.Outcome == TestOutcome.Failed);

        // Every query reads the (Outcome, CompletedAtUtc) index, which includes the stored signature. Each aggregate has at
        // most one DISTINCT: SQL Server evaluates two distinct aggregates in one GROUP BY through a spool, about 5x slower.
        var totals = await current.GroupBy(_ => 1)
            .Select(g => new { Failures = g.Count(), Unsigned = g.Count(x => x.Signature == null) })
            .SingleOrDefaultAsync(cancellationToken);
        var previousUnsigned = await previous.CountAsync(x => x.Signature == null, cancellationToken);
        var signed = current.Where(x => x.Signature != null);
        var distinctSignatures = await signed.Select(x => x.Signature).Distinct().CountAsync(cancellationToken);
        var groups = await signed
            .GroupBy(x => x.Signature!)
            .Select(g => new
            {
                Signature = g.Key,
                Occurrences = g.Count(),
                Runs = g.Select(x => x.TestRunId).Distinct().Count(),
                FirstSeen = g.Min(x => x.CompletedAtUtc),
                LastSeen = g.Max(x => x.CompletedAtUtc)
            })
            .OrderByDescending(x => x.Occurrences).ThenByDescending(x => x.LastSeen).ThenBy(x => x.Signature)
            .Take(Math.Clamp(top, 1, 100))
            .ToListAsync(cancellationToken);

        var keys = groups.Select(x => x.Signature).ToList();
        var testCases = await signed.Where(x => keys.Contains(x.Signature!) && x.TestCaseId != null)
            .GroupBy(x => x.Signature!)
            .Select(g => new { Signature = g.Key, Count = g.Select(x => x.TestCaseId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Signature, x => x.Count, cancellationToken);
        // The sample is a failure at the signature's last-seen time: an index seek, not a sort of the whole window.
        var lastSeenBySignature = groups.ToDictionary(x => x.Signature, x => x.LastSeen);
        var lastSeen = lastSeenBySignature.Values.Distinct().ToList();
        var samples = (await signed.Where(x => keys.Contains(x.Signature!) && lastSeen.Contains(x.CompletedAtUtc))
                .Select(x => new { Signature = x.Signature!, x.CompletedAtUtc, x.FailureCategory, x.ErrorMessage })
                .ToListAsync(cancellationToken))
            .Where(x => x.CompletedAtUtc == lastSeenBySignature[x.Signature])
            .GroupBy(x => x.Signature)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.ErrorMessage, StringComparer.Ordinal).First());
        var previousCounts = await previous.Where(x => keys.Contains(x.Signature!))
            .GroupBy(x => x.Signature!)
            .Select(g => new { Signature = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Signature, x => x.Count, cancellationToken);

        var signatures = groups.Select(g =>
        {
            var sample = samples[g.Signature];
            return new FailureSignatureDto(
                g.Signature, sample.FailureCategory, FailureSignature.Normalize(sample.ErrorMessage), sample.ErrorMessage ?? "(no message)",
                g.Occurrences, previousCounts.GetValueOrDefault(g.Signature), g.Runs, testCases.GetValueOrDefault(g.Signature), g.FirstSeen, g.LastSeen);
        }).ToList();
        var unsigned = totals?.Unsigned ?? 0;
        return new FailureSignatureReportDto(window, totals?.Failures ?? 0, signatures, distinctSignatures,
            unsigned, previousUnsigned, unsigned == 0 && previousUnsigned == 0);
    }

    [HttpGet("flaky-tests")]
    public async Task<FlakyReportDto> FlakyTests(Guid? projectId, int days = 30, int minExecutions = 5, CancellationToken cancellationToken = default)
    {
        var window = Window(days);
        minExecutions = Math.Clamp(minExecutions, 2, 1_000);
        var perCase = await Results(projectId, window.FromUtc, window.ToUtc)
            .Where(x => x.TestCaseId != null)
            .GroupBy(x => x.TestCaseId!.Value)
            .Select(g => new
            {
                TestCaseId = g.Key,
                Executions = g.Count(),
                Passed = g.Count(x => x.Outcome == TestOutcome.Passed),
                Failed = g.Count(x => x.Outcome == TestOutcome.Failed),
                Skipped = g.Count(x => x.Outcome == TestOutcome.Skipped)
            })
            .ToListAsync(cancellationToken);
        var mixed = perCase.Where(x => x.Executions >= minExecutions && x.Passed > 0 && x.Failed > 0)
            .OrderByDescending(x => Math.Min(x.Passed, x.Failed)).ThenByDescending(x => x.Executions)
            .Take(50).ToList();

        var ids = mixed.Select(x => x.TestCaseId).ToList();
        var cases = await dbContext.TestCases.AsNoTracking().Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.TestSuiteId, SuiteName = x.TestSuite.Name, x.SimulatedOutcome })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var sequences = (await Results(projectId, window.FromUtc, window.ToUtc)
                .Where(x => x.TestCaseId != null && ids.Contains(x.TestCaseId.Value))
                .OrderBy(x => x.CompletedAtUtc).ThenBy(x => x.Id)
                .Select(x => new { TestCaseId = x.TestCaseId!.Value, x.Outcome })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.TestCaseId)
            .ToDictionary(g => g.Key, g => InsightCalculator.CountOutcomeChanges(g.Select(x => x.Outcome)));

        var candidates = mixed.Where(x => cases.ContainsKey(x.TestCaseId)).Select(x =>
        {
            var testCase = cases[x.TestCaseId];
            return new FlakyCandidateDto(
                x.TestCaseId, testCase.Name, testCase.TestSuiteId, testCase.SuiteName, testCase.SimulatedOutcome,
                x.Executions, x.Passed, x.Failed, x.Skipped, Math.Round((double)x.Failed / x.Executions, 4),
                sequences.GetValueOrDefault(x.TestCaseId), testCase.SimulatedOutcome == SimulatedOutcome.Random);
        }).ToList();
        return new FlakyReportDto(window, minExecutions, perCase.Count, candidates);
    }

    [HttpGet("anomalies")]
    public async Task<AnomalyReportDto> Anomalies(
        Guid? projectId, int days = 30, int baselineDays = 14, int minimumBaselineDays = 5, int minimumDailyResults = 20,
        double threshold = 3, CancellationToken cancellationToken = default)
    {
        var window = Window(days);
        baselineDays = Math.Clamp(baselineDays, 3, 90);
        minimumBaselineDays = Math.Clamp(minimumBaselineDays, 2, baselineDays);
        if (threshold is < 1 or > 10) throw ValidationException.For(nameof(threshold), "Threshold must be between 1 and 10 standard deviations.");

        var historyFrom = window.FromUtc.AddDays(-baselineDays);
        var daily = await Results(projectId, historyFrom, window.ToUtc)
            .GroupBy(x => x.CompletedAtUtc.Date)
            .Select(g => new { Date = g.Key, Results = g.Count(), Failed = g.Count(x => x.Outcome == TestOutcome.Failed) })
            .ToListAsync(cancellationToken);
        var history = daily.Select(x => new InsightCalculator.DailyCount(DateOnly.FromDateTime(x.Date), x.Results, x.Failed)).ToList();
        var windowDays = Enumerable.Range(0, (int)(window.ToUtc - window.FromUtc).TotalDays)
            .Select(i => DateOnly.FromDateTime(window.FromUtc.UtcDateTime).AddDays(i)).ToList();

        var assessed = InsightCalculator.DetectAnomalies(history, windowDays, baselineDays, minimumBaselineDays, minimumDailyResults, threshold);
        return new AnomalyReportDto(window, baselineDays, minimumBaselineDays, minimumDailyResults, threshold, InsightCalculator.AnomalyMethod, assessed);
    }

    [HttpGet("quality-trends")]
    public async Task<QualityTrendReportDto> QualityTrends(int days = 7, CancellationToken cancellationToken = default)
    {
        var window = Window(days);
        var rows = await dbContext.TestCaseResults.AsNoTracking()
            .Where(x => x.CompletedAtUtc >= window.PreviousFromUtc && x.CompletedAtUtc < window.ToUtc)
            .GroupBy(x => new { x.TestRun.ProjectId, Current = x.CompletedAtUtc >= window.FromUtc })
            .Select(g => new { g.Key.ProjectId, g.Key.Current, Results = g.Count(), Failed = g.Count(x => x.Outcome == TestOutcome.Failed) })
            .ToListAsync(cancellationToken);
        var names = await dbContext.Projects.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var trends = rows.GroupBy(x => x.ProjectId).Select(g =>
        {
            var current = g.FirstOrDefault(x => x.Current);
            var previous = g.FirstOrDefault(x => !x.Current);
            var rate = current is null || current.Results == 0 ? 0 : (double)current.Failed / current.Results;
            double? previousRate = previous is null || previous.Results == 0 ? null : (double)previous.Failed / previous.Results;
            return new QualityTrendDto(g.Key, names.GetValueOrDefault(g.Key, "(deleted project)"),
                current?.Results ?? 0, current?.Failed ?? 0, Math.Round(rate, 4),
                previous?.Results ?? 0, previous?.Failed ?? 0, previousRate is null ? null : Math.Round(previousRate.Value, 4),
                current is null ? null : InsightCalculator.ChangeInPercentagePoints(rate, previousRate));
        })
        .Where(x => x.Results > 0)
        .OrderByDescending(x => x.ChangePercentagePoints ?? double.MinValue)
        .ToList();

        return new QualityTrendReportDto(window, trends,
            "Not offered: CQ Pulse shows observed history and explainable baselines only. Forecasts would need an evaluable model and more history than a simulator workspace provides.");
    }

    private IQueryable<TestCaseResult> Results(Guid? projectId, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        var query = dbContext.TestCaseResults.AsNoTracking().Where(x => x.CompletedAtUtc >= fromUtc && x.CompletedAtUtc < toUtc);
        return projectId is null ? query : query.Where(x => x.TestRun.ProjectId == projectId);
    }

    private static InsightWindow Window(int days) => InsightWindows.LastDays(days, DateTimeOffset.UtcNow);
}
