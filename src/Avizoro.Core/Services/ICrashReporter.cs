using Avizoro.Core.Models;

namespace Avizoro.Core.Services;

/// <summary>
/// Abstraction over platform crash capture. Implementations hook the native
/// unhandled-exception sources for their platform and forward a normalized
/// <see cref="ExceptionInfo"/> into the pipeline.
/// </summary>
public interface ICrashReporter
{
    /// <summary>
    /// Registers the platform crash handlers. Called once during app startup.
    /// </summary>
    void Register();

    /// <summary>
    /// Reports an already-parsed exception.
    /// </summary>
    Task ReportAsync(ExceptionInfo exception);
}
