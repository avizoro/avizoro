using Microsoft.JSInterop;

namespace Avizoro.Dashboard.Services;

/// <summary>
/// Holds the shared admin read-token used to reach the Avizoro server,
/// persisted in the browser's localStorage. Also tracks whether the server has
/// challenged us (a 401) so the layout can show a token prompt.
/// </summary>
public sealed class AuthState(IJSRuntime js)
{
    private const string StorageKey = "avizoro_read_token";

    public string? Token { get; private set; }

    public bool HasToken => !string.IsNullOrWhiteSpace(Token);

    /// <summary>True once the server rejected a request for lack of a valid token.</summary>
    public bool ChallengeRequired { get; private set; }

    /// <summary>Raised when the token or the challenge state changes.</summary>
    public event Action? Changed;

    /// <summary>Loads any previously saved token. Call once at startup.</summary>
    public async Task InitializeAsync()
    {
        Token = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
    }

    public async Task SetTokenAsync(string? token)
    {
        Token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();

        if (Token is null)
            await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        else
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, Token);

        ChallengeRequired = false;
        Changed?.Invoke();
    }

    /// <summary>Called by the HTTP handler when the server answers 401.</summary>
    public void NotifyChallenge()
    {
        if (ChallengeRequired)
            return;

        ChallengeRequired = true;
        Changed?.Invoke();
    }
}
