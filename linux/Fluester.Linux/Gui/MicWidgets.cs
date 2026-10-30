using Fluester.Engine;

namespace Fluester.Linux.Gui;

/// <summary>Mikrofon-Auswahl als Zeile. Die Geräte werden im Hintergrund gesucht (pw-dump kann dauern).</summary>
sealed class MicPicker
{
    public readonly Adw.ComboRow Row = Adw.ComboRow.New();
    readonly SettingsStore store;
    List<(string Id, string Name, string Label, bool Muted)> items = new();
    bool loading = true;

    /// <summary>Neue Wahl (Id, "" = Systemstandard) und ob das Gerät stummgeschaltet ist.</summary>
    public event Action<string, bool>? Changed;
    public event Action? Loaded;

    public MicPicker(SettingsStore store)
    {
        this.store = store;
        Row.UseMarkup = false;
        Row.Title = "Eingang";
        Row.Model = Gtk.StringList.New(new[] { "Mikrofone werden gesucht …" });
        Row.Sensitive = false;
        GObject.Object.NotifySignal.Connect(Row, (_, _) => Ui.Safe(() =>
        {
            if (loading || Row.Selected >= items.Count) return;
            var (id, name, _, muted) = items[(int)Row.Selected];
            if (id == store.Current.MicrophoneId) return;
            store.Update(s => { s.MicrophoneId = id; s.MicrophoneName = name; });
            Changed?.Invoke(id, muted);
        }), false, "selected");
        Ui.Run(Load);
    }

    public bool SelectedMuted => Row.Selected < items.Count && items[(int)Row.Selected].Muted;
    public string SelectedId => Row.Selected < items.Count ? items[(int)Row.Selected].Id : store.Current.MicrophoneId;

    async Task Load()
    {
        var mics = await Task.Run(Recorder.Devices);
        var s = store.Current;
        // Unter neuer Id (anderer USB-Port/Hub, anderes Profil) gleich wiederfinden und merken – sonst „nicht angeschlossen“
        var chosen = MicrophoneParser.Find(mics, s.MicrophoneId, s.MicrophoneName);
        if (chosen != null && (chosen.Id != s.MicrophoneId || chosen.Name != s.MicrophoneName))
            store.Update(x => { x.MicrophoneId = chosen.Id; x.MicrophoneName = chosen.Name; });
        var current = chosen?.Id ?? s.MicrophoneId;
        var def = mics.FirstOrDefault(m => m.IsDefault);
        items = new() { ("", "", def != null ? $"Systemstandard ({def.Name})" : "Systemstandard", def?.Muted ?? false) };
        items.AddRange(mics.Select(m => (m.Id, m.Name, m.Name + (m.Muted ? " · stumm" : ""), m.Muted)));
        if (current.Length > 0 && chosen == null)
            items.Add((current, s.MicrophoneName, (s.MicrophoneName.Length > 0 ? s.MicrophoneName : current) + " – nicht angeschlossen", false));
        loading = true;
        Row.Model = Gtk.StringList.New(items.Select(i => i.Label).ToArray());
        Row.Selected = (uint)Math.Max(0, items.FindIndex(i => i.Id == current));
        Row.Subtitle = (mics.Count == 0
            ? Recorder.Tool == null ? "Kein Aufnahme-Werkzeug – siehe Berechtigungen" : "Keine einzelnen Mikrofone gefunden – Flüster nimmt den Systemstandard"
            : $"Aufnahme über {Recorder.Tool}");
        Row.Sensitive = true;
        loading = false;
        Loaded?.Invoke();
    }
}

/// <summary>Pegelanzeige: nimmt kurz (oder bis Stop) auf und zeigt die Lautstärke live. Spitze ≥ 20 % = Ton kommt an.</summary>
sealed class LevelMeter : IDisposable
{
    public readonly Gtk.LevelBar Bar = Gtk.LevelBar.NewForInterval(0, 1);
    Recorder? rec;
    volatile float now, peak;
    int generation;

    public float Peak => peak;
    public bool Running => rec != null;

    public LevelMeter()
    {
        Bar.Valign = Gtk.Align.Center;
        Bar.Hexpand = true;
        Bar.WidthRequest = 160;
        Bar.AddCssClass("fl-level");
        // Pegel ist schon in dB skaliert (−55 dB … −10 dB); normale Sprache liegt bei 0,5–0,8
        Bar.AddOffsetValue("low", 0.2);
        Bar.AddOffsetValue("high", 0.45);
        Bar.AddOffsetValue("full", 1.0);
    }

    /// <summary>Startet die Aufnahme. <paramref name="seconds"/> = 0: läuft bis <see cref="Stop"/>. Am Ende: Spitze und Fehler.</summary>
    public void Start(string configuredMic, string configuredName, double seconds = 0, Action<float, string?>? finished = null)
    {
        Stop();
        var id = Recorder.Resolve(configuredMic, configuredName, out _, out _);
        var r = new Recorder();
        now = 0;
        peak = 0;
        r.Level += l => { now = l; if (l > peak) peak = l; };
        r.Start(id);
        rec = r;
        int mine = ++generation;
        var started = DateTime.UtcNow;
        float shown = 0;
        Ui.Every(50, () =>
        {
            if (mine != generation) return false;
            // Abfallen langsamer als Ansteigen – ruhiger fürs Auge
            shown = Math.Max(now, shown * 0.85f);
            Bar.Value = Math.Clamp(shown, 0, 1);
            if (seconds <= 0 || (DateTime.UtcNow - started).TotalSeconds < seconds) return true;
            generation++;
            rec = null;
            Bar.Value = 0;
            Task.Run(() => { r.Stop(); return r.LastError; }).ContinueWith(t => Ui.Idle(() => finished?.Invoke(peak, t.Result)));
            return false;
        });
    }

    public void Stop()
    {
        generation++;
        var r = rec;
        rec = null;
        Bar.Value = 0;
        if (r != null) Task.Run(() => r.Stop());
    }

    public void Dispose() => Stop();

    public static string Verdict(float peak, string? error) => error != null
        ? "✗ Aufnahme fehlgeschlagen: " + error
        : peak >= 0.2f ? $"✓ Ton kommt an (Spitze {(int)(peak * 100)} %)"
        : $"✗ Kaum Ton (Spitze {(int)(peak * 100)} %) – stumm, falsches Mikrofon oder zu leise";

    /// <summary>Stummschaltung über pactl aufheben (PulseAudio/PipeWire). null = geklappt.</summary>
    public static string? Unmute(string id)
    {
        if (Desktop.Which("pactl") is not { } pactl) return "pactl fehlt – bitte in den Ton-Einstellungen einschalten";
        var target = id.Length > 0 ? id : "@DEFAULT_SOURCE@";
        return Desktop.Run(pactl, new[] { "set-source-mute", target, "0" }).Code == 0 ? null : "Ging nicht – bitte in den Ton-Einstellungen einschalten";
    }
}
