using System.Diagnostics;
using Fluester.Core;
using Fluester.Engine;

namespace Fluester.Linux;

/// <summary><c>fluester mic</c>: Mikrofone anzeigen, auswählen und mit Pegelanzeige testen.
/// Ein laufendes Flüster nimmt die Wahl ab dem nächsten Diktat.</summary>
static class Mic
{
    public static int Run(string[] args)
    {
        var store = new SettingsStore();
        var sub = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        switch (sub)
        {
            case "":
                return Choose(store, yes: false);
            case "list" or "liste" or "ls":
                Print(store.Current.MicrophoneId, Recorder.Devices());
                return 0;
            case "test":
                double seconds = args.Length > 2 && args[1] == "--seconds" && double.TryParse(args[2], System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 5;
                return LevelTest(store.Current.MicrophoneId, seconds) ? 0 : 1;
            default:
                var mics = Recorder.Devices();
                Set(store, Pick(mics, string.Join(' ', args)), mics);
                return 0;
        }
    }

    /// <summary>Liste mit Nummern; ● markiert die aktuelle Wahl. 0 = Systemstandard.</summary>
    public static void Print(string current, List<Microphone> mics)
    {
        Console.WriteLine($"Mikrofone (Aufnahme über {Recorder.Tool ?? "–"}):");
        var def = mics.FirstOrDefault(m => m.IsDefault);
        Console.WriteLine($"  {(current.Length == 0 ? "●" : " ")} 0  Systemstandard{(def != null ? " – gerade: " + def.Name : "")}");
        for (int i = 0; i < mics.Count; i++)
        {
            var m = mics[i];
            Console.WriteLine($"  {(m.Id == current ? "●" : " ")} {i + 1}  {m.Name}{(m.Muted ? "  · stummgeschaltet" : "")}");
        }
        if (current.Length > 0 && mics.Count > 0 && mics.All(m => m.Id != current))
            Console.WriteLine($"  ●    {current} – nicht angeschlossen, solange gilt der Systemstandard");
        if (mics.Count == 0)
            Console.WriteLine(Recorder.Tool == null
                ? "  Kein Aufnahme-Werkzeug gefunden – „fluester setup“ installiert es."
                : "  Keine einzelnen Mikrofone gefunden – Flüster nimmt den Systemstandard. Läuft PipeWire/PulseAudio? Prüfen: pactl list short sources");
    }

    /// <summary>Eingabe → Mikrofon (null = Systemstandard). Nummer, genaue Id oder ein Stück vom Namen.</summary>
    public static Microphone? Pick(List<Microphone> mics, string query)
    {
        query = query.Trim();
        if (query is "0" or "default" or "standard" or "systemstandard" or "auto") return null;
        var hits = MicrophoneParser.Matches(mics, query);
        if (hits.Count == 1) return hits[0];
        Print(new SettingsStore().Current.MicrophoneId, mics);
        throw new FluesterException(hits.Count == 0
            ? $"Kein Mikrofon „{query}“ – nimm eine Nummer aus der Liste"
            : $"„{query}“ passt auf {hits.Count} Mikrofone – nimm die Nummer");
    }

    static void Set(SettingsStore store, Microphone? mic, List<Microphone> mics)
    {
        store.Update(s => s.MicrophoneId = mic?.Id ?? "");
        Console.WriteLine($"✓ Mikrofon: {Recorder.Describe(mic?.Id ?? "", mics)}");
        if (mic is { Muted: true }) OfferUnmute(mic, ask: !Console.IsInputRedirected);
        if (Daemon.IsRunning()) Console.WriteLine("  Flüster nimmt es ab dem nächsten Diktat.");
    }

    /// <summary>Auswahl im Terminal (auch Schritt im Setup). Mit <paramref name="yes"/> bleibt die Wahl, wie sie ist.</summary>
    public static int Choose(SettingsStore store, bool yes)
    {
        var mics = Recorder.Devices();
        Print(store.Current.MicrophoneId, mics);
        if (yes || Console.IsInputRedirected || mics.Count == 0) return 0;
        while (true)
        {
            Console.Write("  Nummer wählen [Enter = so lassen]: ");
            var a = Console.ReadLine()?.Trim() ?? "";
            if (a.Length == 0) break;
            try { Set(store, Pick(mics, a), mics); break; }
            catch (FluesterException e) { Console.WriteLine("  " + e.Message); }
        }
        Console.Write("  Pegel testen? [J/n] ");
        var t = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(t) || t.StartsWith('j') || t.StartsWith('y')) LevelTest(store.Current.MicrophoneId, 5);
        return 0;
    }

    static void OfferUnmute(Microphone mic, bool ask)
    {
        Console.WriteLine("  ! Dieses Mikrofon ist stummgeschaltet.");
        if (!ask || Recorder.Tool != "parecord" || Desktop.Which("pactl") is not { } pactl) { Console.WriteLine("    In den Ton-Einstellungen einschalten."); return; }
        Console.Write("    Stummschaltung aufheben? [J/n] ");
        var a = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(a) && !a.StartsWith('j') && !a.StartsWith('y')) return;
        bool ok = Desktop.Run(pactl, new[] { "set-source-mute", mic.Id, "0" }).Code == 0;
        Console.WriteLine(ok ? "  ✓ Stummschaltung aufgehoben" : "  ✗ Ging nicht – bitte in den Ton-Einstellungen einschalten");
    }

    /// <summary>Nimmt ein paar Sekunden auf und zeigt den Pegel live. true = es kommt hörbar Ton an.</summary>
    public static bool LevelTest(string configured, double seconds)
    {
        var id = Recorder.Resolve(configured, out bool missing);
        if (missing) Console.WriteLine($"  ! „{configured}“ ist nicht angeschlossen – teste den Systemstandard.");
        bool live = !Console.IsOutputRedirected;
        Console.WriteLine($"Pegel von {Recorder.Describe(id)} – sprich ein paar Worte …");
        using var rec = new Recorder();
        float now = 0, peak = 0;
        rec.Level += l => { now = l; if (l > peak) peak = l; };
        rec.Start(id);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            Thread.Sleep(80);
            if (live) Console.Write($"\r  {Bar(now)} {(int)(now * 100),3} %   noch {Math.Max(0, seconds - sw.Elapsed.TotalSeconds):0} s ");
        }
        var samples = rec.Stop();
        if (live) Console.Write("\r" + new string(' ', 72) + "\r");
        if (rec.LastError != null) { Console.WriteLine("  ✗ Aufnahme fehlgeschlagen: " + rec.LastError); return false; }
        if (samples.Length == 0) { Console.WriteLine("  ✗ Vom Mikrofon kommen keine Daten."); return false; }
        bool ok = peak >= 0.2f;
        Console.WriteLine(ok
            ? $"  ✓ Ton kommt an (Spitze {(int)(peak * 100)} %). Sprache testen: fluester test"
            : $"  ✗ Kein Ton (Spitze {(int)(peak * 100)} %) – stummgeschaltet, falsches Mikrofon oder zu leise. Anderes wählen: fluester mic");
        return ok;
    }

    static string Bar(float level)
    {
        const int w = 30;
        int n = (int)Math.Round(Math.Clamp(level, 0, 1) * w);
        return "▕" + new string('█', n) + new string('░', w - n) + "▏";
    }
}
