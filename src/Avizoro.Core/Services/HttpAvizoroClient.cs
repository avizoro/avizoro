using System.Net.Http.Json;
using Avizoro.Core.Configuration;
using Avizoro.Core.Models;

namespace Avizoro.Core.Services;

/// <summary>
/// Default <see cref="IAvizoroClient"/>: posts events (JSON) and screenshots
/// (raw JPEG) to the server named in the DSN, classifying responses for the
/// store-and-forward uploader. Shared by every platform SDK.
/// </summary>
public class HttpAvizoroClient : IAvizoroClient
{
    private readonly HttpClient _httpClient;
    private readonly AvizoroOptions _options;

    public HttpAvizoroClient(HttpClient httpClient, AvizoroOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<SendResult> SendAsync(AvizoroEvent avizoroEvent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ServerUrl))
            return SendResult.Rejected; // misconfigured; don't retry forever

        var url = $"{_options.ServerUrl}/events";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(avizoroEvent)
            };

            if (!string.IsNullOrWhiteSpace(_options.ProjectKey))
                request.Headers.Add("X-Avizoro-Key", _options.ProjectKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            return HttpStatusClassifier.Classify((int)response.StatusCode);
        }
        catch (Exception)
        {
            // Network/DNS/timeout: keep the event and retry later.
            return SendResult.TransientFailure;
        }
    }

    public async Task<SendResult> SendScreenshotAsync(Guid eventId, byte[] jpeg, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ServerUrl))
            return SendResult.Rejected;

        var url = $"{_options.ServerUrl}/events/{eventId}/screenshot";

        try
        {
            using var content = new ByteArrayContent(jpeg);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            if (!string.IsNullOrWhiteSpace(_options.ProjectKey))
                request.Headers.Add("X-Avizoro-Key", _options.ProjectKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return HttpStatusClassifier.Classify((int)response.StatusCode);
        }
        catch (Exception)
        {
            return SendResult.TransientFailure;
        }
    }
}
