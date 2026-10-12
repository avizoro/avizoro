# Avizoro.WinForms

Crash reporting and diagnostics for **Windows Forms** apps with a self-hosted
[Avizoro](https://github.com/avizoro/avizoro) server.

```csharp
[STAThread]
static void Main()
{
    using var ik = AvizoroWinForms.Init("http://projectKey@host:7099/my-winforms-app");
    ApplicationConfiguration.Initialize();
    Application.Run(new MainForm());
}
```

By default an unhandled UI-thread exception crashes the app (and is captured as a
crash) instead of showing the WinForms "continue?" dialog. Pass
`crashOnUnhandledUiException: false` to keep running and report it as a handled
error. Also includes frozen-UI (ANR) detection, `ILogger` errors, sessions, and a flush on exit.
