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
                "run" => RunDaemon(),
                "setup" => Setup.Run(rest),
                "start" => Cli.Start(),
                "status" or "toggle" or "cancel" or "paste-last" or "stop" => Cli.Remote(cmd),
                "mic" or "mics" or "mikro" or "mikrofon" or "microphone" or "input" => Mic.Run(rest),
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

    static int RunDaemon()
    {
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

    public static void StartDetached()
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--background");
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
