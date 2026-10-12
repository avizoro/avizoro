using Avizoro.Core.Models;

namespace Avizoro.Maui.CrashReporting;

public partial class CrashReporter
{
    partial void RegisterPlatformHandlers()
    {
        // Keep a reference to the handler we're replacing so we can hand off to
        // it after capturing — that's what actually terminates the process the
        // normal way. Without chaining, the app would freeze instead of closing.
        var previous = Java.Lang.Thread.DefaultUncaughtExceptionHandler;
        Java.Lang.Thread.DefaultUncaughtExceptionHandler =
            new AndroidUncaughtExceptionHandler(this, previous);
    }

    /// <summary>
    /// Bridges the native Android uncaught-exception source into the reporter,
    /// parsing the <see cref="Java.Lang.Throwable"/> into a normalized
    /// <see cref="ExceptionInfo"/>, then delegating to the previous handler so
    /// Android performs its usual crash (and closes the app).
    /// </summary>
    private sealed class AndroidUncaughtExceptionHandler
        : Java.Lang.Object, Java.Lang.Thread.IUncaughtExceptionHandler
    {
        private readonly CrashReporter _owner;
        private readonly Java.Lang.Thread.IUncaughtExceptionHandler? _previous;

        public AndroidUncaughtExceptionHandler(CrashReporter owner, Java.Lang.Thread.IUncaughtExceptionHandler? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void UncaughtException(Java.Lang.Thread thread, Java.Lang.Throwable throwable)
        {
            _owner.CaptureFatal(FromThrowable(throwable));

            if (_previous is not null)
            {
                // Let the platform's default handler run: it shows the standard
                // crash behavior and terminates the process.
                _previous.UncaughtException(thread, throwable);
            }
            else
            {
                // No prior handler: terminate ourselves so we don't leave the
                // process frozen.
                Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
                Java.Lang.JavaSystem.Exit(1);
            }
        }

        private static ExceptionInfo FromThrowable(Java.Lang.Throwable throwable)
        {
            return new ExceptionInfo
            {
                Type = throwable.Class?.Name ?? "Java.Lang.Throwable",
                Message = throwable.Message ?? string.Empty,
                StackTrace = Android.Util.Log.GetStackTraceString(throwable),
                Source = throwable.Class?.CanonicalName ?? string.Empty,
                InnerException = throwable.Cause is null
                    ? null
                    : FromThrowable(throwable.Cause)
            };
        }
    }
}
