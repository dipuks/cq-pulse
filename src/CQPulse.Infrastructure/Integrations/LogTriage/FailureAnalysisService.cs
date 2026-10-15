using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using CQPulse.Application.Analysis;
using CQPulse.Application.Common;
using CQPulse.Application.Simulation;
using CQPulse.Domain;
using CQPulse.Domain.Entities;
using CQPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CQPulse.Infrastructure.Integrations.LogTriage;

/// <summary>
/// Bounded in-process queue of analysis ids. Writers never wait: when the queue is full the analysis simply stays Pending
/// in the database (its durable state) and <see cref="AnalysisWorker"/>'s sweep queues it once there is room. So a slow or
/// unavailable LogTriageAssistant can block neither the simulation worker, nor an HTTP request, nor shutdown, and no
/// request is lost: Pending analyses survive restarts and are picked up again at startup.
/// </summary>
internal sealed class AnalysisQueue
{
    private readonly Channel<Guid> _channel;
    private readonly ConcurrentDictionary<Guid, byte> _tracked = new(); // in the channel or being processed

    public AnalysisQueue(IOptions<LogTriageOptions> options)
    {
        Capacity = options.Value.QueueCapacity;
        _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait, // only TryWrite is used, so this just reports "full"
            SingleReader = true
        });
    }

    public int Capacity { get; }
    public int Count => _channel.Reader.Count;

    /// <summary>Queues the analysis unless it is already queued or running. False when the queue is full.</summary>
    public bool TryQueue(Guid analysisId)
    {
        if (!_tracked.TryAdd(analysisId, 0)) return true;
        if (_channel.Writer.TryWrite(analysisId)) return true;
        _tracked.TryRemove(analysisId, out _);
        return false;
    }

    public bool IsQueuedOrRunning(Guid analysisId) => _tracked.ContainsKey(analysisId);

    /// <summary>Called by the worker when it has finished with a dequeued analysis.</summary>
    public void Release(Guid analysisId) => _tracked.TryRemove(analysisId, out _);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}

internal sealed class FailureAnalysisService(
    CQPulseDbContext dbContext,
    AnalysisQueue queue,
    ICorrelationContext correlation,
    IOptions<LogTriageOptions> options,
    ILogger<FailureAnalysisService> logger) : IFailureAnalysisService, IRunCompletionObserver
{
    public const string ProviderName = "LogTriageAssistant";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<FailureAnalysisDto> RequestAsync(Guid runId, string? userId, CancellationToken cancellationToken)
    {
        var run = await dbContext.TestRuns.AsNoTracking().Where(x => x.Id == runId)
            .Select(x => new { x.Status, x.FailedCount }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("The requested run does not exist.");
        if (!run.Status.IsTerminal())
            throw new ConflictException("The run is still active; analyse it after it finishes.");

        var document = await AnalysisDocumentBuilder.BuildAsync(dbContext, runId, options.Value, cancellationToken);
        if (document.FailureCount == 0)
            throw new ConflictException("The run has no failed results to analyse.");

        var existing = await dbContext.FailureAnalyses.AsNoTracking()
            .Where(x => x.TestRunId == runId && x.InputDigest == document.Digest &&
                        (x.Status == AnalysisStatus.Pending || x.Status == AnalysisStatus.Running || x.Status == AnalysisStatus.Succeeded))
            .OrderByDescending(x => x.RequestedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (existing is not null) return ToDto(existing, queue);

        var analysis = new FailureAnalysis
        {
            TestRunId = runId,
            Provider = ProviderName,
            InputDigest = document.Digest,
            InputFailureCount = document.FailureCount,
            InputTruncated = document.Truncated,
            CorrelationId = correlation.CorrelationId,
            RequestedByUserId = userId
        };
        if (!options.Value.IsConfigured)
        {
            // Recorded, visible and retryable once the integration is configured; nothing is sent anywhere.
            analysis.Fail(DateTimeOffset.UtcNow, AnalysisStatus.Unavailable, "LogTriageAssistant is not configured (Integrations:LogTriage).");
        }
        dbContext.FailureAnalyses.Add(analysis);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Failure analysis {AnalysisId} for run {RunId} recorded as {Status} ({Failures} failures).",
            analysis.Id, runId, analysis.Status, document.FailureCount);
        if (analysis.Status == AnalysisStatus.Pending) Enqueue(analysis);
        return ToDto(analysis, queue);
    }

    public async Task<FailureAnalysisDto> RetryAsync(Guid analysisId, CancellationToken cancellationToken)
    {
        var analysis = await dbContext.FailureAnalyses.SingleOrDefaultAsync(x => x.Id == analysisId, cancellationToken)
            ?? throw new NotFoundException("The requested analysis does not exist.");
        if (analysis.Status is AnalysisStatus.Pending or AnalysisStatus.Running)
            throw new ConflictException("The analysis is already queued or running.");

        var document = await AnalysisDocumentBuilder.BuildAsync(dbContext, analysis.TestRunId, options.Value, cancellationToken);
        analysis.Retry(correlation.CorrelationId);
        analysis.InputDigest = document.Digest;
        analysis.InputFailureCount = document.FailureCount;
        analysis.InputTruncated = document.Truncated;
        if (!options.Value.IsConfigured)
            analysis.Fail(DateTimeOffset.UtcNow, AnalysisStatus.Unavailable, "LogTriageAssistant is not configured (Integrations:LogTriage).");
        await dbContext.SaveChangesAsync(cancellationToken);
        if (analysis.Status == AnalysisStatus.Pending) Enqueue(analysis);
        return ToDto(analysis, queue);
    }

    /// <summary>Never waits: a full queue leaves the analysis Pending, and the worker's sweep queues it later.</summary>
    private void Enqueue(FailureAnalysis analysis)
    {
        if (queue.TryQueue(analysis.Id)) return;
        logger.LogWarning("Analysis queue is full ({Capacity}); analysis {AnalysisId} for run {RunId} stays Pending and is queued automatically when there is room.",
            queue.Capacity, analysis.Id, analysis.TestRunId);
    }

    public async Task<IReadOnlyList<FailureAnalysisDto>> GetForRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var analyses = await dbContext.FailureAnalyses.AsNoTracking().Where(x => x.TestRunId == runId)
            .OrderByDescending(x => x.RequestedAtUtc).ToListAsync(cancellationToken);
        return analyses.Select(x => ToDto(x, queue)).ToList();
    }

    public async Task<FailureAnalysisDto?> GetAsync(Guid analysisId, CancellationToken cancellationToken) =>
        await dbContext.FailureAnalyses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == analysisId, cancellationToken) is { } analysis
            ? ToDto(analysis, queue)
            : null;

    /// <summary>Auto-trigger: only when configured and enabled, and only for runs with failures.</summary>
    public async Task OnRunCompletedAsync(Guid runId, RunStatus status, int failedCount, CancellationToken cancellationToken)
    {
        if (failedCount == 0 || !options.Value.IsConfigured || !options.Value.AutoAnalyzeFailedRuns) return;
        await RequestAsync(runId, userId: null, cancellationToken);
    }

    internal static FailureAnalysisDto ToDto(FailureAnalysis x, AnalysisQueue queue) => new(
        x.Id, x.TestRunId, x.Provider, x.Status, x.RequestedAtUtc, x.StartedAtUtc, x.CompletedAtUtc, x.Attempts, x.LastError,
        x.InputFailureCount, x.InputTruncated, x.CorrelationId, x.ExternalAnalysisId, x.Severity, x.Summary, x.SignalScore, x.TopCategory,
        Deserialize<List<AnalysisFindingDto>>(x.FindingsJson) ?? [], Deserialize<List<string>>(x.RecommendationsJson) ?? [],
        x.Status == AnalysisStatus.Pending && !queue.IsQueuedOrRunning(x.Id));

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Json); } catch (JsonException) { return null; }
    }
}
