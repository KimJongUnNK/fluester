using System.Diagnostics;
using Fluester.Core;
using Fluester.Engine;

namespace Fluester.Linux.Gui;

/// <summary>Was das Fenster am System tut: Dienst starten/stoppen, Rechte und Pakete per pkexec (grafische Passwortabfrage).
/// Alles blockierende läuft in Hintergrund-Tasks.</summary>
static class Service
{
    public static Task<bool> StartAsync() => Task.Run(() =>
    {
        if (Daemon.IsRunning()) return true;
        Cli.StartDetached();
        for (int i = 0; i < 40 && !Daemon.IsRunning(); i++) Thread.Sleep(150);
        return Daemon.IsRunning();
    });

    public static Task StopAsync() => Task.Run(() =>
    {
        Daemon.Send("stop");
        for (int i = 0; i < 30 && Daemon.IsRunning(); i++) Thread.Sleep(150);
        // Einheit mit Restart=on-failure: sauber beendet startet sie nicht neu; Reste trotzdem wegräumen
        if (Desktop.Which("systemctl") is { } sc) Desktop.Run(sc, new[] { "--user", "stop", Cli.Unit + ".service" }, timeoutMs: 5000);
    });

    public static async Task<bool> RestartAsync()
    {
        await StopAsync();
        return await StartAsync();
    }

    public static bool PkexecAvailable => Desktop.Which("pkexec") != null;

    /// <summary>Shell-Skript als Administrator über pkexec. Ergebnis: null = geklappt, sonst die Fehlermeldung.</summary>
    public static Task<string?> RunAsAdminAsync(string script, int timeoutMs = 600_000) => Task.Run<string?>(() =>
    {
        var pkexec = Desktop.Which("pkexec") ?? throw new FluesterException("pkexec fehlt");
        var psi = new ProcessStartInfo(pkexec) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "/bin/sh", "-c", script }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var outp = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return "Zeitüberschreitung"; }
        var text = (err.Result + "\n" + outp.Result).Trim();
        Log.Write($"pkexec: Code {p.ExitCode}" + (text.Length > 0 ? " · " + text.Split('\n').Last() : ""));
        return p.ExitCode switch
        {
            0 => null,
            126 => "Abgebrochen – ohne Passwort geht es nicht.",
            127 => "Keine Passwortabfrage verfügbar (kein Polkit-Agent). Bitte im Terminal ausführen.",
            _ => text.Length > 0 ? text.Split('\n').Last() : $"Fehlgeschlagen (Code {p.ExitCode})",
        };
    });

    /// <summary>Gruppe „input“ und /dev/uinput einrichten. Danach läuft der Dienst über „sg input“ sofort mit Tastaturzugriff.</summary>
    public static async Task<string?> FixRightsAsync()
    {
        var (addGroup, fixUinput) = Setup.MissingRights();
        if (!addGroup && !fixUinput) return null;
        var err = await RunAsAdminAsync(Setup.RightsScript(addGroup, fixUinput, Environment.UserName));
        if (err == null && Daemon.IsRunning()) await RestartAsync();
        return err;
    }

    /// <summary>Befehl für Leute ohne Polkit-Agent (z. B. Sway ohne Agent) – zum Kopieren ins Terminal.</summary>
    public static string RightsCommand()
    {
        var (addGroup, fixUinput) = Setup.MissingRights();
        return addGroup || fixUinput ? "fluester setup" : "";
    }

    public static Task<string?> InstallPackagesAsync(IEnumerable<string> packages)
    {
        var cmd = Setup.InstallCommand(packages.Distinct());
        if (cmd.Length == 0) return Task.FromResult<string?>("Paketverwaltung nicht erkannt – bitte von Hand installieren: " + string.Join(", ", packages));
        var script = string.Join(' ', cmd.Select(a => "'" + a.Replace("'", "'\\''") + "'"));
        return RunAsAdminAsync(script).ContinueWith(t => { Desktop.Rescan(); return t.Result; });
    }

    public static string InstallHint(IEnumerable<string> packages) => "sudo " + string.Join(' ', Setup.InstallCommand(packages.Distinct()));
}
