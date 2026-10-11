using System.Threading.Channels;

namespace CQPulse.Infrastructure.Simulator;

internal sealed class SimulationQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

    public ValueTask QueueAsync(Guid runId, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(runId, cancellationToken);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
