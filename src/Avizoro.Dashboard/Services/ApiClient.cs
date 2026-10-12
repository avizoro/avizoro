using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avizoro.Server.Contracts;

namespace Avizoro.Dashboard.Services;

/// <summary>
/// Thin typed wrapper over the Avizoro server API. Every call returns
/// the shared contract types so the pages stay free of serialization concerns.
/// </summary>
public class ApiClient(HttpClient http)
{
    /// <summary>API root (ends with '/'), for building direct asset URLs like screenshots.</summary>
    public Uri? BaseAddress => http.BaseAddress;

    // The server emits enums (e.g. breadcrumb SeverityLevel) as strings, so the
    // client must accept strings too — the web defaults don't do this on their own.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<ProjectSummary>> GetProjectsAsync(CancellationToken ct = default)
        => await http.GetFromJsonAsync<IReadOnlyList<ProjectSummary>>("/projects", JsonOptions, ct)
           ?? Array.Empty<ProjectSummary>();

    public async Task<PagedResult<EventSummary>> GetEventsAsync(
        EventFilter filter, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var url = $"/events?skip={skip}&take={take}";
        var query = filter.ToQueryString();
        if (query.Length > 0)
            url += "&" + query;

        return await http.GetFromJsonAsync<PagedResult<EventSummary>>(url, JsonOptions, ct)
               ?? new PagedResult<EventSummary>(0, skip, take, Array.Empty<EventSummary>());
    }

    public async Task<IReadOnlyList<TimeBucket>> GetEventsPerHourAsync(
        string? projectId = null, int hours = 24, CancellationToken ct = default)
    {
        var url = $"/stats/events-per-hour?hours={hours}";
        if (!string.IsNullOrWhiteSpace(projectId))
            url += $"&projectId={Uri.EscapeDataString(projectId)}";

        return await http.GetFromJsonAsync<IReadOnlyList<TimeBucket>>(url, JsonOptions, ct)
               ?? Array.Empty<TimeBucket>();
    }

    public async Task<EventDetail?> GetEventAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/events/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EventDetail>(JsonOptions, ct);
    }

    public async Task<PagedResult<IssueSummary>> GetIssuesAsync(
        string? projectId = null, int skip = 0, int take = 50, string? status = null,
        string? release = null, string? q = null, CancellationToken ct = default)
    {
        var url = $"/issues?skip={skip}&take={take}";
        if (!string.IsNullOrWhiteSpace(q))
            url += $"&q={Uri.EscapeDataString(q)}";
        if (!string.IsNullOrWhiteSpace(projectId))
            url += $"&projectId={Uri.EscapeDataString(projectId)}";
        if (!string.IsNullOrWhiteSpace(status))
            url += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrWhiteSpace(release))
            url += $"&release={Uri.EscapeDataString(release)}";

        return await http.GetFromJsonAsync<PagedResult<IssueSummary>>(url, JsonOptions, ct)
               ?? new PagedResult<IssueSummary>(0, skip, take, Array.Empty<IssueSummary>());
    }

    public async Task<IssueSummary?> GetIssueAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/issues/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IssueSummary>(JsonOptions, ct);
    }

    /// <summary>Triage an issue (status / assignee). Returns the updated issue.</summary>
    public async Task<IssueSummary?> UpdateIssueAsync(Guid id, UpdateIssueRequest request, CancellationToken ct = default)
    {
        var response = await http.PatchAsJsonAsync($"/issues/{id}", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IssueSummary>(JsonOptions, ct);
    }

    public async Task<PagedResult<EventSummary>> GetIssueEventsAsync(
        Guid id, int skip = 0, int take = 50, CancellationToken ct = default)
        => await http.GetFromJsonAsync<PagedResult<EventSummary>>(
               $"/issues/{id}/events?skip={skip}&take={take}", JsonOptions, ct)
           ?? new PagedResult<EventSummary>(0, skip, take, Array.Empty<EventSummary>());

    public async Task<CrashFreeStats?> GetCrashFreeAsync(
        string? projectId = null, string? environment = null, string? release = null, int days = 14, CancellationToken ct = default)
    {
        var url = $"/stats/crash-free?days={days}";
        if (!string.IsNullOrWhiteSpace(projectId))
            url += $"&projectId={Uri.EscapeDataString(projectId)}";
        if (!string.IsNullOrWhiteSpace(environment))
            url += $"&environment={Uri.EscapeDataString(environment)}";
        if (!string.IsNullOrWhiteSpace(release))
            url += $"&release={Uri.EscapeDataString(release)}";
        return await http.GetFromJsonAsync<CrashFreeStats>(url, JsonOptions, ct);
    }

    public async Task<IReadOnlyList<ReleaseHealth>> GetReleasesAsync(
        string? projectId = null, string? environment = null, int days = 30, CancellationToken ct = default)
    {
        var url = $"/releases?days={days}";
        if (!string.IsNullOrWhiteSpace(projectId))
            url += $"&projectId={Uri.EscapeDataString(projectId)}";
        if (!string.IsNullOrWhiteSpace(environment))
            url += $"&environment={Uri.EscapeDataString(environment)}";
        return await http.GetFromJsonAsync<IReadOnlyList<ReleaseHealth>>(url, JsonOptions, ct)
               ?? Array.Empty<ReleaseHealth>();
    }

    public async Task<IReadOnlyList<PerformanceSummary>> GetPerformanceAsync(
        string? projectId = null, string? operation = null, string? release = null, int days = 7, CancellationToken ct = default)
    {
        var url = $"/performance?days={days}";
        if (!string.IsNullOrWhiteSpace(projectId))
            url += $"&projectId={Uri.EscapeDataString(projectId)}";
        if (!string.IsNullOrWhiteSpace(operation))
            url += $"&operation={Uri.EscapeDataString(operation)}";
        if (!string.IsNullOrWhiteSpace(release))
            url += $"&release={Uri.EscapeDataString(release)}";
        return await http.GetFromJsonAsync<IReadOnlyList<PerformanceSummary>>(url, JsonOptions, ct)
               ?? Array.Empty<PerformanceSummary>();
    }

    public async Task<IReadOnlyList<AffectedUser>> GetIssueUsersAsync(Guid issueId, CancellationToken ct = default)
        => await http.GetFromJsonAsync<IReadOnlyList<AffectedUser>>($"/issues/{issueId}/users?take=20", JsonOptions, ct)
           ?? Array.Empty<AffectedUser>();

    public async Task<UserTimeline?> GetUserTimelineAsync(string userId, string? projectId = null, CancellationToken ct = default)
    {
        var url = $"/users/{Uri.EscapeDataString(userId)}";
        if (!string.IsNullOrWhiteSpace(projectId))
            url += $"?projectId={Uri.EscapeDataString(projectId)}";
        return await http.GetFromJsonAsync<UserTimeline>(url, JsonOptions, ct);
    }

    public async Task<IssueDistributions?> GetIssueDistributionsAsync(Guid issueId, CancellationToken ct = default)
        => await http.GetFromJsonAsync<IssueDistributions>($"/issues/{issueId}/distributions", JsonOptions, ct);

    public async Task<IReadOnlyList<FeedbackInfo>> GetIssueFeedbackAsync(Guid issueId, CancellationToken ct = default)
        => await http.GetFromJsonAsync<IReadOnlyList<FeedbackInfo>>($"/issues/{issueId}/feedback", JsonOptions, ct)
           ?? Array.Empty<FeedbackInfo>();

    public async Task<IReadOnlyList<FeedbackInfo>> GetEventFeedbackAsync(Guid eventId, CancellationToken ct = default)
        => await http.GetFromJsonAsync<IReadOnlyList<FeedbackInfo>>($"/events/{eventId}/feedback", JsonOptions, ct)
           ?? Array.Empty<FeedbackInfo>();

    // Project administration (admin-only) --------------------------------

    public async Task<IReadOnlyList<ProjectInfo>> GetRegisteredProjectsAsync(CancellationToken ct = default)
        => await http.GetFromJsonAsync<IReadOnlyList<ProjectInfo>>("/admin/projects", JsonOptions, ct)
           ?? Array.Empty<ProjectInfo>();

    /// <summary>Registers a project; returns its one-time credentials, or the server's error (400/409).</summary>
    public async Task<(ProjectCredentials? Credentials, string? Error)> CreateProjectAsync(CreateProjectRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("/admin/projects", request, JsonOptions, ct);
        if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Conflict)
            return (null, (await response.Content.ReadAsStringAsync(ct)).Trim('"'));

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectCredentials>(JsonOptions, ct), null);
    }

    public async Task<ProjectCredentials?> RotateProjectKeyAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"/admin/projects/{id}/rotate-key", null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProjectCredentials>(JsonOptions, ct);
    }

    public async Task DeleteProjectAsync(Guid id, CancellationToken ct = default)
        => (await http.DeleteAsync($"/admin/projects/{id}", ct)).EnsureSuccessStatusCode();

    public async Task<IntegrationsInfo?> GetIntegrationsAsync(CancellationToken ct = default)
        => await http.GetFromJsonAsync<IntegrationsInfo>("/integrations", JsonOptions, ct);

    /// <summary>Creates (or returns the existing) GitHub/Jira issue. Returns the server's error on failure.</summary>
    public async Task<(ExternalIssueResult? Result, string? Error)> CreateExternalIssueAsync(Guid issueId, string provider, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"/issues/{issueId}/external/{provider}", null, ct);
        if (!response.IsSuccessStatusCode)
            return (null, (await response.Content.ReadAsStringAsync(ct)).Trim('"'));
        return (await response.Content.ReadFromJsonAsync<ExternalIssueResult>(JsonOptions, ct), null);
    }

    // Alerts (admin-only) ---------------------------------------------------

    public async Task<IReadOnlyList<AlertRuleInfo>> GetAlertRulesAsync(CancellationToken ct = default)
        => await http.GetFromJsonAsync<IReadOnlyList<AlertRuleInfo>>("/alerts/rules", JsonOptions, ct)
           ?? Array.Empty<AlertRuleInfo>();

    /// <summary>Creates (id null) or replaces a rule. Returns the server's error text on 400.</summary>
    public async Task<(AlertRuleInfo? Rule, string? Error)> SaveAlertRuleAsync(
        Guid? id, UpsertAlertRuleRequest request, CancellationToken ct = default)
    {
        var response = id is null
            ? await http.PostAsJsonAsync("/alerts/rules", request, JsonOptions, ct)
            : await http.PutAsJsonAsync($"/alerts/rules/{id}", request, JsonOptions, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            return (null, (await response.Content.ReadAsStringAsync(ct)).Trim('"'));

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AlertRuleInfo>(JsonOptions, ct), null);
    }

    public async Task DeleteAlertRuleAsync(Guid id, CancellationToken ct = default)
        => (await http.DeleteAsync($"/alerts/rules/{id}", ct)).EnsureSuccessStatusCode();

    /// <summary>Fires a synthetic alert through the rule. Returns null on success, else the error.</summary>
    public async Task<string?> TestAlertRuleAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"/alerts/rules/{id}/test", null, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
        return result.GetProperty("success").GetBoolean() ? null : result.GetProperty("error").GetString();
    }

    public async Task<PagedResult<AlertNotificationInfo>> GetAlertHistoryAsync(
        string? projectId = null, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var url = $"/alerts/history?skip={skip}&take={take}";
        if (!string.IsNullOrWhiteSpace(projectId))
            url += $"&projectId={Uri.EscapeDataString(projectId)}";
        return await http.GetFromJsonAsync<PagedResult<AlertNotificationInfo>>(url, JsonOptions, ct)
               ?? new PagedResult<AlertNotificationInfo>(0, skip, take, Array.Empty<AlertNotificationInfo>());
    }

    public async Task<PagedResult<SessionSummary>> GetSessionsAsync(
        string? projectId = null, string? status = null, bool withTimeline = false,
        int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var url = $"/sessions?skip={skip}&take={take}";
        if (!string.IsNullOrWhiteSpace(projectId))
            url += $"&projectId={Uri.EscapeDataString(projectId)}";
        if (!string.IsNullOrWhiteSpace(status))
            url += $"&status={Uri.EscapeDataString(status)}";
        if (withTimeline)
            url += "&withTimeline=true";

        return await http.GetFromJsonAsync<PagedResult<SessionSummary>>(url, JsonOptions, ct)
               ?? new PagedResult<SessionSummary>(0, skip, take, Array.Empty<SessionSummary>());
    }

    public async Task<SessionTimelineResult?> GetSessionTimelineAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/sessions/{id}/timeline", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SessionTimelineResult>(JsonOptions, ct);
    }
}
