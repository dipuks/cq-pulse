using System.Threading.Channels;
using CQPulse.Application.Simulation;
using Microsoft.Extensions.Options;

namespace CQPulse.Infrastructure.Simulator;

/// <summary>
/// In-process, bounded run queue. Not durable: queued run IDs are lost on restart, and
/// <see cref="ExecutionRecoveryService"/> marks those runs failed at the next startup.
/// </summary>
internal sealed class SimulationQueue(IOptions<SimulatorOptions> options)
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

    public ValueTask QueueAsync(Guid runId, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(runId, cancellationToken);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
