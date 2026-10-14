using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
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
    void Post(Action a) => queue.Add(a);

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
        if (Recorder.Tool == null) Problem("Aufnahme-Werkzeug fehlt (parecord oder arecord)");
        watcher.Start();
        StartSocket();
        Prewarm();

        var timer = new Timer(_ => Post(Tick), null, 50, 50);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; queue.CompleteAdding(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { queue.CompleteAdding(); } catch { } };
        var s0 = store.Current;
        if (s0.Notifications) Desktop.Notify("Flüster ist bereit", $"{s0.Hotkey.Label(linux: true)} halten, sprechen, loslassen.");

        foreach (var action in queue.GetConsumingEnumerable())
        {
            try { action(); }
            catch (Exception e) { Log.Write("Dienst: " + e); }
        }
        timer.Dispose();
        Log.Write("Dienst beendet");
        return 0;
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
    }

    void WatchSettings()
    {
        try
        {
            settingsWatcher = new FileSystemWatcher(Paths.Config, "settings.json") { EnableRaisingEvents = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
            DateTime last = default;
            settingsWatcher.Changed += (_, _) =>
            {
                if ((DateTime.UtcNow - last).TotalMilliseconds < 300) return;
                last = DateTime.UtcNow;
                Thread.Sleep(100);
                Post(() => { store.Reload(); Prewarm(); });
            };
        }
        catch (Exception e) { Log.Write("Einstellungen beobachten: " + e.Message); }
    }

    void Prewarm()
    {
        var s = store.Current;
        var model = WhisperModel.ById(s.WhisperModelId);
        if (s.Engine != EngineChoice.WhisperLocal) return;
        if (!ModelStore.IsInstalled(model)) { Problem($"Whisper-Modell „{model.Name}“ fehlt – fluester model download {model.Id}"); return; }
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
        if (a.Kind != HotkeyGestures.Kind.None) Handle(a);
        if (state == State.Recording && (DateTime.UtcNow - recorder.Started).TotalSeconds > 900) Finish();
    }

    void Handle(HotkeyGestures.Action a)
    {
        switch (a.Kind)
        {
            case HotkeyGestures.Kind.StartHold: Begin(a.Command); break;
            case HotkeyGestures.Kind.StopAndProcess: Finish(); break;
            case HotkeyGestures.Kind.Cancel: Cancel(false); break;
            case HotkeyGestures.Kind.SwitchToHandsFree:
                if (state == State.Recording && store.Current.Notifications) Desktop.Notify("🎙 Flüster hört zu (freihändig)", "Taste einmal tippen zum Beenden.");
                break;
        }
    }

    void Begin(bool command)
    {
        if (state != State.Idle) { if (state == State.Processing) gestures.Reset(); return; }
        target = Desktop.ActiveWindow();
        isCommand = command;
        selection = command ? Desktop.GetClipboard(primary: true) : null;
        try
        {
            recorder.Start(store.Current.MicrophoneId);
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
    }

    void UpgradeToCommand()
    {
        if (state != State.Recording || isCommand || !store.Current.CommandModeEnabled || (DateTime.UtcNow - recorder.Started).TotalSeconds > 1.5) return;
        isCommand = true;
        selection = Desktop.GetClipboard(primary: true);
        if (store.Current.Notifications) Desktop.Notify("✨ Command Mode", selection != null ? "Anweisung für den markierten Text sprechen." : "Was soll Flüster schreiben?");
    }

    void Finish()
    {
        if (state != State.Recording) return;
        var samples = recorder.Stop();
        var s = store.Current;
        if (s.Sounds) Desktop.Sound("stop");
        double seconds = samples.Length / (double)AudioUtil.SampleRate;
        if (seconds < 0.3) { state = State.Idle; return; }
        state = State.Processing;
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
            if (s.Notifications) Desktop.Notify("Nichts verstanden", "");
            return;
        }
        bool inserted = Insert(result.Text, s, app);
        if (s.KeepHistory)
            history.Add(new HistoryItem { Raw = result.Raw, Text = result.Text, AppName = app.Name, AppId = app.Id, Seconds = seconds, IsCommand = result.IsCommand, Engine = result.Engine });
        if (result.Warning != null) Log.Write("Hinweis: " + result.Warning);
        if (!inserted && s.Notifications) Desktop.Notify("In der Zwischenablage", "Mit Strg+V einfügen.");
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
        if (s.Notifications) Desktop.Notify("Flüster", message, urgent: true);
    }

    void Cancel(bool explicitly)
    {
        if (state == State.Recording) recorder.Stop();
        work?.Cancel();
        gestures.Reset();
        bool was = state != State.Idle;
        state = State.Idle;
        if (explicitly && was && store.Current.Notifications) Desktop.Notify("Abgebrochen", "");
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
            default:
                var s = store.Current;
                return $"läuft · {state} · Taste: {s.Hotkey.Label(linux: true)} · Erkennung: {s.Engine.Label()} · Laufzeit: {WhisperLocalEngine.Backend ?? "noch nicht geladen"}"
                       + (problems.Count > 0 ? "\nProbleme:\n  " + string.Join("\n  ", problems) : "");
        }
    }

    public static bool IsRunning() => Send("ping") != null;

    /// <summary>Befehl an den laufenden Dienst; null = läuft nicht.</summary>
    public static string? Send(string cmd)
    {
        if (!File.Exists(SocketPath)) return null;
        try
        {
            using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            s.Connect(new UnixDomainSocketEndPoint(SocketPath));
            s.Send(Encoding.UTF8.GetBytes(cmd));
            s.ReceiveTimeout = 4000;
            var buf = new byte[4096];
            int n = s.Receive(buf);
            return Encoding.UTF8.GetString(buf, 0, n).Trim();
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        watcher.Dispose();
        recorder.Dispose();
        keyboard.Dispose();
        settingsWatcher?.Dispose();
        server?.Dispose();
        try { File.Delete(SocketPath); } catch { }
    }
}
