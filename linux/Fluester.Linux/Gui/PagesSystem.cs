using System.Diagnostics;
using System.Text.Json;
using Fluester.Engine;

namespace Fluester.Linux.Gui;

sealed partial class Pages
{
    // ───────────────────────── Berechtigungen

    Gtk.Widget Rights()
    {
        var page = Ui.Page();
        page.Add(RightsGroup(w, () => w.Rebuild(Page.Rights)));
        page.Add(ToolsGroup(w, () => w.Rebuild(Page.Rights)));

        var session = Ui.Group("Sitzung");
        session.Add(Ui.Row("Grafische Sitzung", $"{Desktop.Session} · {Desktop.DesktopName}"));
        var barRow = Ui.Row("Flow-Leiste");
        w.OnStatus(() => Ui.SetSubtitle(barRow, w.Status?.Bar is { Length: > 0 } b ? b : w.Live ? "Flüster läuft nicht" : "–"));
        session.Add(barRow);
        session.Add(Ui.Row("Aktive App erkennen (Stil je App)",
            Desktop.X11 ? (Desktop.Which("xdotool") != null ? "über xdotool" : "xdotool fehlt – Stil gilt dann für alle Apps gleich")
            : Environment.GetEnvironmentVariable("SWAYSOCK") != null ? "über swaymsg"
            : Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE") != null ? "über hyprctl"
            : "Unter Wayland (GNOME, KDE) nicht möglich – es gilt „Alle anderen Apps“"));
        page.Add(session);
        return page;
    }

    /// <summary>Gruppe „input“ und /dev/uinput mit Knopf zum Einrichten – auch Schritt der Einrichtung.</summary>
    public static Adw.PreferencesGroup RightsGroup(MainWindow w, Action refresh)
    {
        var g = Ui.Group("Tastatur und Einfügen", "Flüster liest die Diktat-Taste direkt von der Tastatur und fügt über eine eigene virtuelle Tastatur ein. " +
            "So geht es unter X11 und Wayland gleich – es braucht aber diese zwei Rechte.");
        bool active = InputAccess.Active, registered = active || InputAccess.Registered;
        g.Add(Ui.StatusRow("Gruppe „input“",
            active ? "Aktiv – Flüster kann die Diktat-Taste lesen"
            : registered ? "Eingetragen. Gilt ab dem nächsten Anmelden überall – bis dahin startet Flüster über „sg input“ mit Zugriff."
            : "Fehlt – ohne kann Flüster die Diktat-Taste nicht lesen",
            active ? true : registered ? null : false));
        bool uinput = Setup.UinputOk;
        bool ruleWaiting = !uinput && File.Exists(Setup.UdevRule) && registered && !active;
        g.Add(Ui.StatusRow("Virtuelle Tastatur (/dev/uinput)",
            uinput ? "Bereit – Einfügen per Strg+V in jeder App"
            : ruleWaiting ? "Freigegeben, gilt nach dem nächsten Anmelden (bis dahin über „sg input“)"
            : "Nicht beschreibbar – Text landet dann nur in der Zwischenablage",
            uinput ? true : ruleWaiting ? null : false));
        var (addGroup, fixUinput) = Setup.MissingRights();
        if (addGroup || fixUinput)
        {
            var fix = Ui.Row("Jetzt einrichten", Service.PkexecAvailable
                ? "Fragt einmal nach deinem Passwort. Danach geht es sofort – ganz ohne Neuanmelden."
                : "pkexec fehlt – im Terminal einrichten: fluester setup");
            fix.AddPrefix(Ui.Icon("system-run-symbolic"));
            if (Service.PkexecAvailable)
            {
                Gtk.Button? b = null;
                b = Ui.Button("Einrichten …", () => Ui.Run(async () =>
                {
                    b!.Sensitive = false;
                    Ui.SetSubtitle(fix, "Warte auf das Passwort …");
                    var err = await Service.FixRightsAsync();
                    b.Sensitive = true;
                    if (err != null)
                    {
                        Ui.SetSubtitle(fix, err + (err.Contains("Terminal") ? " – fluester setup" : ""));
                        w.Toast(err);
                        return;
                    }
                    w.Toast("Rechte eingerichtet – Flüster hat jetzt Zugriff auf die Taste");
                    refresh();
                }), "suggested-action");
                fix.AddSuffix(b);
            }
            else fix.AddSuffix(Ui.Button("Befehl kopieren", () => { Ui.Copy("fluester setup"); w.Toast("Kopiert – im Terminal einfügen"); }));
            g.Add(fix);
        }
        return g;
    }

    /// <summary>Werkzeuge mit Knopf zum Nachinstallieren.</summary>
    public static Adw.PreferencesGroup ToolsGroup(MainWindow w, Action refresh)
    {
        var g = Ui.Group("Werkzeuge", "Kleine Standardprogramme, die Flüster nutzt. Fehlt eines, installiert Flüster es über die Paketverwaltung.");
        var tools = Setup.Tools();
        foreach (var t in tools)
            g.Add(Ui.StatusRow(t.Title, t.Found ?? (t.Required ? $"Fehlt – Paket „{t.Package}“" : $"Optional – Paket „{t.Package}“"),
                t.Found != null ? true : t.Required ? false : null));
        var missing = tools.Where(t => t.Found == null).Select(t => t.Package).Distinct().ToList();
        if (missing.Count > 0 && Setup.PackageManager != null)
        {
            var row = Ui.Row("Fehlende installieren", Service.InstallHint(missing));
            row.AddPrefix(Ui.Icon("system-run-symbolic"));
            if (Service.PkexecAvailable)
            {
                Gtk.Button? b = null;
                b = Ui.Button("Installieren …", () => Ui.Run(async () =>
                {
                    b!.Sensitive = false;
                    Ui.SetSubtitle(row, "Installiere – das kann eine Minute dauern …");
                    var err = await Service.InstallPackagesAsync(missing);
                    b.Sensitive = true;
                    if (err != null) { Ui.SetSubtitle(row, err); w.Toast(err); return; }
                    w.Toast("Installiert");
                    refresh();
                }), tools.Any(t => t.Found == null && t.Required) ? new[] { "suggested-action" } : Array.Empty<string>());
                row.AddSuffix(b);
            }
            else row.AddSuffix(Ui.Button("Befehl kopieren", () => { Ui.Copy(Service.InstallHint(missing)); w.Toast("Kopiert – im Terminal einfügen"); }));
            g.Add(row);
        }
        return g;
    }

    // ───────────────────────── Über

    static string Version => typeof(Pages).Assembly.GetName().Version?.ToString(3) ?? "dev";
    const string Repo = "https://github.com/KimJongUnNK/fluester";

    Gtk.Widget About()
    {
        var page = Ui.Page();
        var hero = Ui.Group("");
        var icon = Ui.Icon(Integration.AppId, 112);
        icon.MarginTop = 6;
        var title = Ui.Label("Flüster " + Version, "fl-hero");
        title.Halign = Gtk.Align.Center;
        var tag = Ui.Label("Diktieren in jeder App: halten, sprechen, loslassen. Kostenlos – Erkennung und KI wahlweise komplett auf deinem Rechner.", "dim-label");
        tag.Halign = Gtk.Align.Center;
        tag.Justify = Gtk.Justification.Center;
        tag.MaxWidthChars = 60;
        hero.Add(Ui.VBox(10, icon, title, tag));
        page.Add(hero);

        var version = Ui.Group("Version");
        var update = Ui.Row("Updates", w.Live ? "Prüfe …" : "–");
        version.Add(update);
        if (w.Live) Ui.Run(() => CheckUpdate(update));
        page.Add(version);

        var help = Ui.Group("Hilfe");
        var bar = Ui.Row("Flow-Leiste testen", "Zeigt die Leiste ein paar Sekunden in allen Zuständen – unten in der Bildschirmmitte.");
        bar.AddSuffix(Ui.Button("Zeigen", () => Spawn("bar")));
        help.Add(bar);
        var onboarding = Ui.Row("Einrichtung erneut starten", "Rechte, Mikrofon, Taste, Erkennung – Schritt für Schritt.");
        onboarding.AddSuffix(Ui.Button("Starten", w.ShowOnboarding));
        help.Add(onboarding);
        var log = Ui.Row("Protokoll", "Jeder Schritt mit Dauer – nie der diktierte Text. " + Paths.LogFile);
        log.AddSuffix(Ui.Button("Öffnen", () => { if (File.Exists(Paths.LogFile)) Ui.Open(Paths.LogFile); else w.Toast("Noch kein Protokoll"); }));
        help.Add(log);
        var data = Ui.Row("Datenordner", "Modelle, Verlauf, Protokoll – " + Paths.Data);
        data.AddSuffix(Ui.Button("Öffnen", () => Ui.Open(Paths.Data)));
        help.Add(data);
        var config = Ui.Row("Einstellungen als Datei", Paths.SettingsFile);
        config.AddSuffix(Ui.Button("Ordner öffnen", () => Ui.Open(Paths.Config)));
        help.Add(config);
        page.Add(help);

        var links = Ui.Group("Links");
        foreach (var (label, sub, url) in new[]
        {
            ("Anleitung für Linux", "Einrichtung, Befehle, häufige Probleme", Repo + "/blob/main/docs/linux.md"),
            ("Flüster auf GitHub", "Quellcode, Versionen, Änderungen", Repo),
            ("Problem melden", "Mit der Ausgabe von „fluester status“ hilft es am meisten", Repo + "/issues"),
        })
        {
            var r = Ui.Row(label, sub);
            r.AddSuffix(Ui.Icon("adw-external-link-symbolic"));
            r.Activatable = true;
            r.OnActivated += (_, _) => Ui.Safe(() => Ui.Open(url));
            links.Add(r);
        }
        page.Add(links);
        page.Add(Ui.Group("", "Spracherkennung: whisper.cpp (MIT) über Whisper.net, oder OpenAI-kompatible APIs. Oberfläche: GTK 4 und libadwaita über Gir.Core. " +
            "Flüster enthält keinen Code von Wispr Flow und ist nicht mit Wispr verbunden. Lizenz: MIT."));
        return page;
    }

    /// <summary>Startet <c>fluester …</c> als eigenen Prozess (z. B. die Probe der Flow-Leiste).</summary>
    static void Spawn(params string[] args)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }

    /// <summary>Installiert über scripts/install.sh? Nur dann kann Flüster sich selbst aktualisieren.</summary>
    static bool SelfUpdatable => Integration.RunningInstalled;

    async Task CheckUpdate(Adw.ActionRow row)
    {
        string? latest;
        try
        {
            latest = await Task.Run(async () =>
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Fluester/" + Version);
                using var doc = JsonDocument.Parse(await http.GetStringAsync("https://api.github.com/repos/KimJongUnNK/fluester/releases/latest"));
                return doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v');
            });
        }
        catch (Exception e)
        {
            Ui.SetSubtitle(row, "Konnte nicht prüfen (" + e.Message + ")");
            return;
        }
        if (latest == null || !System.Version.TryParse(latest, out var l) || !System.Version.TryParse(Version, out var mine) || l <= mine)
        {
            Ui.SetSubtitle(row, $"✓ Aktuell ({Version})");
            return;
        }
        Ui.SetSubtitle(row, $"Version {latest} ist da (du hast {Version})");
        if (!SelfUpdatable)
        {
            row.AddSuffix(Ui.Button("Herunterladen ↗", () => Ui.Open(Repo + "/releases/latest")));
            return;
        }
        Gtk.Button? b = null;
        b = Ui.Button("Aktualisieren", () => Ui.Run(async () =>
        {
            b!.Sensitive = false;
            Ui.SetSubtitle(row, "Lade und installiere …");
            var (code, output) = await Task.Run(() =>
            {
                var r = Desktop.Run("/bin/bash", new[] { "-c", "curl -fsSL https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.sh | FLUESTER_NO_SETUP=1 bash" },
                    timeoutMs: 600_000);
                return (r.Code, (r.Err + r.Out).Trim());
            });
            Log.Write($"Aktualisierung: Code {code}");
            if (code != 0)
            {
                b.Sensitive = true;
                Ui.SetSubtitle(row, "Aktualisierung fehlgeschlagen: " + (output.Split('\n').LastOrDefault() ?? ""));
                return;
            }
            // Die alten Programmdateien sind ersetzt – gleich neu öffnen, bevor das Fenster etwas Neues nachladen muss
            Ui.SetSubtitle(row, $"✓ Auf {latest} aktualisiert – öffne neu …");
            row.Remove(b);
            Ui.After(1200, Relaunch);
        }), "suggested-action");
        row.AddSuffix(b);
    }

    /// <summary>Fenster schließen und die neue Version öffnen (kurz warten, damit diese Instanz weg ist).</summary>
    static void Relaunch()
    {
        var exe = Path.Combine(Integration.InstalledDir, "fluester");
        var psi = new ProcessStartInfo(Desktop.Which("setsid") ?? "/bin/sh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        if (psi.FileName != "/bin/sh") psi.ArgumentList.Add("/bin/sh");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add($"sleep 1; exec '{exe.Replace("'", "'\\''")}' app");
        Process.Start(psi);
        DesktopApp.App?.Quit();
    }
}
