using Avizoro.Core.Configuration;
using Avizoro.Core.Diagnostics;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Core.Services;
using Avizoro.Core.Storage;
using Avizoro.Maui.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Maui
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
#if IOS || MACCATALYST
            NativeExceptionBtn.IsVisible = true;
            NativeSignalBtn.IsVisible = true;
#endif
        }

        private static IServiceProvider Services =>
            IPlatformApplication.Current!.Services;

        private async void OnTrackEventClicked(object? sender, EventArgs e)
        {
            var kit = Services.GetRequiredService<IAvizoro>();

            var avizoroEvent = new AvizoroEvent
            {
                EventType = EventType.Log,
                Data =
                {
                    ["Message"] = "Evento de prueba desde el sample",
                    ["ClickedAt"] = DateTime.Now.ToString("O")
                }
            };

            await kit.TrackAsync(avizoroEvent);
            await ShowQueueAsync("Evento normal encolado y enviado.");
        }

        private async void OnTrackExceptionClicked(object? sender, EventArgs e)
        {
            var kit = Services.GetRequiredService<IAvizoro>();

            try
            {
                throw new InvalidOperationException("Excepción controlada de prueba");
            }
            catch (Exception ex)
            {
                await kit.TrackExceptionAsync(ex);
            }

            await ShowQueueAsync("Excepción controlada reportada.");
        }

        private async void OnShowQueueClicked(object? sender, EventArgs e)
        {
            await ShowQueueAsync("Cola consultada.");
        }

        private async void OnFlushClicked(object? sender, EventArgs e)
        {
            var uploader = Services.GetRequiredService<IEventUploader>();
            await uploader.FlushAsync();
            await ShowQueueAsync("Flush ejecutado.");
        }

        private async void OnGoSecondPageClicked(object? sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("SecondPage");
        }

        private void OnSetUserClicked(object? sender, EventArgs e)
        {
            var kit = Services.GetRequiredService<IAvizoro>();
            kit.SetUser($"anonymous-{Random.Shared.Next(10000, 99999)}");
            kit.SetTag("plan", "premium");
            StatusLabel.Text = "Usuario y tag asociados a los próximos eventos.";
        }

        private int _breadcrumbCount;

        private void OnAddBreadcrumbClicked(object? sender, EventArgs e)
        {
            var kit = Services.GetRequiredService<IAvizoro>();
            _breadcrumbCount++;
            kit.AddBreadcrumb(
                $"Tap on demo action #{_breadcrumbCount}",
                BreadcrumbCategories.User,
                data: new Dictionary<string, string> { ["count"] = _breadcrumbCount.ToString() });
            StatusLabel.Text = $"Breadcrumb #{_breadcrumbCount} agregado (viaja con el próximo evento).";
        }

        private int _actionCount;

        private void OnTrackActionClicked(object? sender, EventArgs e)
        {
            var kit = Services.GetRequiredService<IAvizoro>();
            _actionCount++;
            kit.TrackAction("demo_button_clicked", new Dictionary<string, string> { ["count"] = _actionCount.ToString() });
            StatusLabel.Text = $"Acción #{_actionCount} registrada (aparece en la timeline de la sesión).";
        }

        // One instrumented client for the page: every request becomes an "http" step.
        private HttpClient? _http;

        private async void OnHttpClicked(object? sender, EventArgs e)
        {
            var kit = Services.GetRequiredService<IAvizoro>();
            var server = Services.GetRequiredService<AvizoroOptions>().ServerUrl;
            _http ??= new HttpClient(new AvizoroHttpHandler(kit) { InnerHandler = new HttpClientHandler() })
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            try
            {
                using var response = await _http.GetAsync($"{server}/health/live");
                StatusLabel.Text = $"GET /health/live → {(int)response.StatusCode}.";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"La llamada falló ({ex.GetType().Name}); también queda en la timeline.";
            }
        }

        private async void OnScreenshotClicked(object? sender, EventArgs e)
        {
            var captured = await Services.GetRequiredService<IScreenCapture>().CaptureToTimelineAsync();
            StatusLabel.Text = captured
                ? "Captura agregada a la timeline de la sesión."
                : "No se pudo capturar (timeline desactivada o pantalla excluida).";
        }

        private async void OnLogClicked(object? sender, EventArgs e)
        {
            var kit = Services.GetRequiredService<IAvizoro>();
            await kit.TrackLogAsync(SeverityLevel.Warning, "Token de pago vacío",
                new Dictionary<string, string> { ["service"] = "PaymentService" });
            await ShowQueueAsync("Log (warning) registrado.");
        }

        private void OnFreezeClicked(object? sender, EventArgs e)
        {
            // Deliberately block the UI thread longer than the 5 s ANR threshold.
            Thread.Sleep(TimeSpan.FromSeconds(7));
        }

        private void OnCrashClicked(object? sender, EventArgs e)
        {
            // Unhandled on purpose: exercises the crash reporter. The event is
            // persisted to SQLite before the process dies and uploaded on the
            // next launch.
            throw new Exception("Crash generado desde botón de prueba");
        }

        private void OnNativeExceptionClicked(object? sender, EventArgs e)
        {
#if IOS || MACCATALYST
            // The run loop performs a selector the object doesn't implement: an
            // NSInvalidArgumentException raised by native code, not by this method.
            new Foundation.NSObject().PerformSelector(new ObjCRuntime.Selector("avizoroMissingSelector"), null, 0.1);
#endif
        }

        private void OnNativeSignalClicked(object? sender, EventArgs e)
        {
#if IOS || MACCATALYST
            // No exception at all: the process just receives SIGABRT. Avizoro can only
            // leave a marker, and reports the crash on the next launch.
            abort();
#endif
        }

#if IOS || MACCATALYST
        [System.Runtime.InteropServices.DllImport("libc")]
        private static extern void abort();
#endif

        private async Task ShowQueueAsync(string action)
        {
            var store = Services.GetRequiredService<IEventStore>();
            var pending = await store.CountAsync();
            StatusLabel.Text = $"{action} Pendientes en cola: {pending}.";
        }
    }
}
