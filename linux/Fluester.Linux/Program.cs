using System.Diagnostics;
using Fluester.Core;
using Fluester.Engine;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("linux")]

namespace Fluester.Linux;

static class Program
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var cmd = args.Length > 0 ? args[0] : "help";
        var rest = args.Skip(1).ToArray();
        try
        {
            return cmd switch
            {
                "run" => RunDaemon(rest.Contains("--background")),
                "e2e-keys" => SelfTest.PressHotkey(rest),
                "setup" => Setup.Run(rest),
                "start" => Cli.Start(),
                "status" or "toggle" or "cancel" or "paste-last" or "stop" => Cli.Remote(cmd),
                "mic" or "mics" or "mikro" or "mikrofon" or "microphone" or "input" => Mic.Run(rest),
                "bar" or "leiste" => Cli.BarDemo(),
                "model" or "models" => Cli.Model(rest).GetAwaiter().GetResult(),
                "key" => Cli.Key(rest),
                "config" => Cli.Config(),
                "transcribe" => Cli.Transcribe(rest).GetAwaiter().GetResult(),
                "test" => Cli.MicTest().GetAwaiter().GetResult(),
                "selftest" => SelfTest.Run().GetAwaiter().GetResult(),
                "autostart" => Cli.AutostartCmd(rest),
                "version" or "--version" => Cli.Version(),
                _ => Cli.Help(),
            };
        }
        catch (FluesterException e)
        {
            Console.Error.WriteLine("✗ " + e.Message);
            return 1;
        }
    }

    static int RunDaemon(bool background)
    {
        // Im Hintergrund hängt niemand mehr an der Ausgabe – Meldungen stehen im Protokoll (fluester.log)
        if (background) { Console.SetOut(TextWriter.Null); Console.SetError(TextWriter.Null); }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("ABSTURZ: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Write("Unbeobachteter Fehler: " + e.Exception); e.SetObserved(); };
        using var d = new Daemon();
        return d.Run();
    }
}

static class Cli
{
    public static int Help()
    {
        Console.WriteLine($"""
            Flüster {Ver} – Diktieren in jeder App. Taste halten, sprechen, loslassen.

              fluester setup              Einrichtung (Rechte, Werkzeuge, Mikrofon, Taste, Modell, Autostart)
              fluester mic                Mikrofon wählen und Pegel testen
              fluester mic list | test | <nummer>
              fluester bar                Flow-Leiste zur Probe zeigen
              fluester start              im Hintergrund starten
              fluester run                im Vordergrund laufen lassen (zeigt Meldungen)
              fluester status             läuft er? Taste, Erkennung, Probleme
              fluester toggle             freihändig starten/beenden (für eigene Tastenkürzel)
              fluester cancel             laufendes Diktat abbrechen
              fluester paste-last         letztes Diktat erneut einfügen
              fluester stop               beenden
              fluester test               5 Sekunden aufnehmen und erkennen (ohne Einfügen)
              fluester transcribe a.wav   WAV-Datei erkennen
              fluester model [list|download <id>|delete <id>]
              fluester key <groq|openai|custom> <schlüssel>
              fluester config             Einstellungen im Editor öffnen
              fluester autostart on|off
              fluester selftest

            Einstellungen: {Paths.SettingsFile}
            Anleitung:     https://github.com/KimJongUnNK/fluester/blob/main/docs/linux.md
            """);
        return 0;
    }

    static string Ver => typeof(Cli).Assembly.GetName().Version?.ToString(3) ?? "dev";

    public static int Version() { Console.WriteLine("Flüster " + Ver); return 0; }

    /// <summary><c>fluester bar</c>: zeigt die Flow-Leiste ein paar Sekunden in allen Zuständen – zum Prüfen, ob sie erscheint.</summary>
    public static int BarDemo()
    {
        Desktop.RepairEnvironment();
        using var bar = Overlay.TryCreate(out var why);
        if (bar == null) { Console.WriteLine("✗ Flow-Leiste nicht verfügbar: " + why + " – Flüster meldet sich dann über Benachrichtigungen."); return 1; }
        Console.WriteLine($"Flow-Leiste auf {bar.Where} – Probe läuft 7 Sekunden, schau unten in die Bildschirmmitte …");
        bar.Listening(false, false);
        var rnd = new Random(7);
        for (int i = 0; i < 60; i++) { bar.PushLevel((float)(0.15 + 0.8 * Math.Abs(Math.Sin(i * 0.31)) * (0.5 + 0.5 * rnd.NextDouble()))); Thread.Sleep(50); }
        if (bar.WindowState() is { } st) Log.Write($"Flow-Leiste Probe: sichtbar={st.Viewable}, ohne Fensterverwaltung={st.OverrideRedirect}, {st.W}×{st.H} bei {st.X},{st.Y}");
        bar.Processing(false);
        Thread.Sleep(1500);
        bar.Done("12 Wörter");
        Thread.Sleep(1500);
        bar.Error("Nur ein Test – alles gut");
        Thread.Sleep(1500);
        bar.Idle();
        Thread.Sleep(200);
        Console.WriteLine("✓ Fertig. Leiste nicht gesehen? Dann schick mir die Ausgabe von „fluester status“.");
        return 0;
    }

    /// <summary>Umgebung, die der Dienst braucht – eine systemd-Einheit erbt sie sonst nicht.</summary>
    static readonly string[] PassEnv =
    {
        "WAYLAND_DISPLAY", "DISPLAY", "XAUTHORITY", "XDG_RUNTIME_DIR", "XDG_SESSION_TYPE", "XDG_CURRENT_DESKTOP", "XDG_SESSION_DESKTOP",
        "DBUS_SESSION_BUS_ADDRESS", "HYPRLAND_INSTANCE_SIGNATURE", "SWAYSOCK", "PATH", "LANG", "LC_ALL", "HOME",
        "XDG_CONFIG_HOME", "XDG_DATA_HOME", "PULSE_SERVER", "FLUESTER_HOME", "FLUESTER_RECORDER",
    };

    public const string Unit = "fluester";

    /// <summary>Startet den Dienst losgelöst vom Terminal: als eigene systemd-Einheit (Schließen des Terminals beendet ihn nicht,
    /// nach einem Absturz startet er neu), sonst in eigener Sitzung (setsid).</summary>
    public static void StartDetached()
    {
        var self = Environment.ProcessPath!;
        var cmd = new List<string>();
        // Über „sg input“ starten, sobald der Nutzer in der Gruppe eingetragen ist: wirkt sofort (auch vor dem Neuanmelden)
        // und auch dann, wenn der systemd-Nutzerdienst noch mit den Gruppen von vor dem Eintragen läuft
        if (InputAccess.Registered && Libc.geteuid() != 0 && Desktop.Which("sg") is { } sg)
        {
            cmd.AddRange(new[] { sg, "input", "-c", $"exec '{self.Replace("'", "'\\''")}' run --background" });
            Log.Write("Start über „sg input“");
        }
        else cmd.AddRange(new[] { self, "run", "--background" });

        if (Desktop.Which("systemd-run") is { } sr && Desktop.Which("systemctl") is { } sc
            && Desktop.Run(sc, new[] { "--user", "show-environment" }, timeoutMs: 3000).Code == 0)
        {
            // Reste einer alten Einheit wegräumen (z. B. nach Absturz)
            Desktop.Run(sc, new[] { "--user", "stop", Unit + ".service" }, timeoutMs: 5000);
            Desktop.Run(sc, new[] { "--user", "reset-failed", Unit + ".service" }, timeoutMs: 3000);
            var args = new List<string> { "--user", "--unit=" + Unit, "--description=Flüster – Diktieren in jeder App", "--collect", "--quiet",
                "--property=Restart=on-failure", "--property=RestartSec=2" };
            foreach (var v in PassEnv)
                if (Environment.GetEnvironmentVariable(v) is { Length: > 0 } val) args.Add($"--setenv={v}={val}");
            args.Add("--");
            args.AddRange(cmd);
            var r = Desktop.Run(sr, args, timeoutMs: 10000);
            if (r.Code == 0) { Log.Write("Gestartet als systemd-Einheit " + Unit); return; }
            Log.Write("systemd-run ging nicht, starte direkt: " + (r.Err + r.Out).Trim());
        }

        var setsid = Desktop.Which("setsid");
        var psi = new ProcessStartInfo(setsid ?? cmd[0])
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var a in setsid != null ? cmd : cmd.Skip(1)) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }

    public static int Start()
    {
        if (Daemon.IsRunning()) { Console.WriteLine("Flüster läuft bereits."); return 0; }
        StartDetached();
        for (int i = 0; i < 20 && !Daemon.IsRunning(); i++) Thread.Sleep(150);
        Console.WriteLine(Daemon.Send("status") ?? "✗ Start fehlgeschlagen – „fluester run“ zeigt die Meldungen");
        return Daemon.IsRunning() ? 0 : 1;
    }

    public static int Remote(string cmd)
    {
        var reply = Daemon.Send(cmd);
        if (reply == null)
        {
            Console.WriteLine(cmd == "status" ? "Flüster läuft nicht. Starten: fluester start" : "✗ Flüster läuft nicht – „fluester start“");
            return cmd == "status" ? 3 : 1;
        }
        Console.WriteLine(reply);
        return 0;
    }

    public static async Task<int> Model(string[] args)
    {
        var sub = args.Length > 0 ? args[0] : "list";
        var store = new SettingsStore();
        if (sub == "list")
        {
            foreach (var m in WhisperModel.All)
                Console.WriteLine($"  {(store.Current.WhisperModelId == m.Id ? "●" : " ")} {m.Id,-9} {m.Name,-28} {m.SizeMB,5} MB  {(ModelStore.IsInstalled(m) ? "✓ geladen" : "")}");
            Console.WriteLine($"\nOrdner: {Paths.Models}");
            return 0;
        }
        if (args.Length < 2) throw new FluesterException("Modell-ID fehlt, z. B. „fluester model download turbo-q5“");
        var model = WhisperModel.All.FirstOrDefault(x => x.Id == args[1]) ?? throw new FluesterException("Unbekanntes Modell: " + args[1]);
        if (sub == "download")
        {
            await Download(model);
            store.Update(s => { s.WhisperModelId = model.Id; s.Engine = EngineChoice.WhisperLocal; });
            return 0;
        }
        if (sub == "delete") { ModelStore.Delete(model); Console.WriteLine("Gelöscht."); return 0; }
        throw new FluesterException("Unbekannt: " + sub);
    }

    public static async Task Download(WhisperModel model)
    {
        Console.WriteLine($"  Lade {model.Name} ({model.SizeMB} MB) …");
        var progress = new Progress<double>(p =>
        {
            int w = 30, done = (int)(p * w);
            Console.Write($"\r  [{new string('█', done)}{new string('░', w - done)}] {p:P0}   ");
        });
        await ModelStore.DownloadAsync(model, progress, CancellationToken.None);
        Console.WriteLine($"\r  ✓ {model.Name} geladen{new string(' ', 30)}");
    }

    public static int Key(string[] args)
    {
        if (args.Length < 1) throw new FluesterException("Anbieter fehlt: fluester key groq <schlüssel>");
        var secrets = new FileSecretStore();
        secrets.Set(args[0], args.Length > 1 ? args[1] : null);
        Console.WriteLine(args.Length > 1 ? "✓ Schlüssel gespeichert (nur für dich lesbar)" : "Schlüssel gelöscht");
        return 0;
    }

    public static int Config()
    {
        var store = new SettingsStore();
        if (!File.Exists(store.FilePath)) store.Update(_ => { });
        var editor = Environment.GetEnvironmentVariable("VISUAL") ?? Environment.GetEnvironmentVariable("EDITOR") ?? Desktop.Which("nano") ?? "vi";
        var p = Process.Start(new ProcessStartInfo(editor, store.FilePath) { UseShellExecute = false });
        p?.WaitForExit();
        Console.WriteLine("Gespeichert. Ein laufendes Flüster übernimmt die Änderungen sofort.");
        return 0;
    }

    public static int AutostartCmd(string[] args)
    {
        bool on = args.Length == 0 || args[0] is "on" or "an";
        Setup.Autostart(on);
        Console.WriteLine(on ? "✓ Autostart an" : "Autostart aus");
        return 0;
    }

    public static async Task<int> Transcribe(string[] args)
    {
        if (args.Length < 1) throw new FluesterException("Datei fehlt: fluester transcribe aufnahme.wav");
        var samples = AudioUtil.ReadWav(await File.ReadAllBytesAsync(args[0]));
        var store = new SettingsStore();
        var svc = new DictationService(new FileSecretStore());
        var sw = Stopwatch.StartNew();
        var result = await svc.ProcessAsync(samples, store.Current, TargetApp.Unknown, false, null, CancellationToken.None);
        Console.WriteLine(result == null ? "(keine Sprache erkannt)" : result.Text);
        Console.Error.WriteLine($"[{sw.Elapsed.TotalSeconds:0.0} s · {result?.Engine} · Laufzeit {WhisperLocalEngine.Backend ?? "-"}]");
        return 0;
    }

    public static async Task<int> MicTest()
    {
        var store = new SettingsStore();
        using var rec = new Recorder();
        var mic = Recorder.Resolve(store.Current.MicrophoneId, out bool missing);
        if (missing) Console.WriteLine($"! „{store.Current.MicrophoneId}“ ist nicht angeschlossen – nehme den Systemstandard.");
        Console.WriteLine($"Sprich jetzt 5 Sekunden lang … (Mikrofon: {Recorder.Describe(mic)}, Aufnahme mit {Recorder.Tool ?? "?"})");
        float peak = 0;
        rec.Level += l => peak = Math.Max(peak, l);
        rec.Start(mic);
        for (int i = 5; i > 0; i--) { Console.Write($"\r  {i} "); await Task.Delay(1000); }
        var samples = rec.Stop();
        if (rec.LastError != null) { Console.WriteLine("\r✗ Aufnahme fehlgeschlagen: " + rec.LastError + " – anderes Mikrofon: fluester mic"); return 1; }
        Console.WriteLine($"\r  {samples.Length / 16000.0:0.0} s aufgenommen, Pegel max. {(int)(peak * 100)} %");
        if (!AudioUtil.HasSpeech(samples, AudioUtil.SampleRate)) { Console.WriteLine("✗ Keine Sprache erkannt – Mikrofon stumm oder falsches gewählt? Wählen und Pegel prüfen: fluester mic"); return 1; }
        var svc = new DictationService(new FileSecretStore());
        var sw = Stopwatch.StartNew();
        var result = await svc.ProcessAsync(samples, store.Current, TargetApp.Unknown, false, null, CancellationToken.None);
        Console.WriteLine($"✓ „{result?.Text}“  ({sw.Elapsed.TotalSeconds:0.0} s, {result?.Engine}, Laufzeit {WhisperLocalEngine.Backend ?? "-"})");
        return 0;
    }
}
