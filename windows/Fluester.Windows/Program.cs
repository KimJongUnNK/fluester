using System.Windows.Forms;
using Fluester.Engine;

namespace Fluester.Windows;

static class Program
{
    const string MutexName = "Local\\Fluester.SingleInstance";
    const string ShowEventName = "Local\\Fluester.ShowSettings";

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--selftest")) return SelfTest.Run();

        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Läuft schon → dort die Einstellungen öffnen
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.System);
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Application.ThreadException += (_, e) => Log.Write("UI-Fehler: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("Absturz: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Write("Task: " + e.Exception); e.SetObserved(); };
        Log.Write($"Start {typeof(Program).Assembly.GetName().Version} auf {Environment.OSVersion}");

        var app = new TrayApp(background: args.Contains("--background"));
        // Zweiter Start (z. B. Doppelklick im Startmenü) holt die Einstellungen nach vorn
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var ui = SynchronizationContext.Current!;
        var waiter = new Thread(() =>
        {
            while (showEvent.WaitOne())
                ui.Post(_ => app.ShowSettings(Ui.SettingsPage.General), null);
        }) { IsBackground = true, Name = "Fluester.ShowSettings" };
        waiter.Start();
        Application.Run(app);
        return 0;
    }
}
