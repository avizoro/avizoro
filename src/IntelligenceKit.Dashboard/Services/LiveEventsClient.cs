using IntelligenceKit.Server.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;

namespace IntelligenceKit.Dashboard.Services;

/// <summary>
/// Maintains a single SignalR connection to the server's events hub and raises
/// <see cref="EventReceived"/> whenever a new event is ingested anywhere. Shared
/// (singleton) so navigating between pages doesn't churn connections.
/// </summary>
public sealed class LiveEventsClient : IAsyncDisposable
{
    private readonly string _hubUrl;
    private readonly AuthState _auth;
    private HubConnection? _connection;

    public LiveEventsClient(IConfiguration configuration, AuthState auth)
    {
        var baseUrl = configuration["ApiBaseUrl"] ?? "http://localhost:7099";
        _hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/events";
        _auth = auth;
    }

    /// <summary>Raised on the SignalR thread; handlers should marshal to the UI.</summary>
    public event Action<EventSummary>? EventReceived;

    /// <summary>Raised when an issue is created or its count changes.</summary>
    public event Action<IssueSummary>? IssueUpserted;

    /// <summary>Raised when a session starts or changes state (status, duration, timeline count).</summary>
    public event Action<SessionSummary>? SessionUpdated;

    /// <summary>Raised when new timeline entries of a session arrive (<c>EnableSessionTimeline</c>).</summary>
    public event Action<TimelinePush>? TimelineReceived;

    public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;

    /// <summary>Connects if not already connected. Safe to call repeatedly.</summary>
    public async Task EnsureStartedAsync()
    {
        if (_connection is not null)
            return;

        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl, options =>
            {
                // The hub requires the read token; SignalR sends it as the
                // access_token query parameter (WebSockets can't set headers).
                options.AccessTokenProvider = () => Task.FromResult(_auth.Token);
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<EventSummary>("eventReceived", summary => EventReceived?.Invoke(summary));
        _connection.On<IssueSummary>("issueUpserted", issue => IssueUpserted?.Invoke(issue));
        _connection.On<SessionSummary>("sessionUpdated", session => SessionUpdated?.Invoke(session));
        _connection.On<TimelinePush>("timelineReceived", push => TimelineReceived?.Invoke(push));

        await _connection.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
