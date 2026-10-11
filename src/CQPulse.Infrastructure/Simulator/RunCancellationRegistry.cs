using System.Collections.Concurrent;

namespace CQPulse.Infrastructure.Simulator;

internal sealed class RunCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _sources = new();

    public CancellationToken Register(Guid runId, CancellationToken stoppingToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        if (!_sources.TryAdd(runId, source))
        {
            source.Dispose();
            throw new InvalidOperationException("Run cancellation token was already registered.");
        }

        return source.Token;
    }

    public void Cancel(Guid runId)
    {
        if (_sources.TryGetValue(runId, out var source)) source.Cancel();
    }

    public void Remove(Guid runId)
    {
        if (_sources.TryRemove(runId, out var source)) source.Dispose();
    }
}
