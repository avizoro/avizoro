using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Server.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Avizoro.Server.Sessions;

/// <summary>
/// Applies an SDK session update (an <see cref="EventType.Session"/> event) to the
/// <see cref="AppSession"/> table. Updates are applied in sequence order; a stale
/// or duplicate update is ignored, and <c>Crashed</c> is terminal.
/// </summary>
public sealed class SessionIngestor
{
    private readonly AvizoroDbContext _db;
    private readonly IHubContext<EventsHub> _hub;
    private readonly ILogger<SessionIngestor> _logger;

    public SessionIngestor(AvizoroDbContext db, IHubContext<EventsHub> hub, ILogger<SessionIngestor> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    public async Task IngestAsync(AvizoroEvent e, CancellationToken ct = default)
    {
        var update = e.Session!;
        var status = update.Status == SessionStatus.Abnormal ? SessionStatus.Exited : update.Status;
        var now = DateTime.UtcNow;

        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == update.SessionId, ct);
        if (session is null)
        {
            session = new AppSession
            {
                Id = update.SessionId,
                ProjectId = e.ProjectId,
                Started = update.Started == default ? now : update.Started,
            };
            _db.Sessions.Add(session);
        }
        else if (update.Sequence <= session.Sequence || session.Status == nameof(SessionStatus.Crashed))
        {
            return;
        }

        session.Release = e.Release;
        session.Environment = e.Environment;
        session.Platform = e.Platform;
        session.DeviceModel = Truncate(e.DeviceModel);
        session.Manufacturer = Truncate(e.Manufacturer);
        session.OperatingSystem = Truncate(e.OperatingSystem);
        session.DistinctId = update.DistinctId;
        session.UserId = e.UserId;
        session.Status = status.ToString();
        session.Errors = Math.Max(session.Errors, update.Errors);
        session.DurationSeconds = update.DurationSeconds;
        session.Sequence = update.Sequence;
        session.LastUpdate = now;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two first updates of the same session raced on insert; the other one
            // won. Session updates are retried by the SDK only on failure, and the
            // next lifecycle update will carry the newer state anyway.
            return;
        }

        // Live Sessions page. Best-effort: the update is already stored.
        try
        {
            await _hub.Clients.Groups("admins", $"project:{session.ProjectId}").SendAsync("sessionUpdated", session.ToSummary(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to push session {SessionId}.", session.Id);
        }
    }

    private static string Truncate(string? value)
        => string.IsNullOrEmpty(value) ? string.Empty : value.Length <= 200 ? value : value[..200];
}
