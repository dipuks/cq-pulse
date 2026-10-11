using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CQPulse.Api.Hubs;

[Authorize(Roles = "Administrator")]
public sealed class RunsHub : Hub
{
    public Task JoinRun(Guid runId) => Groups.AddToGroupAsync(Context.ConnectionId, runId.ToString());
    public Task LeaveRun(Guid runId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, runId.ToString());
}
