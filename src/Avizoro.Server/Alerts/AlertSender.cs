using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avizoro.Server.Data;

namespace Avizoro.Server.Alerts;

/// <summary>
/// Formats an <see cref="AlertJob"/> for its channel and delivers it. Chat
/// channels get a short human message in their native webhook shape; the generic
/// webhook gets the full structured payload (optionally HMAC-signed) for
/// machine consumers. Email goes out over the SMTP server in <c>Alerts:Smtp</c>.
/// </summary>
public sealed class AlertSender
{
    public const string HttpClientName = "alerts";
    public const string SignatureHeader = "X-Avizoro-Signature";

    /// <summary>Header name before the rename; still sent so existing receivers keep verifying.</summary>
    public const string LegacySignatureHeader = "X-IntelligenceKit-Signature";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;

    public AlertSender(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _config = config;
    }

    public async Task SendAsync(AlertJob job, CancellationToken ct = default)
    {
        switch (job.Channel)
        {
            case AlertChannels.Email:
                await SendEmailAsync(job, ct);
                return;

            case AlertChannels.Slack:
                await PostJsonAsync(job.Target, SlackPayload(job), secret: null, ct);
                return;

            case AlertChannels.Discord:
                await PostJsonAsync(job.Target, DiscordPayload(job), secret: null, ct);
                return;

            case AlertChannels.Teams:
                await PostJsonAsync(job.Target, TeamsPayload(job), secret: null, ct);
                return;

            default:
                await PostJsonAsync(job.Target, WebhookPayload(job), job.Secret, ct);
                return;
        }
    }

    /// <summary>Headline used by every human-facing channel.</summary>
    public static string Headline(AlertJob job) => job.Trigger switch
    {
        AlertTriggers.NewIssue => $"New issue in {job.Issue.ProjectId}: {job.Issue.Title}",
        AlertTriggers.Regression => $"Regression in {job.Issue.ProjectId}: {job.Issue.Title}",
        AlertTriggers.Threshold => $"{job.Issue.Title} hit {job.WindowCount} events in {job.WindowMinutes} min ({job.Issue.ProjectId})",
        _ => $"[{job.Trigger}] {job.Issue.Title} ({job.Issue.ProjectId})"
    };

    public string? IssueUrl(AlertJob job)
    {
        var dashboard = _config["Alerts:DashboardUrl"];
        return string.IsNullOrWhiteSpace(dashboard) ? null : $"{dashboard.TrimEnd('/')}/issues/{job.Issue.Id}";
    }

    private string Details(AlertJob job)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(job.Issue.Culprit))
            lines.Add($"Culprit: {job.Issue.Culprit}");
        var message = job.Event?.ExceptionMessage ?? job.Event?.Message;
        if (!string.IsNullOrWhiteSpace(message))
            lines.Add($"Message: {Truncate(message, 300)}");
        if (job.Event is { } e)
            lines.Add($"App: {e.ApplicationName} {e.ApplicationVersion} · {e.Platform} · {e.Environment}");
        lines.Add($"Events: {job.Issue.EventCount} · First seen {job.Issue.FirstSeen:u}");
        lines.Add($"Rule: {job.RuleName}");
        return string.Join('\n', lines);
    }

    private object WebhookPayload(AlertJob job) => new
    {
        trigger = job.Trigger,
        rule = job.RuleName,
        headline = Headline(job),
        url = IssueUrl(job),
        windowCount = job.WindowCount,
        windowMinutes = job.WindowMinutes,
        issue = job.Issue,
        @event = job.Event,
        sentAt = DateTime.UtcNow
    };

    private object SlackPayload(AlertJob job)
    {
        var url = IssueUrl(job);
        var text = $"*{Headline(job)}*\n{Details(job)}" + (url is null ? "" : $"\n<{url}|Open in Avizoro>");
        return new { text };
    }

    private object DiscordPayload(AlertJob job) => new
    {
        content = Truncate(Headline(job), 1900),
        embeds = new[]
        {
            new
            {
                title = Truncate(job.Issue.Title, 250),
                description = Truncate(Details(job), 4000),
                url = IssueUrl(job),
                color = job.Trigger == AlertTriggers.Regression ? 0xF0AD4E : 0xDC3545
            }
        }
    };

    /// <summary>
    /// Adaptive Card envelope — the shape accepted by Teams "Workflows" webhooks
    /// (the replacement for the retired Office 365 connectors).
    /// </summary>
    private object TeamsPayload(AlertJob job)
    {
        var url = IssueUrl(job);
        var body = new List<object>
        {
            new { type = "TextBlock", text = Headline(job), weight = "Bolder", size = "Medium", wrap = true },
            new { type = "TextBlock", text = Details(job).Replace("\n", "\n\n"), wrap = true, isSubtle = true }
        };
        object[] actions = url is null ? [] : [new { type = "Action.OpenUrl", title = "Open in Avizoro", url }];

        return new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = new Dictionary<string, object>
                    {
                        ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
                        ["type"] = "AdaptiveCard",
                        ["version"] = "1.4",
                        ["body"] = body,
                        ["actions"] = actions
                    }
                }
            }
        };
    }

    private async Task PostJsonAsync(string url, object payload, string? secret, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(payload, Json);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrEmpty(secret))
        {
            var signature = Sign(body, secret);
            request.Headers.Add(SignatureHeader, signature);
            request.Headers.Add(LegacySignatureHeader, signature);
        }

        var client = _httpFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Webhook returned {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
    }

    /// <summary><c>sha256=&lt;hex HMAC of the raw body&gt;</c>, GitHub-style, so receivers can verify origin.</summary>
    public static string Sign(string body, string secret)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
        return "sha256=" + Convert.ToHexStringLower(hash);
    }

    private async Task SendEmailAsync(AlertJob job, CancellationToken ct)
    {
        var smtp = _config.GetSection("Alerts:Smtp");
        var host = smtp["Host"];
        var from = smtp["From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("Email alerts need Alerts:Smtp:Host and Alerts:Smtp:From.");

        using var message = new MailMessage { From = new MailAddress(from), Subject = Truncate(Headline(job), 200) };
        foreach (var to in job.Target.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            message.To.Add(to);

        var url = IssueUrl(job);
        message.Body = Details(job) + (url is null ? "" : $"\n\n{url}");

        using var client = new SmtpClient(host, smtp.GetValue("Port", 587))
        {
            EnableSsl = smtp.GetValue("EnableSsl", true)
        };
        var user = smtp["User"];
        if (!string.IsNullOrWhiteSpace(user))
            client.Credentials = new NetworkCredential(user, smtp["Password"]);

        await client.SendMailAsync(message, ct);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
