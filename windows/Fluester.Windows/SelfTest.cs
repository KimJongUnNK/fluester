using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Fluester.Core;
using Fluester.Engine;

namespace Fluester.Windows;

/// <summary><c>Fluester.exe --selftest</c>: prüft ohne Klicks, ob Taste, Einfügen, Leiste, Schlüssel und Spracherkennung
/// wirklich funktionieren. Für die CI und zur Fehlersuche. Umgebungsvariablen:
/// FLUESTER_SELFTEST_WAV (Sprachdatei), FLUESTER_SELFTEST_EXPECT (Wort darin), FLUESTER_SELFTEST_WHISPER (Modell-ID),
/// FLUESTER_SELFTEST_INSERT=1 (Einfügen/Hook sind Pflicht).</summary>
static class SelfTest
{
    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int pid);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetStdHandle(int which);

    static readonly string? LogFile = Environment.GetEnvironmentVariable("FLUESTER_SELFTEST_LOG");

    static bool ok = true;

    static void Log(string s)
    {
        Console.WriteLine(s);
        Console.Out.Flush();
        if (LogFile != null) File.AppendAllText(LogFile, s + Environment.NewLine);
        Engine.Log.Write("[Selbsttest] " + s);
    }

    static void Check(bool condition, string what, bool required = true)
    {
        Log((condition ? "  ✓ " : required ? "  ✗ " : "  ⚠ ") + what);
        if (!condition && required) ok = false;
    }

    public static int Run()
    {
        // Aus einer Konsole gestartet, ohne Umleitung: Ausgabe dort anzeigen
        if (GetStdHandle(-11) == IntPtr.Zero) AttachConsole(-1);
        if (LogFile != null) File.WriteAllText(LogFile, "");
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        ApplicationConfiguration.Initialize();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var env = Environment.GetEnvironmentVariables();
        bool insertRequired = Environment.GetEnvironmentVariable("FLUESTER_SELFTEST_INSERT") == "1";
        Log($"Flüster-Selbsttest (Windows) · {Environment.OSVersion} · {RuntimeInformation.ProcessArchitecture}");
        try
        {
            RulesTest();
            BarTest();
            SecretsTest();
            AutostartTest();
            Log("Mikrofone: " + string.Join(", ", AudioRecorder.Devices().Select(d => d.Name).DefaultIfEmpty("keine")));
            HookTest(insertRequired);
            InsertTest(insertRequired);
            SpeechTest().GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            Log("✗ Unerwarteter Fehler: " + e);
            ok = false;
        }
        Log(ok ? "\n✅ SELBSTTEST OK" : "\n❌ SELBSTTEST FEHLGESCHLAGEN");
        return ok ? 0 : 1;
    }

    static void Pump(int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
    }

    static void RulesTest()
    {
        Log("Regeln:");
        var svc = new DictationService(new FileSecretStore(Path.Combine(Path.GetTempPath(), "fluester-selftest-keys.json")));
        var (text, _) = svc.CleanTextAsync("äh also wir treffen uns um 3, ich meine um 4 Uhr", WritingStyle.Formal, "Test", new Settings { Cleanup = CleanupChoice.Rules })
            .GetAwaiter().GetResult();
        Check(text == "Also wir treffen uns um 4 Uhr.", $"„{text}“");
    }

    static void BarTest()
    {
        Log("Flow-Leiste:");
        using var bar = new FlowBar();
        bar.Listening(false, false);
        for (int i = 0; i < 30; i++) bar.PushLevel(i % 10 / 10f);
        Pump(150);
        bar.Processing(false);
        Pump(100);
        bar.Done("5 Wörter");
        bar.Error("Testfehler");
        bar.Idle();
        Check(true, "alle Zustände gezeichnet");
    }

    static void SecretsTest()
    {
        Log("API-Schlüssel (DPAPI):");
        var store = new WinSecretStore();
        var before = store.Get("selftest");
        store.Set("selftest", "geheim-123");
        var file = Path.Combine(Paths.Config, "keys.dat");
        bool encrypted = !File.ReadAllText(file).Contains("geheim-123");
        Check(store.Get("selftest") == "geheim-123", "speichern und lesen");
        Check(encrypted, "Datei ist verschlüsselt");
        store.Set("selftest", before);
    }

    static void AutostartTest()
    {
        Log("Autostart:");
        bool was = Autostart.IsEnabled;
        Autostart.Set(true);
        bool on = Autostart.IsEnabled;
        Autostart.Set(was);
        Check(on && Autostart.IsEnabled == was, "Registry-Eintrag setzen und zurücksetzen");
    }

    static void HookTest(bool required)
    {
        Log("Tastatur-Hook:");
        using var hook = new KeyboardHook { Choice = HotkeyChoice.F8, AcceptInjected = true };
        bool installed = hook.Install();
        Check(installed, "installiert", required);
        if (!installed) return;
        bool pressed = false, released = false;
        hook.Pressed += _ => pressed = true;
        hook.Released += () => released = true;
        // Unmarkierte Eingabe (wie eine echte Taste)
        Native.SendInput(1, new[] { Untagged(Native.VK_F8, false) }, Marshal.SizeOf<Native.INPUT>());
        Pump(300);
        Native.SendInput(1, new[] { Untagged(Native.VK_F8, true) }, Marshal.SizeOf<Native.INPUT>());
        Pump(300);
        Check(pressed && released, "F8 gedrückt/losgelassen erkannt", required);
    }

    static Native.INPUT Untagged(ushort vk, bool up)
    {
        var i = Native.Key(vk, up);
        i.U.ki.dwExtraInfo = IntPtr.Zero;
        return i;
    }

    static void InsertTest(bool required)
    {
        Log("Einfügen in ein Textfeld:");
        using var form = new Form { Text = "Flüster-Test", Width = 500, Height = 200, StartPosition = FormStartPosition.CenterScreen, TopMost = true };
        var box = new TextBox { Dock = DockStyle.Fill, Multiline = true };
        form.Controls.Add(box);
        form.Show();
        form.Activate();
        Native.SetForegroundWindow(form.Handle);
        box.Focus();
        Pump(400);
        var target = ForegroundInfo.Of(form.Handle);

        TextInserter.SetClipboardText("alter Inhalt");
        TextInserter.Insert("Hallo Welt, äöü ß €.", InsertMethod.Paste, false, target, allowSelf: true);
        Pump(1200);
        Check(box.Text == "Hallo Welt, äöü ß €.", $"Strg+V: „{box.Text}“", required);
        Check(Clipboard.ContainsText() && Clipboard.GetText() == "alter Inhalt", "Zwischenablage wiederhergestellt", required);

        box.Clear();
        box.Focus();
        Pump(200);
        TextInserter.Insert("Zeile 1\nZeile 2", InsertMethod.Type, false, target, allowSelf: true);
        Pump(800);
        Check(box.Text.Replace("\r\n", "\n") == "Zeile 1\nZeile 2", $"Tippen: „{box.Text.Replace("\r\n", "⏎")}“", required);
        form.Close();
    }

    static async Task SpeechTest()
    {
        var wav = Environment.GetEnvironmentVariable("FLUESTER_SELFTEST_WAV");
        var expect = (Environment.GetEnvironmentVariable("FLUESTER_SELFTEST_EXPECT") ?? "").ToLowerInvariant();
        var modelId = Environment.GetEnvironmentVariable("FLUESTER_SELFTEST_WHISPER");
        if (modelId == null) { Log("Whisper: übersprungen (FLUESTER_SELFTEST_WHISPER nicht gesetzt)"); return; }
        var model = WhisperModel.ById(modelId);
        Log($"Whisper ({model.Name}):");
        Check(ModelStore.IsInstalled(model), "Modell vorhanden: " + ModelStore.PathOf(model));
        if (!ModelStore.IsInstalled(model)) return;

        var samples = new List<(string Name, float[] Audio, string Expect)>();
        if (wav != null && File.Exists(wav)) samples.Add(("Datei", AudioUtil.ReadWav(File.ReadAllBytes(wav)), expect));
        var tts = Synthesize("Hello, this is a test of the dictation app.");
        if (tts != null) samples.Add(("Windows-Sprachausgabe", tts, "test"));
        else Log("  ⚠ Windows-Sprachausgabe nicht verfügbar");
        Check(samples.Count > 0, "Testaudio vorhanden");

        var engine = new WhisperLocalEngine(model, useGpu: true);
        foreach (var (name, audio, word) in samples)
        {
            var sw = Stopwatch.StartNew();
            var text = await engine.TranscribeAsync(AudioUtil.TrimSilence(audio, AudioUtil.SampleRate), "en", Array.Empty<string>());
            Log($"  {name} ({audio.Length / 16000.0:0.0} s): „{text}“ in {sw.Elapsed.TotalSeconds:0.0} s · Laufzeit {WhisperLocalEngine.Backend}");
            Check(word.Length == 0 || text.ToLowerInvariant().Contains(word), $"enthält „{word}“", required: name == "Datei");
        }
        // Gesamter Ablauf wie beim echten Diktat
        var svc = new DictationService(new WinSecretStore());
        var settings = new Settings { WhisperModelId = model.Id, Language = "en-US", Cleanup = CleanupChoice.Rules };
        var result = await svc.ProcessAsync(samples[0].Audio, settings, new TargetApp("Outlook", "outlook"), false, null, default);
        Log($"  Ganzer Ablauf (Stil E-Mail): „{result?.Text}“");
        Check(result != null && result.Text.Length > 0, "Diktat-Ablauf liefert Text");
        var sw2 = Stopwatch.StartNew();
        await engine.TranscribeAsync(samples[0].Audio, "en", Array.Empty<string>());
        Log($"  zweites Diktat: {sw2.Elapsed.TotalSeconds:0.00} s");
    }

    /// <summary>Text mit der Windows-Sprachausgabe sprechen lassen (wenn Stimmen installiert sind).</summary>
    static float[]? Synthesize(string text)
    {
        try
        {
            var file = Path.Combine(Path.GetTempPath(), "fluester-selftest.wav");
            using (var synth = new System.Speech.Synthesis.SpeechSynthesizer())
            {
                if (synth.GetInstalledVoices().Count == 0) return null;
                var en = synth.GetInstalledVoices().FirstOrDefault(v => v.VoiceInfo.Culture.TwoLetterISOLanguageName == "en");
                if (en != null) synth.SelectVoice(en.VoiceInfo.Name);
                synth.SetOutputToWaveFile(file);
                synth.Speak(text);
            }
            return AudioUtil.ReadWav(File.ReadAllBytes(file));
        }
        catch (Exception e)
        {
            Log("  Sprachausgabe: " + e.Message);
            return null;
        }
    }
}
