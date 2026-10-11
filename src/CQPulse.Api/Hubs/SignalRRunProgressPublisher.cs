using CQPulse.Application.Simulation;
using Microsoft.AspNetCore.SignalR;

namespace CQPulse.Api.Hubs;

public sealed class SignalRRunProgressPublisher(IHubContext<RunsHub> hubContext) : IRunProgressPublisher
{
    public Task PublishAsync(RunProgress progress, CancellationToken cancellationToken) =>
        hubContext.Clients.Group(progress.RunId.ToString())
            .SendAsync("RunProgressUpdated", progress, cancellationToken);
}
