# Avizoro.Blazor

Error tracking for **Blazor WebAssembly** apps with a self-hosted
[Avizoro](https://github.com/avizoro/avizoro) server.

```csharp
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.UseAvizoro("http://projectKey@host:7099/my-spa");

var host = builder.Build();
host.StartAvizoro();
await host.RunAsync();
```

- Unhandled component exceptions (which Blazor logs as `Critical`) and any
  `ILogger` error become events; lower-level logs and route changes become breadcrumbs.
- Add `.AddAvizoroHandler()` to your `HttpClient` registrations for HTTP
  breadcrumbs and timings.
- The browser has no durable storage for the SDK, so the offline queue is in
  memory, and session tracking is off.
- The server must allow your app origin (the default CORS policy allows any origin).
