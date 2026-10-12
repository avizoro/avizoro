using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Avizoro.Dashboard;
using Avizoro.Dashboard.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The dashboard talks to the Avizoro server API. The base URL comes
// from wwwroot/appsettings.json ("ApiBaseUrl"), falling back to the default
// self-hosted port so a fresh clone just works.
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:7099";

// Holds the admin read-token (persisted in localStorage) and the 401 state.
builder.Services.AddScoped<AuthState>();

// Route the API HttpClient through the auth handler so every request carries the
// bearer token and 401s bubble up as a re-auth prompt.
builder.Services.AddScoped<AuthMessageHandler>();
builder.Services.AddScoped(sp =>
{
    var authHandler = sp.GetRequiredService<AuthMessageHandler>();
    authHandler.InnerHandler = new HttpClientHandler();
    return new HttpClient(authHandler) { BaseAddress = new Uri(apiBaseUrl) };
});
builder.Services.AddScoped<ApiClient>();

// Single shared live connection for real-time event push (one scope in WASM).
builder.Services.AddScoped<LiveEventsClient>();

var host = builder.Build();

// Restore a previously saved token before the first render or API call.
await host.Services.GetRequiredService<AuthState>().InitializeAsync();

await host.RunAsync();
