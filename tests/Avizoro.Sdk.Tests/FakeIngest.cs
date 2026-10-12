using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avizoro.Core.Models;

namespace Avizoro.Sdk.Tests;

/// <summary>Stands in for the Avizoro server: records every event the SDK posts.</summary>
public sealed class FakeIngest : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public ConcurrentQueue<AvizoroEvent> Events { get; } = new();

    public HttpStatusCode Status { get; set; } = HttpStatusCode.Created;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Status == HttpStatusCode.Created && request.RequestUri!.AbsolutePath == "/events" && request.Content is not null)
        {
            var e = JsonSerializer.Deserialize<AvizoroEvent>(await request.Content.ReadAsStringAsync(cancellationToken), Json);
            if (e is not null)
                Events.Enqueue(e);
        }
        return new HttpResponseMessage(Status);
    }

    public async Task<AvizoroEvent> WaitForAsync(Func<AvizoroEvent, bool> predicate, string what)
    {
        for (var i = 0; i < 100; i++)
        {
            var hit = Events.FirstOrDefault(predicate);
            if (hit is not null)
                return hit;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Never received: {what}. Got {Events.Count} event(s).");
    }
}

internal static class TempDir
{
    public static string Create()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ik-sdk-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
