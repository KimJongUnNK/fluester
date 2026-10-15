using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Fluester.Core;
using Fluester.Engine;

namespace Fluester.Linux;

/// <summary>Der Hintergrunddienst: Taste → Aufnahme → Erkennung → Bereinigung → Einfügen.
/// Alle Ereignisse laufen über eine Warteschlange auf einem Thread – keine Wettläufe zwischen Tastatur, Timer und Socket.</summary>
sealed class Daemon : IDisposable
{
    public static string SocketPath => Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } r ? r : Path.GetTempPath(),
        "fluester.sock");

    readonly SettingsStore store = new();
    readonly HistoryStore history = new();
    readonly FileSecretStore secrets = new();
    readonly DictationService service;
    readonly KeyboardWatcher watcher = new();
    readonly HotkeyTracker tracker = new();
    readonly HotkeyGestures gestures = new();
    readonly Recorder recorder = new();
    readonly VirtualKeyboard keyboard = new();
    readonly BlockingCollection<Action> queue = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    string? keyboardProblem;
    string? missingMicNotified;
    Overlay? bar;
    string barInfo = "";
    bool BarOk => bar is { Alive: true };
    readonly HashSet<string> problems = new();

    enum State { Idle, Recording, Processing }
    State state = State.Idle;
    bool isCommand;
    string? selection;
    TargetApp target = TargetApp.Unknown;
    CancellationTokenSource? work;
    FileSystemWatcher? settingsWatcher;
    Socket? server;

    public Daemon() => service = new DictationService(secrets);

    double Now => clock.Elapsed.TotalSeconds;
    void Post(Action a)
    {
        try { queue.Add(a); } catch (InvalidOperationException) { /* Dienst endet gerade */ }
    }

    public int Run()
    {
        Desktop.RepairEnvironment();
        if (IsRunning())
        {
            Console.WriteLine("Flüster läuft bereits.");
            return 0;
        }
        Log.Write($"Dienst startet · {Desktop.Session} · {Desktop.DesktopName}");
        Console.WriteLine($"Flüster läuft ({Desktop.Session}, {Desktop.DesktopName}). Beenden mit Strg+C.");

        tracker.Pressed += cmd => Post(() => Handle(gestures.Press(Now, cmd && store.Current.CommandModeEnabled)));
        tracker.Released += () => Post(() => Handle(gestures.Release(Now)));
        tracker.CommandModifier += () => Post(UpgradeToCommand);
        tracker.OtherKey += () => Post(gestures.OtherKeyPressed);
        tracker.Escape += () => Post(() => { if (state != State.Idle) Cancel(true); });
        watcher.Key += tracker.Feed;
        watcher.Problem += msg => Post(() => Problem(msg));
        ApplySettings(store.Current);
        store.Changed += s => Post(() => ApplySettings(s));
        WatchSettings();

        keyboardProblem = keyboard.Open();
        if (keyboardProblem != null) Problem(keyboardProblem + " – Text landet dann in der Zwischenablage");
        if (!Desktop.ClipboardAvailable) Problem("Zwischenablage-Werkzeug fehlt: " + Desktop.ClipboardTool);
        if (Recorder.Tool == null) Problem("Aufnahme-Werkzeug fehlt (parecord, pw-record oder arecord) – fluester setup");
        watcher.Start();
        StartSocket();
        Prewarm();
        // Flow-Leiste wie auf Mac/Windows; ohne X11/Xwayland bleiben Benachrichtigungen
        bar = Overlay.TryCreate(out var whyNoBar, store.Current.ShowIdleBar);
        barInfo = bar != null ? bar.Where : "aus – " + whyNoBar + " (Rückmeldung über Benachrichtigungen)";
        Log.Write("Flow-Leiste: " + barInfo);
        recorder.Level += l => bar?.PushLevel(l);
        bar?.Idle();

        var timer = new Timer(_ => Post(Tick), null, 50, 50);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Shutdown("Strg+C"); };
        // Terminal geschlossen: weiterlaufen. Beenden nur über „fluester stop“, SIGTERM oder Abmelden.
        using var hup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, c => { c.Cancel = true; Log.Write("SIGHUP ignoriert (Terminal geschlossen?)"); });
        using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => Log.Write("SIGTERM – beende"));
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Shutdown("Prozessende");
            try { recorder.Stop(); } catch { }
        };
        StartWatchdog();
        var s0 = store.Current;
        if (s0.Notifications) Desktop.Notify("Flüster ist bereit", $"{s0.Hotkey.Label(linux: true)} halten, sprechen, loslassen.");

        foreach (var action in queue.GetConsumingEnumerable())
        {
            busySince = clock.Elapsed.TotalSeconds;
            try { action(); }
            catch (Exception e) { Log.Write("Dienst: " + e); }
            busySince = -1;
        }
        timer.Dispose();
        Log.Write("Dienst beendet (" + (stopReason ?? "?") + ")");
        return 0;
    }

    string? stopReason;
    double busySince = -1;

    void Shutdown(string reason)
    {
        stopReason ??= reason;
        try { queue.CompleteAdding(); } catch { }
    }

    double processingSince, processingLimit = double.MaxValue;

    /// <summary>Die Erkennung kommt nicht zurück (z. B. Grafikkarten-Treiber hängt): statt für immer „verarbeitet“ zu bleiben,
    /// auf den Prozessor umstellen (falls die Grafikkarte rechnete) und neu starten.</summary>
    void ProcessingStuck()
    {
        var s = store.Current;
        double waited = clock.Elapsed.TotalSeconds - processingSince;
        bool gpu = s.Engine == EngineChoice.WhisperLocal && s.UseGpu && WhisperLocalEngine.Backend is { } b && !b.StartsWith("Cpu");
        Log.Write($"Erkennung hängt seit {waited:0} s (Laufzeit {WhisperLocalEngine.Backend ?? "?"}) – {(gpu ? "schalte auf Prozessor um und " : "")}starte neu");
        if (gpu) store.Update(x => x.UseGpu = false);
        if (s.Notifications)
            Desktop.Notify("Flüster startet neu", gpu
                ? "Die Grafikkarte hing bei der Erkennung. Flüster rechnet ab jetzt mit dem Prozessor – gleich wieder bereit."
                : "Die Erkennung hing. Flüster ist gleich wieder bereit.", urgent: true);
        bar?.Error(gpu ? "Grafikkarte hing – Flüster startet neu (Prozessor)" : "Erkennung hing – Flüster startet neu", 3);
        Thread.Sleep(600);
        Restart(70);
    }

    /// <summary>Sauber neu starten: unter systemd übernimmt die Einheit den Neustart, sonst startet Flüster sich selbst.</summary>
    void Restart(int code)
    {
        try { recorder.Stop(); } catch { }
        try { server?.Dispose(); File.Delete(SocketPath); } catch { }
        if (Environment.GetEnvironmentVariable("INVOCATION_ID") == null) Cli.StartDetached();
        Environment.Exit(code);
    }

    /// <summary>Wächter: Hängt die Ereignis-Schleife (ein externes Werkzeug antwortet nicht), startet Flüster sich selbst neu,
    /// statt bis zum nächsten manuellen Start taub zu bleiben.</summary>
    void StartWatchdog()
    {
        new Thread(() =>
        {
            while (!queue.IsAddingCompleted)
            {
                Thread.Sleep(2000);
                var since = busySince;
                if (since < 0 || clock.Elapsed.TotalSeconds - since < 25) continue;
                Log.Write($"Wächter: Dienst hängt seit {clock.Elapsed.TotalSeconds - since:0} s (Zustand {state}) – starte neu");
                Restart(70);
            }
        }) { IsBackground = true, Name = "Wächter" }.Start();
    }

    void Problem(string msg)
    {
        if (!problems.Add(msg)) return;
        Console.Error.WriteLine("⚠ " + msg);
        Log.Write("Problem: " + msg);
        if (store.Current.Notifications) Desktop.Notify("Flüster: Problem", msg, urgent: true);
    }

    void ApplySettings(Settings s)
    {
        tracker.Choice = s.Hotkey;
        tracker.Reset();
        gestures.DoubleTapEnabled = s.HandsFreeDoubleTap;
        if (bar != null)
        {
            bar.ShowWhenIdle = s.ShowIdleBar;
            if (state == State.Idle && bar.Current is Overlay.Phase.Idle or Overlay.Phase.Hidden) bar.Idle();
        }
    }

    void WatchSettings()
    {
        try
        {
            // Alle Arten zu speichern: direkt (Editor), neu angelegt, oder per Umbenennen (fluester mic/model/setup schreiben atomar)
            settingsWatcher = new FileSystemWatcher(Paths.Config) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
            DateTime last = default;
            void OnChange(string? name)
            {
                if (name != "settings.json" || (DateTime.UtcNow - last).TotalMilliseconds < 300) return;
                last = DateTime.UtcNow;
                Thread.Sleep(100);
                Post(() => { store.Reload(); Prewarm(); });
            }
            settingsWatcher.Changed += (_, e) => OnChange(e.Name);
            settingsWatcher.Created += (_, e) => OnChange(e.Name);
            settingsWatcher.Renamed += (_, e) => OnChange(e.Name);
            settingsWatcher.EnableRaisingEvents = true;
        }
        catch (Exception e) { Log.Write("Einstellungen beobachten: " + e.Message); }
    }

    void Prewarm()
    {
        var s = store.Current;
        var model = WhisperModel.ById(s.WhisperModelId);
        if (s.Engine != EngineChoice.WhisperLocal) return;
        if (!ModelStore.IsInstalled(model)) { Problem($"Whisper-Modell „{model.Name}“ fehlt – fluester model download {model.Id}"); return; }
        // Inzwischen geladen (z. B. über das App-Fenster): alte Meldung weg
        problems.RemoveWhere(p => p.StartsWith("Whisper-Modell"));
        var engine = new WhisperLocalEngine(model, s.UseGpu);
        Task.Run(async () =>
        {
            try { await engine.PrewarmAsync(); Log.Write("Modell bereit, Laufzeit " + WhisperLocalEngine.Backend); }
            catch (Exception e) { Log.Write("Vorladen: " + e.Message); }
        });
    }

    void Tick()
    {
        var a = gestures.Tick(Now);
        if (a.Kind == HotkeyGestures.Kind.Cancel && state == State.Recording) TappedOnly();
        else if (a.Kind != HotkeyGestures.Kind.None) Handle(a);
        if (state == State.Recording && (DateTime.UtcNow - recorder.Started).TotalSeconds > 900) Finish();
        if (state == State.Processing && clock.Elapsed.TotalSeconds - processingSince > processingLimit) ProcessingStuck();
    }

    void Handle(HotkeyGestures.Action a)
    {
        switch (a.Kind)
        {
            case HotkeyGestures.Kind.StartHold: Begin(a.Command); break;
            case HotkeyGestures.Kind.StopAndProcess: Finish(); break;
            case HotkeyGestures.Kind.Cancel: Cancel(false); break;
            case HotkeyGestures.Kind.SwitchToHandsFree:
                if (state != State.Recording) break;
                Log.Write("Freihändig");
                if (BarOk) bar!.Listening(true, isCommand);
                else if (store.Current.Notifications) Desktop.Notify("🎙 Flüster hört zu (freihändig)", "Taste einmal tippen zum Beenden.");
                break;
        }
    }

    void Begin(bool command)
    {
        if (state != State.Idle)
        {
            if (state == State.Processing)
            {
                // Sichtbar machen statt still zu ignorieren
                gestures.Reset();
                double busy = clock.Elapsed.TotalSeconds - processingSince;
                Log.Write($"Taste während der Erkennung gedrückt (läuft seit {busy:0.0} s)");
                if (!BarOk && store.Current.Notifications) Desktop.Notify("Moment …", $"Das letzte Diktat wird noch erkannt ({busy:0} s).");
            }
            return;
        }
        target = Desktop.ActiveWindow();
        isCommand = command;
        selection = command ? Desktop.GetClipboard(primary: true) : null;
        try
        {
            var wanted = store.Current.MicrophoneId;
            var mic = Recorder.Resolve(wanted, out bool missing);
            if (missing && missingMicNotified != wanted)
            {
                missingMicNotified = wanted;
                Log.Write($"Mikrofon {wanted} nicht angeschlossen – nehme den Systemstandard");
                if (store.Current.Notifications) Desktop.Notify("Mikrofon nicht gefunden", "Das gewählte Mikrofon ist nicht angeschlossen. Flüster nimmt den Systemstandard.");
            }
            else if (!missing) missingMicNotified = null;
            recorder.Start(mic);
            Log.Write($"Aufnahme startet{(command ? " (Command Mode)" : "")} · {Recorder.Tool} · Mikrofon {(mic.Length == 0 ? "Systemstandard" : mic)} · Ziel {(target.Name.Length > 0 ? target.Name : "unbekannt")}");
        }
        catch (Exception e)
        {
            gestures.Reset();
            Fail(e.Message);
            return;
        }
        state = State.Recording;
        var s = store.Current;
        if (s.Sounds) Desktop.Sound("start");
        if (BarOk) bar!.Listening(false, command);
        else if (s.Notifications) Desktop.Notify(command ? "✨ Flüster hört zu (Command Mode)" : "🎙 Flüster hört zu", "Loslassen zum Beenden · doppelt tippen = freihändig");
    }

    /// <summary>Nur kurz getippt (kein Halten, kein zweiter Tipp): Aufnahme verwerfen und erklären, statt still nichts zu tun.</summary>
    void TappedOnly()
    {
        Cancel(false);
        Log.Write("Nur kurz getippt – Diktat verworfen");
        var s = store.Current;
        if (s.Sounds) Desktop.Sound("stop");
        var key = s.Hotkey.Label(linux: true);
        if (BarOk) bar!.Error($"{key} halten zum Sprechen · 2× tippen = freihändig", 3.5);
        else if (s.Notifications) Desktop.Notify("Taste halten zum Sprechen", $"{key} gedrückt halten, sprechen, loslassen. Doppelt tippen = freihändig.");
    }

    void UpgradeToCommand()
    {
        if (state != State.Recording || isCommand || !store.Current.CommandModeEnabled || (DateTime.UtcNow - recorder.Started).TotalSeconds > 1.5) return;
        isCommand = true;
        selection = Desktop.GetClipboard(primary: true);
        if (BarOk) bar!.Listening(gestures.State == HotkeyGestures.GestureState.HandsFree, true);
        else if (store.Current.Notifications) Desktop.Notify("✨ Command Mode", selection != null ? "Anweisung für den markierten Text sprechen." : "Was soll Flüster schreiben?");
    }

    void Finish()
    {
        if (state != State.Recording) return;
        var samples = recorder.Stop(tailMs: 150);
        var s = store.Current;
        if (s.Sounds) Desktop.Sound("stop");
        double seconds = samples.Length / (double)AudioUtil.SampleRate;
        if (seconds < 0.3 && recorder.LastError != null) { Fail("Aufnahme fehlgeschlagen: " + recorder.LastError + " – Mikrofon prüfen: fluester mic"); return; }
        if (seconds < 0.3) { state = State.Idle; Log.Write("Aufnahme verworfen (zu kurz)"); bar?.Idle(); return; }
        state = State.Processing;
        if (BarOk) bar!.Processing(isCommand);
        processingSince = clock.Elapsed.TotalSeconds;
        // Großzügig: lokales Whisper braucht auf langsamen Prozessoren etwa Echtzeit, Cloud hat eigene Zeitlimits
        processingLimit = (s.Engine == EngineChoice.Cloud ? 75 : 30 + 3 * seconds) + (s.Cleanup == CleanupChoice.Llm ? 15 : 0);
        if (double.TryParse(Environment.GetEnvironmentVariable("FLUESTER_PROCESSING_TIMEOUT"), System.Globalization.CultureInfo.InvariantCulture, out var t)) processingLimit = t; // Tests
        Log.Write($"Aufnahme beendet: {seconds:0.0} s · Erkennung läuft ({(s.Engine == EngineChoice.Cloud ? "Cloud" : $"Whisper {WhisperModel.ById(s.WhisperModelId).Id}, Laufzeit {WhisperLocalEngine.Backend ?? "lädt"}")})");
        bool cmd = isCommand;
        var sel = selection;
        var app = target;
        work?.Cancel();
        work = new CancellationTokenSource();
        var ct = work.Token;
        Task.Run(async () =>
        {
            try
            {
                var result = await service.ProcessAsync(samples, s, app, cmd, sel, ct);
                Post(() => Done(result, seconds, app, s, ct));
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Log.Write("Diktat: " + e);
                Post(() => Fail(e is FluesterException ? e.Message : "Fehler: " + e.Message));
            }
        });
    }

    void Done(DictationResult? result, double seconds, TargetApp app, Settings s, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        state = State.Idle;
        if (result == null || string.IsNullOrWhiteSpace(result.Text))
        {
            Log.Write($"Nichts verstanden ({seconds:0.0} s Audio)");
            if (BarOk) bar!.Error("Nichts verstanden – lauter oder näher ans Mikrofon?", 3);
            else if (s.Notifications) Desktop.Notify("Nichts verstanden", "");
            return;
        }
        bool inserted = Insert(result.Text, s, app);
        int words = result.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (BarOk) bar!.Done(inserted ? (words == 1 ? "1 Wort" : $"{words} Wörter") : "In der Zwischenablage – Strg+V", inserted ? 1.4 : 3.5);
        Log.Write($"Diktat fertig nach {clock.Elapsed.TotalSeconds - processingSince:0.0} s: {result.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length} Wörter · {(inserted ? "eingefügt" : "in der Zwischenablage")} · {result.Engine}");
        if (s.KeepHistory)
            history.Add(new HistoryItem { Raw = result.Raw, Text = result.Text, AppName = app.Name, AppId = app.Id, Seconds = seconds, IsCommand = result.IsCommand, Engine = result.Engine });
        if (result.Warning != null) Log.Write("Hinweis: " + result.Warning);
        if (!inserted && !BarOk && s.Notifications) Desktop.Notify("In der Zwischenablage", "Mit Strg+V einfügen.");
    }

    /// <summary>Text einfügen: Zwischenablage setzen, Strg+V (Terminals: Strg+Umschalt+V) über die virtuelle Tastatur, alte Zwischenablage zurück.</summary>
    bool Insert(string text, Settings s, TargetApp app)
    {
        if (s.InsertMethod == InsertMethod.Type && TryType(text)) return true;
        var backup = Desktop.GetClipboard();
        try { Desktop.SetClipboard(text); }
        catch (Exception e) { Fail(e.Message); return false; }
        Thread.Sleep(60);
        var keys = s.PasteShortcut switch
        {
            "ctrl+v" => new[] { Keys.LEFTCTRL, Keys.V },
            "ctrl+shift+v" => new[] { Keys.LEFTCTRL, Keys.LEFTSHIFT, Keys.V },
            "shift+insert" => new[] { Keys.LEFTSHIFT, Keys.INSERT },
            _ => Desktop.IsTerminal(app) ? new[] { Keys.LEFTCTRL, Keys.LEFTSHIFT, Keys.V } : new[] { Keys.LEFTCTRL, Keys.V },
        };
        bool sent = false;
        if (keyboardProblem == null)
        {
            try { keyboard.Chord(keys); sent = true; } catch (Exception e) { Log.Write("uinput: " + e.Message); }
        }
        if (!sent) sent = PasteWithTool(keys);
        if (sent && backup != null)
        {
            var restore = backup;
            Task.Delay(700).ContinueWith(_ => { try { Desktop.SetClipboard(restore); } catch { } });
        }
        return sent;
    }

    /// <summary>Ohne /dev/uinput: wtype (Wayland), xdotool (X11) oder ydotool.</summary>
    static bool PasteWithTool(ushort[] keys)
    {
        bool shift = keys.Contains(Keys.LEFTSHIFT);
        bool insert = keys.Contains(Keys.INSERT);
        try
        {
            if (Desktop.Wayland && Desktop.Which("wtype") is { } wt)
                return Desktop.Run(wt, insert ? new[] { "-M", "shift", "-k", "Insert", "-m", "shift" }
                    : shift ? new[] { "-M", "ctrl", "-M", "shift", "v", "-m", "shift", "-m", "ctrl" } : new[] { "-M", "ctrl", "v", "-m", "ctrl" }).Code == 0;
            if (Desktop.X11 && Desktop.Which("xdotool") is { } xd)
                return Desktop.Run(xd, new[] { "key", "--clearmodifiers", insert ? "shift+Insert" : shift ? "ctrl+shift+v" : "ctrl+v" }).Code == 0;
            if (Desktop.Which("ydotool") is { } yd)
                return Desktop.Run(yd, new[] { "key" }.Concat(keys.Select(k => $"{k}:1")).Concat(keys.Reverse().Select(k => $"{k}:0"))).Code == 0;
        }
        catch (Exception e) { Log.Write("Einfügen per Werkzeug: " + e.Message); }
        return false;
    }

    static bool TryType(string text)
    {
        try
        {
            if (Desktop.Wayland && Desktop.Which("wtype") is { } wt) return Desktop.Run(wt, new[] { "--", text }, timeoutMs: 20000).Code == 0;
            if (Desktop.X11 && Desktop.Which("xdotool") is { } xd) return Desktop.Run(xd, new[] { "type", "--clearmodifiers", "--delay", "4", "--", text }, timeoutMs: 20000).Code == 0;
        }
        catch { }
        return false;
    }

    void Fail(string message)
    {
        state = State.Idle;
        Log.Write("Fehler: " + message);
        Console.Error.WriteLine("✗ " + message);
        var s = store.Current;
        if (s.Sounds) Desktop.Sound("error");
        if (BarOk) bar!.Error(message, 5);
        // Lange Meldungen passen nicht in die Leiste – dann zusätzlich als Benachrichtigung
        if (s.Notifications && (!BarOk || message.Length > 60)) Desktop.Notify("Flüster", message, urgent: true);
    }

    void Cancel(bool explicitly)
    {
        if (state == State.Recording) recorder.Stop();
        work?.Cancel();
        gestures.Reset();
        bool was = state != State.Idle;
        state = State.Idle;
        if (BarOk) { if (explicitly && was) bar!.Error("Abgebrochen", 1.2); else bar!.Idle(); }
        else if (explicitly && was && store.Current.Notifications) Desktop.Notify("Abgebrochen", "");
    }

    // ───────────── Steuerung von außen: fluester toggle | status | cancel | paste-last | stop

    void StartSocket()
    {
        try
        {
            if (File.Exists(SocketPath)) File.Delete(SocketPath);
            server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            server.Bind(new UnixDomainSocketEndPoint(SocketPath));
            File.SetUnixFileMode(SocketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            server.Listen(4);
            new Thread(() =>
            {
                while (true)
                {
                    Socket client;
                    try { client = server.Accept(); } catch { return; }
                    try
                    {
                        var buf = new byte[256];
                        int n = client.Receive(buf);
                        var cmd = Encoding.UTF8.GetString(buf, 0, n).Trim();
                        var done = new ManualResetEventSlim();
                        string reply = "";
                        Post(() => { reply = Command(cmd); done.Set(); });
                        done.Wait(3000);
                        client.Send(Encoding.UTF8.GetBytes(reply + "\n"));
                    }
                    catch { }
                    finally { client.Dispose(); }
                }
            }) { IsBackground = true, Name = "Socket" }.Start();
        }
        catch (Exception e)
        {
            Log.Write("Socket: " + e.Message);
        }
    }

    string Command(string cmd)
    {
        switch (cmd)
        {
            case "toggle":
                if (state == State.Recording) { gestures.Reset(); Finish(); return "beendet"; }
                if (state != State.Idle) return "beschäftigt";
                double t = Now;
                gestures.Reset();
                gestures.Press(t, false); gestures.Release(t + 0.1); gestures.Press(t + 0.2, false); gestures.Release(t + 0.25);
                Begin(false);
                return state == State.Recording ? "hört zu (nochmal toggle zum Beenden)" : "Start fehlgeschlagen";
            case "cancel":
                Cancel(true);
                return "abgebrochen";
            case "paste-last":
                var last = history.Last;
                if (last == null) return "kein Diktat im Verlauf";
                return Insert(last.Text, store.Current, Desktop.ActiveWindow()) ? "eingefügt" : "in der Zwischenablage";
            case "stop":
                queue.CompleteAdding();
                return "beendet";
            case "status-json":
                // Für das App-Fenster: derselbe Stand wie „status“, maschinenlesbar
                var cur = store.Current;
                return JsonSerializer.Serialize(new DaemonStatus
                {
                    State = state switch { State.Recording => "recording", State.Processing => "processing", _ => "idle" },
                    BusySeconds = state == State.Processing ? Math.Round(clock.Elapsed.TotalSeconds - processingSince, 1) : 0,
                    Pid = Environment.ProcessId,
                    Version = typeof(Daemon).Assembly.GetName().Version?.ToString(3) ?? "dev",
                    Hotkey = cur.Hotkey.Label(linux: true),
                    Microphone = MicDescription(cur.MicrophoneId),
                    Bar = bar is { Alive: false } ? "Verbindung zur Anzeige verloren – Flüster neu starten" : barInfo,
                    BarOk = BarOk,
                    Backend = WhisperLocalEngine.Backend,
                    KeyboardOk = keyboardProblem == null,
                    Problems = problems.ToArray(),
                }, DaemonStatus.Json);
            default:
                var s = store.Current;
                var engine = s.Engine == EngineChoice.WhisperLocal
                    ? $"{s.Engine.Label()} · {WhisperModel.ById(s.WhisperModelId).Name} · Laufzeit {WhisperLocalEngine.Backend ?? "noch nicht geladen"}"
                    : s.Engine.Label();
                return $"""
                    Flüster läuft · {(state switch { State.Recording => "hört zu", State.Processing => $"erkennt seit {clock.Elapsed.TotalSeconds - processingSince:0} s", _ => "bereit" })} · PID {Environment.ProcessId}
                      Taste:     {s.Hotkey.Label(linux: true)}
                      Mikrofon:  {Recorder.Describe(s.MicrophoneId)}
                      Leiste:    {(bar is { Alive: false } ? "Verbindung zur Anzeige verloren – fluester stop; fluester start" : barInfo)}
                      Erkennung: {engine}
                    """ + (problems.Count > 0 ? "\nProbleme:\n  " + string.Join("\n  ", problems) : "");
        }
    }

    (string Id, string Text, DateTime At)? micCache;

    /// <summary>Name des Mikrofons, höchstens alle 5 s neu ermittelt – das App-Fenster fragt den Stand laufend ab.</summary>
    string MicDescription(string id)
    {
        if (micCache is { } c && c.Id == id && (DateTime.UtcNow - c.At).TotalSeconds < 5) return c.Text;
        var text = Recorder.Describe(id);
        micCache = (id, text, DateTime.UtcNow);
        return text;
    }

    public static bool IsRunning() => Send("ping") != null;

    /// <summary>Befehl an den laufenden Dienst; null = läuft nicht. Liest bis der Dienst die Verbindung schließt.</summary>
    public static string? Send(string cmd, int timeoutMs = 4000)
    {
        if (!File.Exists(SocketPath)) return null;
        try
        {
            using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            s.Connect(new UnixDomainSocketEndPoint(SocketPath));
            s.Send(Encoding.UTF8.GetBytes(cmd));
            s.ReceiveTimeout = timeoutMs;
            using var all = new MemoryStream();
            var buf = new byte[4096];
            int n;
            while ((n = s.Receive(buf)) > 0) all.Write(buf, 0, n);
            return all.Length == 0 ? null : Encoding.UTF8.GetString(all.ToArray()).Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Stand des Dienstes für das App-Fenster; null = läuft nicht.</summary>
    public static DaemonStatus? Status(int timeoutMs = 2500)
    {
        var json = Send("status-json", timeoutMs);
        if (json == null || !json.StartsWith('{')) return null;
        try { return JsonSerializer.Deserialize<DaemonStatus>(json, DaemonStatus.Json); }
        catch (JsonException) { return null; }
    }

    public void Dispose()
    {
        bar?.Dispose();
        watcher.Dispose();
        recorder.Dispose();
        keyboard.Dispose();
        settingsWatcher?.Dispose();
        server?.Dispose();
        try { File.Delete(SocketPath); } catch { }
    }
}

/// <summary>Antwort auf „status-json“.</summary>
sealed class DaemonStatus
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>idle | recording | processing</summary>
    public string State { get; set; } = "idle";
    public double BusySeconds { get; set; }
    public int Pid { get; set; }
    public string Version { get; set; } = "";
    public string Hotkey { get; set; } = "";
    public string Microphone { get; set; } = "";
    public string Bar { get; set; } = "";
    public bool BarOk { get; set; }
    public string? Backend { get; set; }
    public bool KeyboardOk { get; set; }
    public string[] Problems { get; set; } = Array.Empty<string>();

    public string StateLabel => State switch
    {
        "recording" => "hört zu",
        "processing" => $"erkennt ({BusySeconds:0} s)",
        _ => "bereit",
    };
}
