namespace Avizoro.Core.Models;

/// <summary>
/// Platform-neutral representation of a captured exception.
/// Each platform crash reporter (managed, Android Throwable, iOS NSException)
/// is responsible for producing one of these, so the rest of the pipeline
/// never needs to know where the crash came from.
/// </summary>
public class ExceptionInfo
{
    public string Type { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string StackTrace { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public ExceptionInfo? InnerException { get; set; }

    /// <summary>
    /// Structured managed frames (see <see cref="StackFrameInfo"/>), used by the
    /// server to symbolicate release builds. Null for native/Java exceptions.
    /// </summary>
    public List<StackFrameInfo>? Frames { get; set; }

    /// <summary>
    /// Builds an <see cref="ExceptionInfo"/> from a managed <see cref="Exception"/>,
    /// preserving the inner-exception chain.
    /// </summary>
    public static ExceptionInfo FromException(Exception exception)
    {
        return new ExceptionInfo
        {
            Type = exception.GetType().FullName ?? exception.GetType().Name,
            Message = exception.Message,
            StackTrace = exception.StackTrace ?? string.Empty,
            Source = exception.Source ?? string.Empty,
            Frames = StackFrameInfo.Capture(exception),
            InnerException = exception.InnerException is null
                ? null
                : FromException(exception.InnerException)
        };
    }
}
