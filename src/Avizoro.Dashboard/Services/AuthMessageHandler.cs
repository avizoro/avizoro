using System.Net;
using System.Net.Http.Headers;

namespace Avizoro.Dashboard.Services;

/// <summary>
/// Attaches the admin read-token as a <c>Bearer</c> header on every API call and
/// flags <see cref="AuthState"/> when the server answers 401, so the UI can prompt
/// for a (new) token.
/// </summary>
public sealed class AuthMessageHandler(AuthState auth) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (auth.HasToken)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            auth.NotifyChallenge();

        return response;
    }
}
