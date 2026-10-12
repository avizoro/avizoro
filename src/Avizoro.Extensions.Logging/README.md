# Avizoro.Extensions.Logging

`Microsoft.Extensions.Logging` provider for [Avizoro](https://github.com/avizoro/avizoro).

```csharp
builder.Logging.AddAvizoro(o =>
{
    o.MinimumBreadcrumbLevel = LogLevel.Information; // trail attached to later events
    o.MinimumEventLevel = LogLevel.Error;            // sent as grouped events
});
```

- Entries at or above `MinimumBreadcrumbLevel` become `log` breadcrumbs.
- Entries at or above `MinimumEventLevel` become events: an exception event when an
  exception is attached, otherwise a log event grouped by its **message template**
  (`"Order {Id} failed"` is one issue, not one per id). Structured properties land in
  the event data.

Requires an `IAvizoro` registered by a platform SDK (`Avizoro.Maui`
registers this provider for you; `Avizoro.AspNetCore` too).
