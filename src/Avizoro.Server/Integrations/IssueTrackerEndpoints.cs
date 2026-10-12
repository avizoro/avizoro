using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Avizoro.Core.Models;
using Avizoro.Server.Auth;
using Avizoro.Server.Contracts;
using Avizoro.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avizoro.Server.Integrations;

/// <summary>
/// "Create a GitHub/Jira issue" from an Avizoro issue, prefilled with the
/// summary, releases and latest stack trace, and linked back. Configured on the
/// server (secrets never reach the dashboard):
/// <list type="bullet">
///   <item><c>Integrations:GitHub:Repository</c> ("owner/repo") + optional <c>Token</c>.
///   With a token the issue is created via the API; without one, a prefilled
///   "new issue" URL is returned for the browser to open.</item>
///   <item><c>Integrations:Jira:{BaseUrl,Email,ApiToken,ProjectKey,IssueType}</c>
///   (Jira Cloud REST v2).</item>
/// </list>
/// </summary>
public static class IssueTrackerEndpoints
{
    public const string HttpClientName = "integrations";

    public static void MapIssueTrackerEndpoints(this WebApplication app)
    {
        app.MapGet("/integrations", (IConfiguration config) => Results.Ok(new IntegrationsInfo(
            GitHub: !string.IsNullOrWhiteSpace(config["Integrations:GitHub:Repository"]),
            GitHubCreatesViaApi: !string.IsNullOrWhiteSpace(config["Integrations:GitHub:Token"]),
            Jira: !string.IsNullOrWhiteSpace(config["Integrations:Jira:BaseUrl"])))).RequireAuthorization();

        app.MapPost("/issues/{id:guid}/external/{provider}", async (Guid id, string provider, AvizoroDbContext db,
            ClaimsPrincipal user, IConfiguration config, IHttpClientFactory http, CancellationToken ct) =>
        {
            var issue = await db.Issues.FirstOrDefaultAsync(i => i.Id == id, ct);
            var scope = user.ProjectScope();
            if (issue is null || (scope is not null && issue.ProjectId != scope))
                return Results.NotFound();

            // Idempotent: one external issue per Avizoro issue.
            if (!string.IsNullOrEmpty(issue.ExternalIssueUrl))
                return Results.Ok(new ExternalIssueResult(issue.ExternalIssueUrl, Created: false, Prefilled: false));

            var (title, body) = await ComposeAsync(db, issue, config, ct);

            try
            {
                switch (provider.ToLowerInvariant())
                {
                    case "github":
                        var repo = config["Integrations:GitHub:Repository"];
                        if (string.IsNullOrWhiteSpace(repo))
                            return Results.BadRequest("GitHub is not configured (Integrations:GitHub:Repository).");

                        var token = config["Integrations:GitHub:Token"];
                        if (string.IsNullOrWhiteSpace(token))
                        {
                            // No token: let the user file it themselves, prefilled.
                            var prefilled = $"https://github.com/{repo}/issues/new?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(Truncate(body, 6000))}";
                            return Results.Ok(new ExternalIssueResult(prefilled, Created: false, Prefilled: true));
                        }

                        issue.ExternalIssueUrl = await CreateGitHubAsync(http, config, repo, token, title, body, ct);
                        break;

                    case "jira":
                        if (string.IsNullOrWhiteSpace(config["Integrations:Jira:BaseUrl"]))
                            return Results.BadRequest("Jira is not configured (Integrations:Jira:BaseUrl).");
                        issue.ExternalIssueUrl = await CreateJiraAsync(http, config, title, body, ct);
                        break;

                    default:
                        return Results.BadRequest("Provider must be 'github' or 'jira'.");
                }
            }
            catch (HttpRequestException ex)
            {
                return Results.Problem($"The {provider} API rejected the request: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new ExternalIssueResult(issue.ExternalIssueUrl!, Created: true, Prefilled: false));
        }).RequireAuthorization();
    }

    private static async Task<(string Title, string Body)> ComposeAsync(AvizoroDbContext db, Issue issue, IConfiguration config, CancellationToken ct)
    {
        var latest = await db.Events.AsNoTracking()
            .Where(e => e.Id == issue.LastEventId)
            .Select(e => new { e.ExceptionJson, e.ExceptionMessage, e.Message, e.Platform, e.OperatingSystem })
            .FirstOrDefaultAsync(ct);

        var title = string.IsNullOrWhiteSpace(issue.Culprit) ? issue.Title : $"{issue.Title} in {issue.Culprit}";

        var sb = new StringBuilder();
        sb.AppendLine($"**{issue.Title}** reported by Avizoro ({issue.ProjectId}).").AppendLine();
        var message = latest?.ExceptionMessage ?? latest?.Message;
        if (!string.IsNullOrWhiteSpace(message))
            sb.AppendLine($"> {message}").AppendLine();
        sb.AppendLine($"- Events: {issue.EventCount}");
        sb.AppendLine($"- First seen: {issue.FirstSeen:u}{(issue.FirstRelease is null ? "" : $" (release {issue.FirstRelease})")}");
        sb.AppendLine($"- Last seen: {issue.LastSeen:u}{(issue.LastRelease is null ? "" : $" (release {issue.LastRelease})")}");
        if (latest is not null)
            sb.AppendLine($"- Latest on: {latest.Platform} {latest.OperatingSystem}");

        var dashboard = config["Alerts:DashboardUrl"];
        if (!string.IsNullOrWhiteSpace(dashboard))
            sb.AppendLine($"- Details: {dashboard.TrimEnd('/')}/issues/{issue.Id}");

        if (latest?.ExceptionJson is { } json && JsonSerializer.Deserialize<ExceptionInfo>(json) is { StackTrace: { Length: > 0 } stack })
            sb.AppendLine().AppendLine("```").AppendLine(Truncate(stack, 4000)).AppendLine("```");

        return (title, sb.ToString());
    }

    private static async Task<string> CreateGitHubAsync(IHttpClientFactory http, IConfiguration config,
        string repo, string token, string title, string body, CancellationToken ct)
    {
        var apiBase = config["Integrations:GitHub:ApiUrl"] ?? "https://api.github.com";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{apiBase.TrimEnd('/')}/repos/{repo}/issues")
        {
            Content = JsonContent.Create(new { title, body, labels = new[] { "avizoro" } })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("Avizoro");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await http.CreateClient(HttpClientName).SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return created.GetProperty("html_url").GetString()!;
    }

    private static async Task<string> CreateJiraAsync(IHttpClientFactory http, IConfiguration config,
        string title, string body, CancellationToken ct)
    {
        var jira = config.GetSection("Integrations:Jira");
        var baseUrl = jira["BaseUrl"]!.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/rest/api/2/issue")
        {
            Content = JsonContent.Create(new
            {
                fields = new
                {
                    project = new { key = jira["ProjectKey"] },
                    summary = Truncate(title, 250),
                    description = body,
                    issuetype = new { name = jira["IssueType"] ?? "Bug" }
                }
            })
        };
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{jira["Email"]}:{jira["ApiToken"]}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var response = await http.CreateClient(HttpClientName).SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return $"{baseUrl}/browse/{created.GetProperty("key").GetString()}";
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;
        var detail = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException($"{(int)response.StatusCode}: {Truncate(detail, 300)}", null, response.StatusCode);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
