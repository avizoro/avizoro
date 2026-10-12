namespace Avizoro.Server.Tests;

/// <summary>Builders for the loosely-typed event JSON a client posts to /events.</summary>
internal static class TestEvents
{
    public static Dictionary<string, object?> Exception(
        string projectId = "demo",
        string exceptionType = "System.NullReferenceException",
        string message = "boom",
        string stackTrace = "   at MyApp.Services.Cart.Checkout() in /src/Cart.cs:line 42",
        Guid? id = null,
        object? eventType = null)
        => new()
        {
            ["id"] = (id ?? Guid.NewGuid()).ToString(),
            ["projectId"] = projectId,
            ["applicationName"] = "DemoApp",
            ["applicationVersion"] = "1.0.0",
            ["environment"] = "staging",
            ["platform"] = "Android",
            ["eventType"] = eventType ?? "Exception",
            ["level"] = "Error",
            ["exception"] = new Dictionary<string, object?>
            {
                ["type"] = exceptionType,
                ["message"] = message,
                ["stackTrace"] = stackTrace,
            },
            ["timestamp"] = DateTime.UtcNow,
        };

    public static Dictionary<string, object?> Log(
        string projectId = "demo",
        string message = "hello",
        object? eventType = null,
        Guid? id = null)
        => new()
        {
            ["id"] = (id ?? Guid.NewGuid()).ToString(),
            ["projectId"] = projectId,
            ["applicationName"] = "DemoApp",
            ["applicationVersion"] = "1.0.0",
            ["environment"] = "staging",
            ["platform"] = "Android",
            ["eventType"] = eventType ?? "Log",
            ["level"] = "Information",
            ["message"] = message,
            ["timestamp"] = DateTime.UtcNow,
        };
}
