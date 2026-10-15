using System.Text.Json;
using CQPulse.Application.Analysis;
using CQPulse.Application.Decisions;
using CQPulse.Domain;
using CQPulse.Domain.Entities;
using CQPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CQPulse.Infrastructure.Integrations.LogTriage;

/// <summary>
/// Processes queued failure analyses one at a time. Only the <see cref="FailureAnalysis"/> record is written; runs and
/// results are read-only here. On startup, analyses interrupted mid-flight are marked Unavailable (retryable). Pending
/// analyses that are not in the queue (it was full, the application restarted, or a request was cancelled after saving)
/// are queued oldest first at startup and then every <see cref="LogTriageOptions.PendingSweepSeconds"/>, without waiting
/// for room, so the queue size bounds memory but never loses or blocks a request.
/// </summary>
internal sealed class AnalysisWorker(
    AnalysisQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<LogTriageOptions> options,
    ILogger<AnalysisWorker> logger) : BackgroundService
{
    private const int MaxStoredFindings = 100;
    private const int MaxErrorLength = 2_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Task sweeper = Task.CompletedTask;
        try
        {
            await RecoverAsync(stoppingToken);
            sweeper = SweepPeriodicallyAsync(stoppingToken);
            await foreach (var analysisId in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessAsync(analysisId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Failure analysis {AnalysisId} crashed.", analysisId);
                    await TryFinishAsync(analysisId, AnalysisStatus.Failed, $"Unexpected error: {exception.GetBaseException().Message}");
                }
                finally
                {
                    queue.Release(analysisId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown. Queued analyses stay Pending in the database and are queued again at the next start.
        }
        finally
        {
            await sweeper;
        }
    }

    /// <summary>Queues Pending analyses that are not queued yet, oldest first, while there is room. Returns how many.</summary>
    internal async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var room = queue.Capacity - queue.Count;
        if (room <= 0) return 0;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CQPulseDbContext>();
        // At most Capacity + 1 pending analyses are already tracked (queued or running), so this always reaches `room` others.
        var pending = await db.FailureAnalyses.AsNoTracking().Where(x => x.Status == AnalysisStatus.Pending)
            .OrderBy(x => x.RequestedAtUtc).ThenBy(x => x.Id).Select(x => x.Id)
            .Take(queue.Capacity + 1 + room).ToListAsync(cancellationToken);
        var queued = 0;
        foreach (var id in pending.Where(id => !queue.IsQueuedOrRunning(id)))
        {
            if (!queue.TryQueue(id)) break;
            queued++;
        }
        return queued;
    }

    private async Task SweepPeriodicallyAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PendingSweepSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var queued = await SweepAsync(stoppingToken);
                    if (queued > 0) logger.LogInformation("Analysis sweep queued {Queued} pending analyses.", queued);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(exception, "Analysis sweep failed; it runs again in {Seconds} s.", options.Value.PendingSweepSeconds);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
    }

    private async Task ProcessAsync(Guid analysisId, CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CQPulseDbContext>();
        var analysis = await db.FailureAnalyses.SingleOrDefaultAsync(x => x.Id == analysisId, stoppingToken);
        if (analysis is null || analysis.Status != AnalysisStatus.Pending) return;

        using var _ = logger.BeginScope("AnalysisId:{AnalysisId} RunId:{RunId} CorrelationId:{CorrelationId}",
            analysis.Id, analysis.TestRunId, analysis.CorrelationId);
        analysis.Start(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(stoppingToken);

        var document = await AnalysisDocumentBuilder.BuildAsync(db, analysis.TestRunId, options.Value, stoppingToken);
        var client = scope.ServiceProvider.GetRequiredService<LogTriageClient>();
        try
        {
            var result = await client.AnalyzeAsync(document.Content, document.FileName, analysis.CorrelationId, stoppingToken);
            var findings = (result.Findings ?? []).Take(MaxStoredFindings).Select(f => new AnalysisFindingDto(
                f.LineNumber, f.EndLineNumber, f.Timestamp, f.Message ?? string.Empty, f.Severity ?? "INFO",
                f.Category ?? "General", f.Evidence ?? string.Empty, f.Suggestion ?? string.Empty)).ToList();
            analysis.Succeed(DateTimeOffset.UtcNow, new AnalysisOutcome(
                result.Id, result.Severity!, result.Summary!, result.Confidence, result.Statistics?.TopCategory,
                JsonSerializer.Serialize(findings, Json), JsonSerializer.Serialize(result.Recommendations ?? [], Json),
                JsonSerializer.Serialize(SeveritiesBySignature(result.Findings ?? [], document.LineSignatures), Json)));
            logger.LogInformation("Failure analysis {AnalysisId} succeeded: severity {Severity}, {Findings} findings.",
                analysis.Id, result.Severity, findings.Count);
        }
        catch (LogTriageUnavailableException exception)
        {
            logger.LogWarning(exception, "Failure analysis {AnalysisId}: LogTriageAssistant unavailable.", analysis.Id);
            analysis.Fail(DateTimeOffset.UtcNow, AnalysisStatus.Unavailable, Truncate(exception.Message));
        }
        catch (LogTriageContractException exception)
        {
            logger.LogWarning("Failure analysis {AnalysisId} rejected: {Reason}", analysis.Id, exception.Message);
            analysis.Fail(DateTimeOffset.UtcNow, AnalysisStatus.Failed, Truncate(exception.Message));
        }
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task RecoverAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CQPulseDbContext>();
        var interrupted = await db.FailureAnalyses.Where(x => x.Status == AnalysisStatus.Running).ToListAsync(stoppingToken);
        foreach (var analysis in interrupted)
            analysis.Fail(DateTimeOffset.UtcNow, AnalysisStatus.Unavailable, "Analysis was interrupted by an application restart; retry it.");
        await db.SaveChangesAsync(stoppingToken);

        var queued = await SweepAsync(stoppingToken);
        if (interrupted.Count + queued > 0)
            logger.LogInformation("Analysis recovery: {Interrupted} interrupted, {Queued} pending re-queued.", interrupted.Count, queued);
    }

    private async Task TryFinishAsync(Guid analysisId, AnalysisStatus status, string error)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CQPulseDbContext>();
            var analysis = await db.FailureAnalyses.SingleOrDefaultAsync(x => x.Id == analysisId);
            if (analysis is null || analysis.Status is not (AnalysisStatus.Pending or AnalysisStatus.Running)) return;
            analysis.Fail(DateTimeOffset.UtcNow, status, Truncate(error));
            await db.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not record failure of analysis {AnalysisId}.", analysisId);
        }
    }

    /// <summary>
    /// Highest severity per failure signature, from every returned finding (not only the stored ones). A finding covers
    /// lines LineNumber..EndLineNumber of the submitted document; findings outside it or with unknown labels are ignored.
    /// </summary>
    internal static Dictionary<string, string> SeveritiesBySignature(IEnumerable<LogTriageFinding> findings, IReadOnlyList<string> lineSignatures)
    {
        var highest = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var finding in findings)
        {
            if (SeverityScale.Rank(finding.Severity) is not { } rank) continue;
            var first = Math.Max(1, finding.LineNumber);
            var last = Math.Min(lineSignatures.Count, Math.Max(finding.LineNumber, finding.EndLineNumber));
            for (var line = first; line <= last; line++)
            {
                var signature = lineSignatures[line - 1];
                highest[signature] = Math.Max(highest.GetValueOrDefault(signature), rank);
            }
        }
        return highest.ToDictionary(x => x.Key, x => SeverityScale.Label(x.Value), StringComparer.Ordinal);
    }

    private static string Truncate(string text) => text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];
}
