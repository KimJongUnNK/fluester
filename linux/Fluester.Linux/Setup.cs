using System.Diagnostics;
using Fluester.Core;
using Fluester.Engine;

namespace Fluester.Linux;

/// <summary>Einrichtung im Terminal: Rechte (Gruppe input, /dev/uinput), Werkzeuge, Mikrofon, Taste, Erkennung, Modell, Autostart.</summary>
static class Setup
{
    public const string UdevRule = "/etc/udev/rules.d/80-fluester-uinput.rules";
    public const string ModulesConf = "/etc/modules-load.d/fluester-uinput.conf";
    const string UdevRuleText = "# Flüster: virtuelle Tastatur für Strg+V\nKERNEL==\"uinput\", SUBSYSTEM==\"misc\", GROUP=\"input\", MODE=\"0660\", OPTIONS+=\"static_node=uinput\"\n";

    static bool yes;

    static void Title(string s) { Console.WriteLine(); Console.WriteLine($"\u001b[1m{s}\u001b[0m"); }
    static void Good(string s) => Console.WriteLine($"  \u001b[32m✓\u001b[0m {s}");
    static void Bad(string s) => Console.WriteLine($"  \u001b[33m!\u001b[0m {s}");
    static void Info(string s) => Console.WriteLine($"  {s}");

    static bool Ask(string question, bool def = true)
    {
        if (yes) return def;
        Console.Write($"  {question} [{(def ? "J/n" : "j/N")}] ");
        var a = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(a)) return def;
        return a.StartsWith('j') || a.StartsWith('y');
    }

    static int Choose(string question, string[] options, int def)
    {
        for (int i = 0; i < options.Length; i++) Info($"{i + 1}) {options[i]}{(i == def ? "  ← Standard" : "")}");
        if (yes) return def;
        Console.Write($"  {question} [{def + 1}] ");
        var a = Console.ReadLine()?.Trim();
        return int.TryParse(a, out var n) && n >= 1 && n <= options.Length ? n - 1 : def;
    }

    static string Prompt(string question, bool secret = false)
    {
        if (yes) return "";
        Console.Write($"  {question} ");
        if (!secret) return Console.ReadLine()?.Trim() ?? "";
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
            sb.Append(k.KeyChar);
        }
        Console.WriteLine();
        return sb.ToString().Trim();
    }

    /// <summary>Befehl mit sudo, Terminal bleibt verbunden (Passwortabfrage).</summary>
    static bool Sudo(params string[] args)
    {
        var psi = new ProcessStartInfo(Libc.geteuid() == 0 ? args[0] : "sudo") { UseShellExecute = false };
        foreach (var a in Libc.geteuid() == 0 ? args.Skip(1) : args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Werkzeug fehlt (z. B. modprobe in Containern, sudo auf Minimalsystemen) – weiter mit dem Rest
            Bad($"„{psi.FileName}“ gibt es hier nicht – übersprungen");
            return false;
        }
    }

    static bool SudoWrite(string file, string content)
    {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, content);
        bool ok = Sudo("install", "-D", "-m", "644", tmp, file);
        File.Delete(tmp);
        return ok;
    }

    /// <summary>Fedora Atomic (Silverblue, Kinoite …) führt Systemgruppen in /usr/lib/group – usermod findet sie aber nur in /etc/group.</summary>
    static void AddGroupToEtc(string group)
    {
        try
        {
            if (File.ReadLines("/etc/group").Any(l => l.StartsWith(group + ":"))) return;
            if (!File.Exists("/usr/lib/group") || !File.ReadLines("/usr/lib/group").Any(l => l.StartsWith(group + ":"))) return;
        }
        catch { return; }
        Info($"Gruppe „{group}“ aus /usr/lib/group nach /etc/group übernehmen (Fedora Atomic)");
        Sudo("sh", "-c", $"grep -q '^{group}:' /etc/group || grep '^{group}:' /usr/lib/group >> /etc/group");
    }

    public static bool CanWrite(string path)
    {
        try { using var f = new FileStream(path, FileMode.Open, FileAccess.Write); return true; }
        catch { return false; }
    }

    public static string? PackageManager => Desktop.Which("apt-get") != null ? "apt" : Desktop.Which("dnf") != null ? "dnf"
        : Desktop.Which("pacman") != null ? "pacman" : Desktop.Which("zypper") != null ? "zypper" : null;

    public static string[] InstallCommand(IEnumerable<string> pkgs) => PackageManager switch
    {
        "apt" => new[] { "apt-get", "install", "-y" }.Concat(pkgs).ToArray(),
        "dnf" => new[] { "dnf", "install", "-y" }.Concat(pkgs).ToArray(),
        "pacman" => new[] { "pacman", "-S", "--needed", "--noconfirm" }.Concat(pkgs).ToArray(),
        "zypper" => new[] { "zypper", "install", "-y" }.Concat(pkgs).ToArray(),
        _ => Array.Empty<string>(),
    };

    static string Pkg(string what) => (PackageManager, what) switch
    {
        ("apt", "pulse") => "pulseaudio-utils",
        ("dnf", "pulse") => "pulseaudio-utils",
        ("pacman", "pulse") => "libpulse",
        ("zypper", "pulse") => "pulseaudio-utils",
        ("apt", "notify") => "libnotify-bin",
        (_, "notify") => "libnotify",
        (_, "wayland-clip") => "wl-clipboard",
        (_, "x-clip") => "xclip",
        (_, "xdotool") => "xdotool",
        ("apt", "gui") or ("zypper", "gui") => "libadwaita-1-0",
        (_, "gui") => "libadwaita",
        _ => what,
    };

    /// <summary>/dev/uinput ist für Flüster beschreibbar.</summary>
    public static bool UinputOk => File.Exists("/dev/uinput") && CanWrite("/dev/uinput");

    /// <summary>Was an Rechten fehlt: Gruppe „input“ eintragen, /dev/uinput freigeben.
    /// Regel schon da und nur die Gruppe noch nicht aktiv → /dev/uinput passt nach dem Neuanmelden von selbst.</summary>
    public static (bool AddGroup, bool FixUinput) MissingRights()
    {
        bool registered = InputAccess.Active || InputAccess.Registered;
        bool fixUinput = !UinputOk && !(File.Exists(UdevRule) && registered && !InputAccess.Active);
        return (!registered, fixUinput);
    }

    /// <summary>Rechte als ein Shell-Skript für pkexec (App-Fenster). Gleiche Schritte wie im Terminal-Setup.</summary>
    public static string RightsScript(bool addGroup, bool fixUinput, string user)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(user, "^[A-Za-z0-9._][A-Za-z0-9._-]*\\$?$")) throw new FluesterException("Ungewöhnlicher Benutzername: " + user);
        var sb = new System.Text.StringBuilder("set -e\n");
        if (addGroup)
        {
            // Fedora Atomic: Gruppe steht nur in /usr/lib/group
            sb.Append("grep -q '^input:' /etc/group || { grep '^input:' /usr/lib/group >> /etc/group 2>/dev/null || groupadd -r input; }\n");
            sb.Append($"usermod -aG input '{user}'\n");
        }
        if (fixUinput)
        {
            sb.Append($"mkdir -p {Path.GetDirectoryName(UdevRule)} {Path.GetDirectoryName(ModulesConf)}\n");
            sb.Append($"printf '%s' '{UdevRuleText.Replace("'", "'\\''")}' > {UdevRule}\n");
            sb.Append($"echo uinput > {ModulesConf}\n");
            sb.Append("modprobe uinput || true\n");
            sb.Append("udevadm control --reload-rules || true\n");
            sb.Append("udevadm trigger --name-match=uinput || true\n");
        }
        return sb.ToString();
    }

    public sealed record Tool(string Title, string? Found, string Package, bool Required);

    /// <summary>Werkzeuge, die Flüster nutzt – mit Paketnamen, falls sie fehlen.</summary>
    public static List<Tool> Tools()
    {
        var list = new List<Tool> { new("Aufnahme", Recorder.Tool, Pkg("pulse"), true) };
        if (Desktop.Wayland) list.Add(new("Zwischenablage", Desktop.Which("wl-copy") != null ? "wl-clipboard" : null, Pkg("wayland-clip"), true));
        else list.Add(new("Zwischenablage", Desktop.ClipboardAvailable ? Desktop.ClipboardTool : null, Pkg("x-clip"), true));
        if (!Desktop.Wayland) list.Add(new("Aktives Fenster (Stil je App)", Desktop.Which("xdotool") != null ? "xdotool" : null, Pkg("xdotool"), false));
        list.Add(new("Benachrichtigungen", Desktop.Which("notify-send") != null ? "notify-send" : null, Pkg("notify"), false));
        list.Add(new("App-Fenster", Gui.Integration.LibrariesAvailable(out _) ? "GTK 4 + libadwaita" : null, Pkg("gui"), false));
        return list;
    }

    public static int Run(string[] args)
    {
        yes = args.Contains("--yes") || args.Contains("-y");
        var store = new SettingsStore();
        var os = File.Exists("/etc/os-release") ? File.ReadLines("/etc/os-release").FirstOrDefault(l => l.StartsWith("PRETTY_NAME="))?[12..].Trim('"') : null;
        Console.WriteLine("\u001b[1;35mFlüster\u001b[0m – Einrichtung");
        Info($"{os ?? "Linux"} · {Desktop.Session} · {Desktop.DesktopName} · {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");

        // 1) Rechte
        Title("1  Rechte für Taste und Einfügen");
        bool active = InputAccess.Active;                       // wirkt in dieser Anmeldung
        bool registered = active || InputAccess.Registered;     // eingetragen, wirkt spätestens nach dem Neuanmelden
        bool needRelogin = !active && registered;
        bool uinputOk = UinputOk;
        bool canRead = KeyboardWatcher.Keyboards().Any(k => { try { using var f = File.OpenRead(k.Path); return true; } catch { return false; } });
        bool fixUinput = MissingRights().FixUinput;
        if (active && uinputOk && canRead) Good("Gruppe „input“ und /dev/uinput passen");
        else
        {
            if (!registered) Bad("Du bist nicht in der Gruppe „input“ (nötig, um die Diktat-Taste zu lesen)");
            else if (!active) Info("Gruppe „input“ ist eingetragen, aktiv ab dem nächsten Anmelden. Bis dahin startet Flüster über „sg input“ mit Tastaturzugriff.");
            else if (!canRead && KeyboardWatcher.Keyboards().Count > 0) Bad("Tastatur trotz Gruppe „input“ nicht lesbar – Rechte prüfen: ls -l /dev/input/event*");
            if (fixUinput) Bad("/dev/uinput nicht beschreibbar (nötig für Strg+V über eine virtuelle Tastatur)");
            if (!registered || fixUinput)
            {
                Info("Einrichten mit sudo:");
                if (!registered) Info("  usermod -aG input $USER");
                if (fixUinput) Info($"  udev-Regel {UdevRule}: KERNEL==\"uinput\", GROUP=\"input\", MODE=\"0660\"");
                if (Ask("Jetzt einrichten?"))
                {
                    bool ok = true;
                    if (!registered)
                    {
                        AddGroupToEtc("input");
                        ok &= Sudo("usermod", "-aG", "input", Environment.UserName);
                        needRelogin = true;
                    }
                    if (fixUinput)
                    {
                        ok &= SudoWrite(UdevRule, UdevRuleText);
                        ok &= SudoWrite(ModulesConf, "uinput\n");
                        Sudo("modprobe", "uinput");
                        Sudo("udevadm", "control", "--reload-rules");
                        Sudo("udevadm", "trigger", "--name-match=uinput");
                    }
                    if (ok) Good("Eingerichtet" + (needRelogin ? " – nach dem Setup einmal ab- und wieder anmelden" : ""));
                    else Bad("Nicht alles hat geklappt – siehe Meldungen oben");
                }
            }
        }

        // 2) Werkzeuge
        Title("2  Werkzeuge");
        var missing = new List<string>();
        foreach (var t in Tools())
        {
            if (t.Found != null) Good($"{t.Title}: {t.Found}");
            else if (t.Required) Bad($"{t.Title}: fehlt ({t.Package})");
            else Info($"Optional – {t.Title}: {t.Package}");
            if (t.Found == null) missing.Add(t.Package);
        }
        if (missing.Count > 0 && PackageManager != null)
        {
            Info("Installieren: sudo " + string.Join(' ', InstallCommand(missing)));
            if (Ask("Jetzt installieren?")) { Sudo(InstallCommand(missing)); Desktop.Rescan(); }
        }
        else if (missing.Count > 0) Info("Bitte installieren: " + string.Join(", ", missing));

        // 3) Mikrofon
        Title("3  Mikrofon");
        Mic.Choose(store, yes);

        // 4) Taste
        Title("4  Diktat-Taste");
        var keys = Enum.GetValues<HotkeyChoice>();
        Info("Halten = sprechen, doppelt tippen = freihändig, + Umschalt = Command Mode.");
        Info("Hinweis: Unter Linux lässt sich die Taste nicht „schlucken“ – Feststelltaste schaltet weiter um, daher Strg rechts empfohlen.");
        int k = Choose("Taste?", keys.Select(h => h.Label(linux: true)).ToArray(), Array.IndexOf(keys, store.Current.Hotkey));
        store.Update(s => s.Hotkey = keys[k]);

        // 5) Erkennung
        Title("5  Erkennung");
        int engine = Choose("Erkennung?", new[] { "Whisper lokal (offline, kostenlos)", "Groq Cloud (schnell, Gratis-Kontingent, braucht Schlüssel)" },
            store.Current.Engine == EngineChoice.Cloud ? 1 : 0);
        if (engine == 0)
        {
            store.Update(s => s.Engine = EngineChoice.WhisperLocal);
            var models = WhisperModel.All;
            int m = Choose("Modell?", models.Select(x => $"{x.Name} · {x.SizeMB} MB · {x.Note}{(ModelStore.IsInstalled(x) ? " · ✓ geladen" : "")}").ToArray(),
                Math.Max(0, Array.FindIndex(models, x => x.Id == store.Current.WhisperModelId)));
            store.Update(s => s.WhisperModelId = models[m].Id);
            if (!ModelStore.IsInstalled(models[m])) Cli.Download(models[m]).GetAwaiter().GetResult();
            else Good("Modell vorhanden");
        }
        else
        {
            store.Update(s => s.Engine = EngineChoice.Cloud);
            var secrets = new FileSecretStore();
            if (secrets.Get("groq") == null)
            {
                Info("Schlüssel kostenlos holen: https://console.groq.com/keys");
                var key = Prompt("Groq-API-Schlüssel (Eingabe unsichtbar):", secret: true);
                if (key.Length > 0) { secrets.Set("groq", key); Good("Schlüssel gespeichert (~/.config/fluester/keys.json, nur für dich lesbar)"); }
            }
            else Good("Groq-Schlüssel vorhanden");
        }

        // 6) KI-Bereinigung
        Title("6  KI-Bereinigung");
        int clean = Choose("Bereinigung?", new[] { "Nur Regeln (sofort, offline)", "Sprachmodell über Groq (formuliert sauberer, braucht Schlüssel)", "Sprachmodell über Ollama (lokal)" },
            store.Current.Cleanup == CleanupChoice.Llm ? (store.Current.CloudChat.ProviderId == "ollama" ? 2 : 1) : 0);
        store.Update(s =>
        {
            s.Cleanup = clean == 0 ? CleanupChoice.Rules : CleanupChoice.Llm;
            if (clean == 2) s.CloudChat = new ApiConfig("ollama", ProviderPreset.Ollama.BaseUrl, ProviderPreset.Ollama.ChatModel);
            if (clean == 1) s.CloudChat = new ApiConfig("groq", ProviderPreset.Groq.BaseUrl, ProviderPreset.Groq.ChatModel);
        });
        if (clean == 1 && new FileSecretStore().Get("groq") == null)
        {
            var key = Prompt("Groq-API-Schlüssel (https://console.groq.com/keys):", secret: true);
            if (key.Length > 0) new FileSecretStore().Set("groq", key);
        }
        if (clean == 2) Info("Ollama: https://ollama.com – danach „ollama pull qwen2.5:7b“");

        // 7) Autostart
        Title("7  Autostart");
        if (Ask("Flüster bei der Anmeldung automatisch starten?"))
        {
            Autostart(true);
            Good("~/.config/autostart/fluester.desktop angelegt");
        }
        store.Update(s => s.OnboardingDone = true);
        try { Gui.Integration.Install(); } catch (Exception e) { Log.Write("App-Menü-Eintrag: " + e.Message); }

        Title("Fertig");
        Info($"Diktieren: in ein Textfeld klicken, {store.Current.Hotkey.Label(linux: true)} halten, sprechen, loslassen.");
        if (Gui.Integration.LibrariesAvailable(out _)) Info("Einstellungen, Verlauf und Wörterbuch: „Flüster“ im App-Menü (oder fluester app)");
        Info("Befehle: fluester status · fluester mic · fluester toggle · fluester config · fluester help");
        if (needRelogin)
        {
            Bad("Wichtig: einmal ab- und wieder anmelden (Gruppe „input“ wird erst dann überall aktiv). Flüster startet danach von selbst.");
            if (InputAccess.PendingRelogin && Ask("Flüster bis dahin schon starten (über „sg input“, mit Tastaturzugriff)?"))
            {
                if (Daemon.IsRunning())
                {
                    Daemon.Send("stop");
                    for (int i = 0; i < 20 && Daemon.IsRunning(); i++) Thread.Sleep(150);
                }
                Cli.StartDetached();
                for (int i = 0; i < 20 && !Daemon.IsRunning(); i++) Thread.Sleep(150);
                Info(Daemon.Send("status") ?? "Start fehlgeschlagen – „fluester run“ zeigt die Meldungen");
            }
            return 0;
        }
        if (!Daemon.IsRunning() && Ask("Flüster jetzt starten?"))
        {
            Cli.StartDetached();
            Thread.Sleep(1200);
            Info(Daemon.Send("status") ?? "Start fehlgeschlagen – „fluester run“ zeigt die Meldungen");
        }
        else if (Daemon.IsRunning())
        {
            Info("Flüster läuft und übernimmt die neuen Einstellungen sofort.");
        }
        return 0;
    }

    public static string AutostartFile => Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "autostart", "fluester.desktop");

    public static bool AutostartEnabled => File.Exists(AutostartFile);

    public static void Autostart(bool on)
    {
        var file = AutostartFile;
        var dir = Path.GetDirectoryName(file)!;
        if (!on) { if (File.Exists(file)) File.Delete(file); return; }
        Directory.CreateDirectory(dir);
        File.WriteAllText(file, $"""
            [Desktop Entry]
            Type=Application
            Name=Flüster
            Comment=Diktieren in jeder App
            Exec={Environment.ProcessPath} start
            Icon={Gui.Integration.AppId}
            Terminal=false
            X-GNOME-Autostart-enabled=true
            NoDisplay=true
            """);
        // App-Menü: „Flüster“ (Fenster) mit Aktion „Diktat an/aus“
        try { Gui.Integration.Install(); } catch (Exception e) { Log.Write("App-Menü-Eintrag: " + e.Message); }
    }
}
