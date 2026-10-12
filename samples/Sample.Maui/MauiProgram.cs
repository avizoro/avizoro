using Avizoro.Maui.Extensions;
using Microsoft.Extensions.Logging;

namespace Sample.Maui
{
    public static class MauiProgram
    {
#if ANDROID
        // 10.0.2.2 is the Android emulator's alias for the host machine.
        private const string Dsn = "http://demo-key@10.0.2.2:7099/demo-maui";
#else
        // iOS simulator, Mac Catalyst and Windows reach the host directly.
        private const string Dsn = "http://demo-key@localhost:7099/demo-maui";
#endif

        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                // DSN: http://{projectKey}@{host}:{port}/{projectId}
                // Avizoro.Server runs on the host machine (http profile, port 7099).
                .UseAvizoro(Dsn, options =>
                {
                    // Opt-in: capture the last screen and attach it to crashes.
                    // (Off by default because screenshots can contain personal data.)
                    options.EnableScreenCapture = true;

                    // Opt-in: record every session as a timeline (screens, actions, HTTP,
                    // logs, screenshots) and watch it live on the dashboard's Sessions page.
                    options.EnableSessionTimeline = true;

                    // After a crash, ask on next launch what the user was doing.
                    options.EnableCrashFeedbackPrompt = true;
                    options.CrashFeedbackTitle = "La app se cerró inesperadamente";
                    options.CrashFeedbackMessage = "¿Qué estabas haciendo? (opcional)";
                    options.CrashFeedbackAccept = "Enviar";
                    options.CrashFeedbackCancel = "Omitir";
                })
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
