using System.Diagnostics;
using Fluester.Core;
using Fluester.Engine;

namespace Fluester.Windows;

/// <summary>Aktualisieren aus der App: neue Version laden und prüfen, Flüster beenden, den Programmordner an Ort und
/// Stelle ersetzen (keine zweite Kopie) und neu starten. Das Ersetzen macht ein kleines PowerShell-Skript, weil
/// Windows laufende Programmdateien sperrt.</summary>
static class Updater
{
    public const string Asset = "Fluester-Windows-x64.zip";

    public static string CurrentVersion => typeof(Updater).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Ordner mit Fluester.exe – genau der wird ersetzt.</summary>
    public static string AppDir => Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    /// <summary>Neuere Version oder null, wenn Flüster aktuell ist.</summary>
    public static async Task<string?> CheckAsync(CancellationToken ct = default)
    {
        var latest = await Updates.LatestAsync(ct);
        return Updates.IsNewer(latest, CurrentVersion) ? latest : null;
    }

    /// <summary>Laden, prüfen und das Ersetzen starten. Danach muss Flüster sich beenden (macht der Aufrufer).</summary>
    public static async Task DownloadAndStartAsync(string version, IProgress<double>? progress, CancellationToken ct = default)
    {
        var work = Path.Combine(Path.GetTempPath(), "fluester-update-" + Guid.NewGuid().ToString("N"));
        var zip = await Updates.DownloadAsync(version, Asset, work, progress, ct);
        Start(zip, restart: true);
    }

    /// <summary>Skript starten, das auf das Ende dieses Prozesses wartet und dann <see cref="AppDir"/> durch das Paket ersetzt.</summary>
    public static void Start(string zip, bool restart)
    {
        var work = Path.GetDirectoryName(Path.GetFullPath(zip))!;
        var script = Path.Combine(work, "apply-update.ps1");
        File.WriteAllText(script, ApplyScript, new System.Text.UTF8Encoding(false));
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            // Nicht im Arbeitsordner starten – Windows löscht keinen Ordner, der Arbeitsverzeichnis eines Prozesses ist
            WorkingDirectory = Path.GetTempPath(),
        };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
                     "-ProcessId", Environment.ProcessId.ToString(), "-Zip", Path.GetFullPath(zip), "-Dir", AppDir,
                     "-LogFile", Paths.LogFile, "-Restart", restart ? "1" : "0" })
            psi.ArgumentList.Add(a);
        Log.Write($"Update: ersetze {AppDir} mit {Path.GetFileName(zip)} (Neustart: {restart})");
        Process.Start(psi);
    }

    /// <summary>Bewusst nur ASCII – Windows PowerShell 5.1 liest Skripte ohne BOM sonst falsch.
    /// Sicher: alter Ordner wird erst umbenannt und nur bei Erfolg gelöscht, sonst zurückgeholt.</summary>
    const string ApplyScript = """
        param([int]$ProcessId, [string]$Zip, [string]$Dir, [string]$LogFile, [string]$Restart = '1')
        $ErrorActionPreference = 'Stop'
        Set-Location ([IO.Path]::GetTempPath())
        function Log($m) { try { Add-Content -Path $LogFile -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' Update: ' + $m) } catch { } }
        $work = Split-Path $Zip -Parent
        $old = "$Dir.alt"
        $exe = Join-Path $Dir 'Fluester.exe'
        try {
            # Warten, bis Fluester beendet ist (Programmdateien sind sonst gesperrt)
            try { Wait-Process -Id $ProcessId -Timeout 30 -ErrorAction SilentlyContinue } catch { }
            if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) { Stop-Process -Id $ProcessId -Force; Start-Sleep -Milliseconds 500 }
            $out = Join-Path $work 'neu'
            if (Test-Path $out) { Remove-Item $out -Recurse -Force }
            Expand-Archive -Path $Zip -DestinationPath $out -Force
            if (-not (Test-Path (Join-Path $out 'Fluester.exe'))) { throw 'Fluester.exe fehlt im Paket' }
            if (Test-Path $old) { Remove-Item $old -Recurse -Force }
            # Umbenennen kann kurz scheitern (Virenscanner, Explorer) - ein paar Mal versuchen
            for ($i = 0; $i -lt 20; $i++) {
                try { Rename-Item -LiteralPath $Dir -NewName (Split-Path $old -Leaf); break }
                catch { if ($i -eq 19) { throw }; Start-Sleep -Milliseconds 500 }
            }
            try {
                New-Item -ItemType Directory -Path $Dir -Force | Out-Null
                Copy-Item -Path (Join-Path $out '*') -Destination $Dir -Recurse -Force
                if (-not (Test-Path $exe)) { throw 'Kopieren unvollstaendig' }
            } catch {
                Log ('Kopieren fehlgeschlagen, stelle alte Version wieder her: ' + $_)
                if (Test-Path $Dir) { Remove-Item $Dir -Recurse -Force }
                Rename-Item -LiteralPath $old -NewName (Split-Path $Dir -Leaf)
                throw
            }
            # Deinstallations-Skript der Installation behalten
            $u = Join-Path $old 'uninstall.ps1'
            if (Test-Path $u) { Copy-Item $u $Dir -Force }
            Get-ChildItem $Dir -Recurse -File | Unblock-File
            Remove-Item $old -Recurse -Force -ErrorAction SilentlyContinue
            $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Fluester'
            $entry = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
            if ($entry -and $entry.InstallLocation -eq $Dir) {
                Set-ItemProperty -Path $key -Name 'DisplayVersion' -Value (Get-Item $exe).VersionInfo.ProductVersion
            }
            Log ('fertig: ' + (Get-Item $exe).VersionInfo.ProductVersion)
        } catch {
            Log ('fehlgeschlagen: ' + $_)
        } finally {
            if ($Restart -eq '1' -and (Test-Path $exe)) { Start-Process -FilePath $exe }
            # Paket und Skript aufraeumen (das Skript ist schon geladen)
            Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
        }
        """;
}
