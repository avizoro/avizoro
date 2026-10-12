namespace Avizoro.Core.Enums;

/// <summary>
/// Severity for log events and breadcrumbs. Mirrors the familiar
/// <c>Microsoft.Extensions.Logging.LogLevel</c> names so it reads naturally to
/// .NET developers.
/// </summary>
public enum SeverityLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}
