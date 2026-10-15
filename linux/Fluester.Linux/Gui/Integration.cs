using System.Runtime.InteropServices;
using Fluester.Core;
using Fluester.Engine;

namespace Fluester.Linux.Gui;

/// <summary>Einbindung in den Desktop: Eintrag im App-Menü (mit Aktion „Diktat an/aus“) und App-Symbol,
/// beides unter ~/.local/share – ohne Administratorrechte.</summary>
static class Integration
{
    /// <summary>Kennung der App: Name der .desktop-Datei, des Symbols und der Wayland-App-ID – nur so ordnen GNOME und KDE
    /// das Fenster dem Eintrag im App-Menü zu.</summary>
    public const string AppId = "io.github.kimjongunnk.Fluester";

    static string DataHome => Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

    /// <summary>Wohin scripts/install.sh installiert.</summary>
    public static string InstalledDir => Path.Combine(DataHome, "fluester", "app");

    /// <summary>Läuft gerade die installierte Version (nicht ein Entwickler-Build)?</summary>
    public static bool RunningInstalled => Environment.ProcessPath is { } p && Path.GetDirectoryName(p) == InstalledDir;

    public static string DesktopFile => Path.Combine(DataHome, "applications", AppId + ".desktop");
    public static string IconFile => Path.Combine(DataHome, "icons", "hicolor", "scalable", "apps", AppId + ".svg");

    /// <summary>Das App-Symbol (SVG), eingebettet ins Programm.</summary>
    public static string IconSvg
    {
        get
        {
            using var s = typeof(Integration).Assembly.GetManifestResourceStream("fluester.svg")!;
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }
    }

    /// <summary>Pfad für Exec= in .desktop-Dateien (Leerzeichen und Sonderzeichen in Anführungszeichen).</summary>
    static string ExecPath(string path) =>
        path.Any(c => c is ' ' or '"' or '\'' or '\\' or '$' or '`') ? "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`") + "\"" : path;

    public static string DesktopEntry(string exe) => $"""
        [Desktop Entry]
        Type=Application
        Name=Flüster
        GenericName=Diktieren
        Comment=Diktieren in jeder App – Taste halten, sprechen, loslassen
        Exec={ExecPath(exe)} app
        Icon={AppId}
        Terminal=false
        Categories=Utility;Accessibility;
        Keywords=Diktat;Diktieren;Sprache;Spracherkennung;Whisper;Dictation;Speech;Voice;Wispr;
        StartupNotify=true
        StartupWMClass={AppId}
        SingleMainWindow=true
        Actions=toggle;

        [Desktop Action toggle]
        Name=Diktat an/aus (freihändig)
        Exec={ExecPath(exe)} toggle

        """;

    /// <summary>App-Menü-Eintrag und Symbol anlegen bzw. auf den aktuellen Programmpfad bringen. Schnell, bei jedem Start sicher.</summary>
    public static void Install()
    {
        var exe = Environment.ProcessPath ?? throw new FluesterException("Programmpfad unbekannt");
        Directory.CreateDirectory(Path.GetDirectoryName(DesktopFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(IconFile)!);
        WriteIfChanged(IconFile, IconSvg);
        bool changed = WriteIfChanged(DesktopFile, DesktopEntry(exe));
        // Älterer, eigener Eintrag „Flüster: Diktat an/aus“ – steckt jetzt als Aktion im Haupteintrag
        var old = Path.Combine(DataHome, "applications", "fluester-toggle.desktop");
        if (File.Exists(old)) { File.Delete(old); changed = true; }
        if (changed && Desktop.Which("update-desktop-database") is { } udd)
            try { Desktop.Run(udd, new[] { "-q", Path.GetDirectoryName(DesktopFile)! }, timeoutMs: 5000); } catch { }
    }

    /// <summary>Symbol auch ohne App-Menü-Eintrag finden (Vorschau, CI, ungewöhnliches XDG_DATA_HOME): eigener Suchpfad.</summary>
    public static void RegisterIcon()
    {
        try
        {
            var dir = Path.Combine(Paths.Data, "icons");
            Directory.CreateDirectory(dir);
            WriteIfChanged(Path.Combine(dir, AppId + ".svg"), IconSvg);
            if (Gdk.Display.GetDefault() is { } display) Gtk.IconTheme.GetForDisplay(display).AddSearchPath(dir);
        }
        catch (Exception e) { Log.Write("Symbol: " + e.Message); }
    }

    static bool WriteIfChanged(string file, string content)
    {
        if (File.Exists(file) && File.ReadAllText(file) == content) return false;
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, file, overwrite: true);
        return true;
    }

    /// <summary>GTK 4 und libadwaita sind installiert (Flüster bringt sie nicht mit, sie gehören zum System).</summary>
    public static bool LibrariesAvailable(out string missing)
    {
        var gone = new List<string>();
        foreach (var lib in new[] { "libgtk-4.so.1", "libadwaita-1.so.0" })
        {
            if (NativeLibrary.TryLoad(lib, out var h)) NativeLibrary.Free(h);
            else gone.Add(lib);
        }
        missing = string.Join(", ", gone);
        return gone.Count == 0;
    }

    /// <summary>Kann hier ein Fenster aufgehen? Sonst der Grund und was hilft.</summary>
    public static bool CanShowWindow(out string reason)
    {
        if (!LibrariesAvailable(out var missing))
        {
            var pkg = Setup.PackageManager switch
            {
                "apt" => "sudo apt install libadwaita-1-0",
                "dnf" => "sudo dnf install libadwaita",
                "pacman" => "sudo pacman -S libadwaita",
                "zypper" => "sudo zypper install libadwaita-1-0",
                _ => "libadwaita (GTK 4) über die Paketverwaltung installieren",
            };
            reason = $"Für das App-Fenster fehlt {missing}. Installieren: {pkg} – oder alles im Terminal: fluester setup";
            return false;
        }
        if (!Desktop.Wayland && !Desktop.X11)
        {
            reason = "Keine grafische Sitzung (weder WAYLAND_DISPLAY noch DISPLAY gesetzt) – im Terminal geht alles mit: fluester setup";
            return false;
        }
        reason = "";
        return true;
    }
}
