using Fluester.Core;
using Fluester.Engine;

namespace Fluester.Windows;

enum DictationState { Idle, Recording, Processing }

/// <summary>Ablauf eines Diktats: Taste → Aufnahme → Erkennung → Bereinigung → Einfügen. Läuft auf dem UI-Thread.</summary>
sealed class DictationController : IDisposable
{
    readonly SettingsStore store;
    readonly HistoryStore history;
    readonly DictationService service;
    readonly KeyboardHook hook = new();
    readonly AudioRecorder recorder = new();
    readonly HotkeyGestures gestures = new();
    readonly System.Windows.Forms.Timer tick = new() { Interval = 50 };
    public readonly FlowBar Bar = new();

    public DictationState State { get; private set; } = DictationState.Idle;
    public string? LastError { get; private set; }
    public bool HotkeyActive => hook.IsInstalled;
    public event Action? StateChanged;

    bool isCommand;
    string? selection;
    ForegroundInfo target = new(IntPtr.Zero, IntPtr.Zero, "", "");
    CancellationTokenSource? work;
    IntPtr lastExternal;
    int tickCount;

    public DictationController(SettingsStore store, HistoryStore history, ISecretStore secrets)
    {
        this.store = store;
        this.history = history;
        service = new DictationService(secrets);
    }

    static double Now => KeyboardHook.Clock.Elapsed.TotalSeconds;

    public void Start()
    {
        hook.Pressed += cmd => Handle(gestures.Press(Now, cmd && store.Current.CommandModeEnabled));
        hook.Released += () => Handle(gestures.Release(Now));
        hook.CommandModifier += UpgradeToCommand;
        hook.OtherKey += () => gestures.OtherKeyPressed();
        hook.EscapePressed += () => Cancel(explicitly: true);
        hook.ShouldSwallowEscape = () => State != DictationState.Idle;
        recorder.Level += lv => Bar.PushLevel(lv);
        tick.Tick += (_, _) =>
        {
            var a = gestures.Tick(Now);
            if (a.Kind != HotkeyGestures.Kind.None) Handle(a);
            // Sicherheitsnetz: Aufnahmen über 15 Minuten beenden
            if (State == DictationState.Recording && recorder.Elapsed > 900) FinishRecording();
            // Letztes echtes App-Fenster merken (für Aktionen aus dem Tray-Menü)
            if (++tickCount % 8 == 0) RememberForeground();
        };
        tick.Start();
        ApplySettings(store.Current);
        store.Changed += s => ApplySettings(s);
        if (!hook.Install()) LastError = "Tastatur-Hook konnte nicht installiert werden";
        Bar.Idle();
        Prewarm();
    }

    void ApplySettings(Settings s)
    {
        hook.Choice = s.Hotkey;
        hook.Reset();
        gestures.DoubleTapEnabled = s.HandsFreeDoubleTap;
        Sounds.Enabled = s.Sounds;
        Bar.ShowWhenIdle = s.ShowIdleBar;
        if (State == DictationState.Idle) Bar.Idle();
        StateChanged?.Invoke();
    }

    /// <summary>Modell vorab laden, damit das erste Diktat schnell ist.</summary>
    public void Prewarm()
    {
        var s = store.Current;
        var model = WhisperModel.ById(s.WhisperModelId);
        if (s.Engine == EngineChoice.WhisperLocal && ModelStore.IsInstalled(model))
        {
            var engine = new WhisperLocalEngine(model, s.UseGpu);
            Task.Run(async () =>
            {
                try { await engine.PrewarmAsync(); }
                catch (Exception e) { Log.Write("Vorladen: " + e.Message); }
            });
        }
    }

    void RememberForeground()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == lastExternal || ForegroundInfo.IsShell(hwnd)) return;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return;
        lastExternal = hwnd;
    }

    /// <summary>Ziel für Aktionen aus dem Menü: das zuletzt aktive App-Fenster statt der Taskleiste.</summary>
    ForegroundInfo MenuTarget()
    {
        var now = ForegroundInfo.Current();
        if (now.Window != IntPtr.Zero && !now.IsSelf && !ForegroundInfo.IsShell(now.Window)) return now;
        return ForegroundInfo.Of(lastExternal);
    }

    void Handle(HotkeyGestures.Action a)
    {
        switch (a.Kind)
        {
            case HotkeyGestures.Kind.StartHold: BeginRecording(a.Command); break;
            case HotkeyGestures.Kind.StopAndProcess: FinishRecording(); break;
            case HotkeyGestures.Kind.Cancel: Cancel(explicitly: false); break;
            case HotkeyGestures.Kind.SwitchToHandsFree:
                if (State == DictationState.Recording) Bar.Listening(handsFree: true, isCommand);
                break;
        }
    }

    void BeginRecording(bool command, ForegroundInfo? fixedTarget = null)
    {
        if (State == DictationState.Processing) { gestures.Reset(); return; }
        if (State == DictationState.Recording) return;
        target = fixedTarget ?? ForegroundInfo.Current();
        isCommand = command;
        selection = command ? TextInserter.CopySelection() : null;
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
        Sounds.Start();
        State = DictationState.Recording;
        Bar.Listening(handsFree: false, command);
        StateChanged?.Invoke();
    }

    /// <summary>Umschalt kurz nach dem Start → Command Mode.</summary>
    void UpgradeToCommand()
    {
        if (State != DictationState.Recording || isCommand || !store.Current.CommandModeEnabled || recorder.Elapsed > 1.5) return;
        isCommand = true;
        selection = TextInserter.CopySelection();
        Bar.Listening(gestures.State == HotkeyGestures.GestureState.HandsFree, true);
    }

    async void FinishRecording()
    {
        if (State != DictationState.Recording) return;
        var samples = recorder.Stop();
        double seconds = samples.Length / (double)AudioUtil.SampleRate;
        Sounds.Stop();
        if (seconds < 0.3)
        {
            ToIdle();
            return;
        }
        State = DictationState.Processing;
        Bar.Processing(isCommand);
        StateChanged?.Invoke();
        var s = store.Current;
        var app = new TargetApp(target.DisplayName, target.ProcessName, target.Title);
        bool cmd = isCommand;
        var sel = selection;
        work?.Cancel();
        work = new CancellationTokenSource();
        var ct = work.Token;
        try
        {
            var result = await Task.Run(() => service.ProcessAsync(samples, s, app, cmd, sel, ct), ct);
            if (ct.IsCancellationRequested) return;
            if (result == null || string.IsNullOrWhiteSpace(result.Text))
            {
                State = DictationState.Idle;
                Bar.Error("Nichts verstanden", 1.6);
                StateChanged?.Invoke();
                return;
            }
            bool inField = TextInserter.Insert(result.Text, s.InsertMethod, s.SmartSpacing && !cmd, target);
            if (s.KeepHistory)
                history.Add(new HistoryItem
                {
                    Raw = result.Raw, Text = result.Text, AppName = app.Name, AppId = app.Id,
                    Seconds = seconds, IsCommand = cmd, Engine = result.Engine,
                });
            LastError = result.Warning;
            State = DictationState.Idle;
            if (inField) Bar.Done(cmd ? "Fertig" : $"{Stats.WordCount(result.Text)} Wörter", 0.9);
            else Bar.Done("In der Zwischenablage – Strg+V", 2.2);
            StateChanged?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Abgebrochen (Esc)
        }
        catch (Exception e)
        {
            Log.Write("Diktat: " + e);
            Fail(e is FluesterException ? e.Message : "Fehler: " + e.Message);
        }
    }

    void Fail(string message)
    {
        LastError = message;
        State = DictationState.Idle;
        Sounds.Error();
        Bar.Error(message, 4);
        StateChanged?.Invoke();
    }

    public void Cancel(bool explicitly)
    {
        if (State == DictationState.Recording) recorder.Stop();
        work?.Cancel();
        gestures.Reset();
        bool wasActive = State != DictationState.Idle;
        State = DictationState.Idle;
        if (explicitly && wasActive) Bar.Error("Abgebrochen", 0.9); else Bar.Idle();
        StateChanged?.Invoke();
    }

    void ToIdle()
    {
        State = DictationState.Idle;
        Bar.Idle();
        StateChanged?.Invoke();
    }

    /// <summary>Menü: freihändig starten bzw. beenden.</summary>
    public void ToggleFromMenu()
    {
        if (State == DictationState.Recording)
        {
            gestures.Reset();
            FinishRecording();
            return;
        }
        if (State != DictationState.Idle) return;
        // Wie ein Doppeltipp: danach beendet ein Tipp auf die Taste die Aufnahme
        double t = Now;
        gestures.Reset();
        gestures.Press(t, false);
        gestures.Release(t + 0.1);
        gestures.Press(t + 0.2, false);
        gestures.Release(t + 0.25);
        BeginRecording(false, MenuTarget());
        if (State == DictationState.Recording) Bar.Listening(handsFree: true, false);
    }

    public void PasteLast()
    {
        var last = history.Last;
        if (last == null) return;
        var s = store.Current;
        TextInserter.Insert(last.Text, s.InsertMethod, false, MenuTarget());
    }

    public void RestartHook()
    {
        hook.Dispose();
        hook.Install();
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        tick.Dispose();
        hook.Dispose();
        recorder.Dispose();
        Bar.Dispose();
        work?.Cancel();
    }
}
