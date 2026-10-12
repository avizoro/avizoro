using Avizoro.Core.Configuration;

namespace Avizoro.Maui.Services;

/// <summary>
/// Kept for API compatibility with 1.0 — the implementation now lives in
/// <see cref="Avizoro.Core.Services.HttpAvizoroClient"/>, shared by every SDK.
/// </summary>
public class HttpAvizoroClient : Avizoro.Core.Services.HttpAvizoroClient
{
    public HttpAvizoroClient(HttpClient httpClient, AvizoroOptions options)
        : base(httpClient, options)
    {
    }
}
