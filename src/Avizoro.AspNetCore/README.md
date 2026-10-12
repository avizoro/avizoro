# Avizoro.AspNetCore

Self-hosted error tracking + performance for ASP.NET Core, backed by an
[Avizoro](https://github.com/wilsonvargas/Avizoro) server.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.UseAvizoro("http://projectKey@host:7099/my-api");
```

That single line:

- captures exceptions that escape the request pipeline, tagged with method, route,
  path and trace id;
- turns logged errors (`ILogger`) into events and lower-level logs into breadcrumbs,
  **scoped per request** so concurrent requests never mix their trails;
- times every request as an `http.server` span per route template
  (`GET /orders/{id}`) — see the dashboard Performance page;
- queues events on disk and flushes them on shutdown.

Tune it with the options callback (`SampleRate`, `BeforeSend`, `EnablePiiScrubbing`,
`EnablePerformanceMonitoring`, …). Add `.AddAvizoroHandler()` to outgoing
`HttpClient`s to trace calls to other services.
