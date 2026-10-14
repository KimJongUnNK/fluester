using System.Diagnostics;
using Fluester.Core;
using Fluester.Engine;

namespace Fluester.Linux;

/// <summary>Einrichtung im Terminal: Rechte (Gruppe input, /dev/uinput), Werkzeuge, Taste, Erkennung, Modell, Autostart.</summary>
static class Setup
{
    const string UdevRule = "/etc/udev/rules.d/80-fluester-uinput.rules";
    const string ModulesConf = "/etc/modules-load.d/fluester-uinput.conf";

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
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode == 0;
    }

    static bool SudoWrite(string file, string content)
    {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, content);
        bool ok = Sudo("install", "-m", "644", tmp, file);
        File.Delete(tmp);
        return ok;
    }

    static bool InGroup(string group)
    {
        var r = Desktop.Run("id", new[] { "-nG" });
        return r.Out.Split(' ', StringSplitOptions.TrimEntries).Contains(group);
    }

    static bool CanWrite(string path)
    {
        try { using var f = new FileStream(path, FileMode.Open, FileAccess.Write); return true; }
        catch { return false; }
    }

    static string? PackageManager => Desktop.Which("apt-get") != null ? "apt" : Desktop.Which("dnf") != null ? "dnf"
        : Desktop.Which("pacman") != null ? "pacman" : Desktop.Which("zypper") != null ? "zypper" : null;

    static string[] InstallCommand(IEnumerable<string> pkgs) => PackageManager switch
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
        _ => what,
    };

    public static int Run(string[] args)
    {
        yes = args.Contains("--yes") || args.Contains("-y");
        var store = new SettingsStore();
        var os = File.Exists("/etc/os-release") ? File.ReadLines("/etc/os-release").FirstOrDefault(l => l.StartsWith("PRETTY_NAME="))?[12..].Trim('"') : null;
        Console.WriteLine("\u001b[1;35mFlüster\u001b[0m – Einrichtung");
        Info($"{os ?? "Linux"} · {Desktop.Session} · {Desktop.DesktopName} · {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");

        // 1) Rechte
        Title("1  Rechte für Taste und Einfügen");
        bool needRelogin = false;
        bool inInput = InGroup("input");
        bool uinputOk = File.Exists("/dev/uinput") && CanWrite("/dev/uinput");
        bool canRead = KeyboardWatcher.Keyboards().Any(k => { try { using var f = File.OpenRead(k.Path); return true; } catch { return false; } });
        if (inInput && uinputOk && canRead) Good("Gruppe „input“ und /dev/uinput passen");
        else
        {
            if (!inInput) Bad("Du bist nicht in der Gruppe „input“ (nötig, um die Diktat-Taste zu lesen)");
            if (!uinputOk) Bad("/dev/uinput nicht beschreibbar (nötig für Strg+V über eine virtuelle Tastatur)");
            Info("Einrichten mit sudo:");
            Info("  usermod -aG input $USER");
            Info($"  udev-Regel {UdevRule}: KERNEL==\"uinput\", GROUP=\"input\", MODE=\"0660\"");
            if (Ask("Jetzt einrichten?"))
            {
                var user = Environment.UserName;
                bool ok = true;
                if (!inInput) { ok &= Sudo("usermod", "-aG", "input", user); needRelogin = true; }
                ok &= SudoWrite(UdevRule, "# Flüster: virtuelle Tastatur für Strg+V\nKERNEL==\"uinput\", SUBSYSTEM==\"misc\", GROUP=\"input\", MODE=\"0660\", OPTIONS+=\"static_node=uinput\"\n");
                ok &= SudoWrite(ModulesConf, "uinput\n");
                Sudo("modprobe", "uinput");
                Sudo("udevadm", "control", "--reload-rules");
                Sudo("udevadm", "trigger", "--name-match=uinput");
                if (ok) Good("Eingerichtet" + (needRelogin ? " – nach dem Setup einmal ab- und wieder anmelden" : ""));
                else Bad("Nicht alles hat geklappt – siehe Meldungen oben");
            }
        }

        // 2) Werkzeuge
        Title("2  Werkzeuge");
        var missing = new List<string>();
        if (Recorder.Tool is { } rec) Good("Aufnahme: " + rec); else { Bad("Aufnahme-Werkzeug fehlt"); missing.Add(Pkg("pulse")); }
        if (Desktop.Wayland) { if (Desktop.Which("wl-copy") != null) Good("Zwischenablage: wl-clipboard"); else { Bad("wl-clipboard fehlt"); missing.Add(Pkg("wayland-clip")); } }
        else { if (Desktop.Which("xclip") != null || Desktop.Which("xsel") != null) Good("Zwischenablage: " + Desktop.ClipboardTool); else { Bad("xclip fehlt"); missing.Add(Pkg("x-clip")); } }
        if (!Desktop.Wayland) { if (Desktop.Which("xdotool") != null) Good("Aktives Fenster: xdotool"); else { Info("Optional: xdotool (Stil je App, Terminal-Erkennung)"); missing.Add(Pkg("xdotool")); } }
        if (Desktop.Which("notify-send") != null) Good("Benachrichtigungen: notify-send"); else { Info("Optional: notify-send (Rückmeldung am Bildschirm)"); missing.Add(Pkg("notify")); }
        if (missing.Count > 0 && PackageManager != null)
        {
            Info("Installieren: sudo " + string.Join(' ', InstallCommand(missing)));
            if (Ask("Jetzt installieren?")) Sudo(InstallCommand(missing));
        }
        else if (missing.Count > 0) Info("Bitte installieren: " + string.Join(", ", missing));

        // 3) Taste
        Title("3  Diktat-Taste");
        var keys = Enum.GetValues<HotkeyChoice>();
        Info("Halten = sprechen, doppelt tippen = freihändig, + Umschalt = Command Mode.");
        Info("Hinweis: Unter Linux lässt sich die Taste nicht „schlucken“ – Feststelltaste schaltet weiter um, daher Strg rechts empfohlen.");
        int k = Choose("Taste?", keys.Select(h => h.Label(linux: true)).ToArray(), Array.IndexOf(keys, store.Current.Hotkey));
        store.Update(s => s.Hotkey = keys[k]);

        // 4) Erkennung
        Title("4  Erkennung");
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

        // 5) KI-Bereinigung
        Title("5  KI-Bereinigung");
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

        // 6) Autostart
        Title("6  Autostart");
        if (Ask("Flüster bei der Anmeldung automatisch starten?"))
        {
            Autostart(true);
            Good("~/.config/autostart/fluester.desktop angelegt");
        }
        store.Update(s => s.OnboardingDone = true);

        Title("Fertig");
        Info($"Diktieren: in ein Textfeld klicken, {store.Current.Hotkey.Label(linux: true)} halten, sprechen, loslassen.");
        Info("Befehle: fluester status · fluester toggle · fluester config · fluester help");
        if (needRelogin)
        {
            Bad("Wichtig: einmal ab- und wieder anmelden (Gruppe „input“ wird erst dann aktiv). Flüster startet danach von selbst.");
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

    public static void Autostart(bool on)
    {
        var dir = Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "autostart");
        var file = Path.Combine(dir, "fluester.desktop");
        if (!on) { if (File.Exists(file)) File.Delete(file); return; }
        Directory.CreateDirectory(dir);
        File.WriteAllText(file, $"""
            [Desktop Entry]
            Type=Application
            Name=Flüster
            Comment=Diktieren in jeder App
            Exec={Environment.ProcessPath} run --background
            Icon=audio-input-microphone
            Terminal=false
            X-GNOME-Autostart-enabled=true
            NoDisplay=true
            """);
        // Eintrag im App-Menü: freihändig an/aus (lässt sich auch als Tastenkürzel im Desktop belegen)
        var apps = Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d ? d
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "applications");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, "fluester-toggle.desktop"), $"""
            [Desktop Entry]
            Type=Application
            Name=Flüster: Diktat an/aus
            Comment=Freihändig diktieren starten oder beenden
            Exec={Environment.ProcessPath} toggle
            Icon=audio-input-microphone
            Terminal=false
            Categories=Utility;Accessibility;
            """);
    }
}
