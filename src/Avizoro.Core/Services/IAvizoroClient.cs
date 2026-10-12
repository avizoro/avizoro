using Avizoro.Core.Models;

namespace Avizoro.Core.Services;

public interface IAvizoroClient
{
    Task<SendResult> SendAsync(AvizoroEvent avizoroEvent, CancellationToken cancellationToken = default);

    /// <summary>Uploads the screenshot blob for an already-delivered event.</summary>
    Task<SendResult> SendScreenshotAsync(Guid eventId, byte[] jpeg, CancellationToken cancellationToken = default);
}
