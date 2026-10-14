using System.Diagnostics;
using Fluester.Core;
using Fluester.Engine;

namespace Fluester.Linux;

/// <summary><c>fluester selftest</c>: prüft Regeln, Speicher, Taste (virtuelle Tastatur → evdev → Taste erkannt),
/// Zwischenablage und echte Spracherkennung. Umgebung: FLUESTER_SELFTEST_WAV, _EXPECT, _WHISPER (Modell-ID),
/// FLUESTER_SELFTEST_INPUT=1 (Taste ist Pflicht), FLUESTER_SELFTEST_CLIPBOARD=1 (Zwischenablage ist Pflicht).</summary>
static class SelfTest
{
    static bool ok = true;

    static void Log(string s) { Console.WriteLine(s); Engine.Log.Write("[Selbsttest] " + s); }

    static void Check(bool c, string what, bool required = true)
    {
        Log((c ? "  ✓ " : required ? "  ✗ " : "  ⚠ ") + what);
        if (!c && required) ok = false;
    }

    public static async Task<int> Run()
    {
        Log($"Flüster-Selbsttest (Linux) · {System.Runtime.InteropServices.RuntimeInformation.OSDescription} · {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture} · {Desktop.Session}");
        try
        {
            await Rules();
            Storage();
            Input(Environment.GetEnvironmentVariable("FLUESTER_SELFTEST_INPUT") == "1");
            Clipboard(Environment.GetEnvironmentVariable("FLUESTER_SELFTEST_CLIPBOARD") == "1");
            Log("Werkzeuge: Aufnahme " + (Recorder.Tool ?? "fehlt") + " · Zwischenablage " + Desktop.ClipboardTool);
            Microphones();
            await Speech();
        }
        catch (Exception e)
        {
            Log("✗ Unerwarteter Fehler: " + e);
            ok = false;
        }
        Log(ok ? "\n✅ SELBSTTEST OK" : "\n❌ SELBSTTEST FEHLGESCHLAGEN");
        return ok ? 0 : 1;
    }

    static async Task Rules()
    {
        Log("Regeln:");
        var svc = new DictationService(new FileSecretStore(Path.Combine(Path.GetTempPath(), "fluester-selftest-keys.json")));
        var (text, _) = await svc.CleanTextAsync("äh also wir treffen uns um 3, ich meine um 4 Uhr", WritingStyle.Formal, "Test", new Settings { Cleanup = CleanupChoice.Rules });
        Check(text == "Also wir treffen uns um 4 Uhr.", $"„{text}“");
    }

    static void Storage()
    {
        Log("Speicher:");
        var secrets = new FileSecretStore();
        var before = secrets.Get("selftest");
        secrets.Set("selftest", "geheim");
        var mode = File.GetUnixFileMode(Path.Combine(Paths.Config, "keys.json"));
        Check(secrets.Get("selftest") == "geheim", "API-Schlüssel speichern/lesen");
        Check(mode == (UnixFileMode.UserRead | UnixFileMode.UserWrite), $"Schlüsseldatei nur für den Nutzer lesbar ({mode})");
        secrets.Set("selftest", before);
    }

    static void Input(bool required)
    {
        Log("Diktat-Taste (virtuelle Tastatur → /dev/input → Taste erkannt):");
        Log("  Gruppe „input“: " + (Libc.geteuid() == 0 ? "nicht nötig (root)" : InputAccess.Active ? "aktiv"
            : InputAccess.Registered ? "eingetragen, aktiv nach dem nächsten Anmelden (bis dahin: fluester stop; fluester start)" : "fehlt – fluester setup"));
        using var vk = new VirtualKeyboard();
        var err = vk.Open();
        Check(err == null, err ?? "virtuelle Tastatur angelegt", required);
        if (err != null) return;
        Thread.Sleep(400);
        var mine = KeyboardWatcher.Keyboards(includeVirtual: true).FirstOrDefault(k => k.Name == KeyboardWatcher.VirtualKeyboardName);
        Check(mine != null, "als Eingabegerät sichtbar: " + (mine?.Path ?? "nein"), required);
        if (mine == null) return;
        using var watcher = new KeyboardWatcher { IncludeVirtual = true };
        var tracker = new HotkeyTracker { Choice = HotkeyChoice.RightCtrl };
        bool pressed = false, released = false, other = false;
        tracker.Pressed += _ => pressed = true;
        tracker.Released += () => released = true;
        tracker.OtherKey += () => other = true;
        watcher.Key += tracker.Feed;
        watcher.Problem += p => Log("  " + p);
        watcher.Start();
        Thread.Sleep(600);
        vk.Press(Keys.RIGHTCTRL, true);
        Thread.Sleep(300);
        vk.Press(Keys.RIGHTCTRL, false);
        Thread.Sleep(300);
        Check(pressed && released, "Strg rechts gedrückt/losgelassen erkannt", required);
        // Strg rechts + C → kein Diktat
        vk.Press(Keys.RIGHTCTRL, true);
        Thread.Sleep(100);
        vk.Chord(Keys.C);
        Thread.Sleep(200);
        vk.Press(Keys.RIGHTCTRL, false);
        Thread.Sleep(200);
        Check(other, "andere Taste beim Halten erkannt (→ kein Diktat)", required);
    }

    static void Clipboard(bool required)
    {
        Log("Zwischenablage:");
        if (!Desktop.ClipboardAvailable || (!Desktop.Wayland && !Desktop.X11))
        {
            Check(false, "nicht verfügbar (" + Desktop.Session + ", " + Desktop.ClipboardTool + ")", required);
            return;
        }
        Desktop.SetClipboard("Flüster äöü ß €");
        Thread.Sleep(200);
        var back = Desktop.GetClipboard();
        Check(back == "Flüster äöü ß €", $"setzen/lesen: „{back}“", required);
        Desktop.SetClipboard("primär", primary: true);
        Thread.Sleep(150);
        Check(Desktop.GetClipboard(primary: true) == "primär", "Primär-Auswahl (für Command Mode)", required);
    }

    /// <summary>Jedes Mikrofon kurz aufnehmen und den Pegel zeigen – zeigt, welches wirklich Ton liefert.</summary>
    static void Microphones()
    {
        var mics = Recorder.Devices();
        var current = new SettingsStore().Current.MicrophoneId;
        Log($"Mikrofone ({Recorder.Tool ?? "kein Aufnahme-Werkzeug"}) · gewählt: {Recorder.Describe(current, mics)}");
        if (mics.Count == 0) { Log("  keine einzeln auflistbar – Flüster nimmt den Systemstandard"); return; }
        foreach (var m in mics.Take(6))
        {
            using var rec = new Recorder();
            float peak = 0;
            rec.Level += l => { if (l > peak) peak = l; };
            rec.Start(m.Id);
            Thread.Sleep(700);
            var samples = rec.Stop();
            var state = rec.LastError != null ? "Fehler: " + rec.LastError : samples.Length == 0 ? "keine Daten" : $"Pegel {(int)(peak * 100)} %";
            Log($"  {(m.Id == current ? "●" : "·")} {m.Name}{(m.IsDefault ? " (Systemstandard)" : "")}{(m.Muted ? " · stummgeschaltet" : "")}: {state}");
        }
    }

    static async Task Speech()
    {
        var id = Environment.GetEnvironmentVariable("FLUESTER_SELFTEST_WHISPER");
        if (id == null) { Log("Whisper: übersprungen (FLUESTER_SELFTEST_WHISPER nicht gesetzt)"); return; }
        var model = WhisperModel.ById(id);
        Log($"Whisper ({model.Name}):");
        Check(ModelStore.IsInstalled(model), "Modell vorhanden: " + ModelStore.PathOf(model));
        if (!ModelStore.IsInstalled(model)) return;
        var samples = new List<(string Name, float[] Audio, string Expect, bool Required)>();
        var wav = Environment.GetEnvironmentVariable("FLUESTER_SELFTEST_WAV");
        if (wav != null && File.Exists(wav))
            samples.Add(("Datei", AudioUtil.ReadWav(File.ReadAllBytes(wav)), (Environment.GetEnvironmentVariable("FLUESTER_SELFTEST_EXPECT") ?? "").ToLowerInvariant(), true));
        if (Speak("Hello, this is a test of the dictation app.") is { } tts) samples.Add(("espeak", tts, "test", false));
        Check(samples.Count > 0, "Testaudio vorhanden");
        var engine = new WhisperLocalEngine(model, useGpu: true);
        foreach (var (name, audio, word, required) in samples)
        {
            var sw = Stopwatch.StartNew();
            var text = await engine.TranscribeAsync(AudioUtil.TrimSilence(audio, AudioUtil.SampleRate), "en", Array.Empty<string>());
            Log($"  {name} ({audio.Length / 16000.0:0.0} s): „{text}“ in {sw.Elapsed.TotalSeconds:0.0} s · Laufzeit {WhisperLocalEngine.Backend}");
            Check(word.Length == 0 || text.ToLowerInvariant().Contains(word), $"enthält „{word}“", required);
        }
        if (samples.Count == 0) return;
        var svc = new DictationService(new FileSecretStore());
        var settings = new Settings { WhisperModelId = model.Id, Language = "en-US", Cleanup = CleanupChoice.Rules };
        var result = await svc.ProcessAsync(samples[0].Audio, settings, new TargetApp("Thunderbird", "thunderbird"), false, null, CancellationToken.None);
        Log($"  Ganzer Ablauf (Stil E-Mail): „{result?.Text}“");
        Check(result is { Text.Length: > 0 }, "Diktat-Ablauf liefert Text");
        var sw2 = Stopwatch.StartNew();
        await engine.TranscribeAsync(samples[0].Audio, "en", Array.Empty<string>());
        Log($"  zweites Diktat: {sw2.Elapsed.TotalSeconds:0.00} s");
    }

    static float[]? Speak(string text)
    {
        var tool = Desktop.Which("espeak-ng") ?? Desktop.Which("espeak");
        if (tool == null) return null;
        var file = Path.Combine(Path.GetTempPath(), "fluester-selftest.wav");
        var r = Desktop.Run(tool, new[] { "-v", "en-us", "-s", "150", "-w", file, text }, timeoutMs: 10000);
        return r.Code == 0 && File.Exists(file) ? AudioUtil.ReadWav(File.ReadAllBytes(file)) : null;
    }
}
