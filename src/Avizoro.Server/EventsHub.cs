using Avizoro.Server.Auth;
using Microsoft.AspNetCore.SignalR;

namespace Avizoro.Server;

/// <summary>
/// Server-to-client push channel for live dashboards. The server broadcasts an
/// "eventReceived"/"issueUpserted" message each time an event is ingested;
/// clients don't call anything on the hub.
///
/// Each connection joins a group based on its access scope, so a project-scoped
/// dashboard only receives its own project's pushes: admins join <c>"admins"</c>,
/// a scoped caller joins <c>"project:{projectId}"</c>. The ingest path sends to
/// both groups.
/// </summary>
public class EventsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var scope = Context.User?.ProjectScope();
        var group = string.IsNullOrEmpty(scope) ? "admins" : $"project:{scope}";
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        await base.OnConnectedAsync();
    }
}
