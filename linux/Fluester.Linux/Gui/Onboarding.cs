using Fluester.Core;
using Fluester.Engine;

namespace Fluester.Linux.Gui;

/// <summary>Einrichtung beim ersten Start – wie am Mac: Willkommen, Rechte, Mikrofon, Taste, Erkennung, Ausprobieren.</summary>
sealed class Onboarding
{
    public readonly Adw.Window Window;
    readonly MainWindow main;
    readonly Action closed;
    readonly Gtk.Stack stack = Gtk.Stack.New();
    readonly Adw.WindowTitle title = Adw.WindowTitle.New("Einrichtung", "");
    readonly Gtk.Button back, next;
    readonly LevelMeter meter = new();
    readonly (string Name, Func<Gtk.Widget> Build)[] steps;
    Gtk.Switch? autostart;
    int index = -1;

    public int Count => steps.Length;
    public int Index => index;

    public Onboarding(MainWindow main, Action closed)
    {
        this.main = main;
        this.closed = closed;
        steps = new (string, Func<Gtk.Widget>)[]
        {
            ("Willkommen", Welcome),
            ("Berechtigungen", Rights),
            ("Mikrofon", Microphone),
            ("Diktat-Taste", Hotkey),
            ("Erkennung", Recognition),
            ("Ausprobieren", TryIt),
        };
        Window = Adw.Window.New();
        Window.Title = "Willkommen bei Flüster";
        Window.SetDefaultSize(700, 760);
        Window.SetSizeRequest(560, 560);
        Window.TransientFor = main.Window;
        Window.Modal = true;

        var header = Adw.HeaderBar.New();
        header.TitleWidget = title;
        stack.TransitionType = Gtk.StackTransitionType.SlideLeftRight;
        stack.TransitionDuration = 180;
        stack.Vexpand = true;
        back = Ui.Button("Zurück", () => Go(index - 1));
        next = Ui.Button("Weiter", () => { if (index == steps.Length - 1) Finish(); else Go(index + 1); }, "suggested-action", "pill");
        back.AddCssClass("pill");
        var spacer = Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
        spacer.Hexpand = true;
        var bottom = Ui.Margins(Ui.HBox(12, back, spacer, next), 18);
        Window.Content = Ui.VBox(0, header, stack, Gtk.Separator.New(Gtk.Orientation.Horizontal), bottom);
        Window.OnCloseRequest += (_, _) => { meter.Dispose(); closed(); return false; };
        Go(0);
    }

    public void Go(int i)
    {
        if (i < 0 || i >= steps.Length) return;
        meter.Stop();
        var old = stack.GetVisibleChild();
        stack.TransitionType = i >= index ? Gtk.StackTransitionType.SlideLeft : Gtk.StackTransitionType.SlideRight;
        index = i;
        var page = Wrap(steps[i].Name, steps[i].Build());
        stack.AddNamed(page, "s" + i + "-" + Environment.TickCount64);
        stack.SetVisibleChild(page);
        if (old != null) Ui.After(400, () => stack.Remove(old));
        title.Subtitle = $"Schritt {i + 1} von {steps.Length} · {steps[i].Name}";
        back.Visible = i > 0;
        next.Label = i == 0 ? "Los geht's" : i == steps.Length - 1 ? "Fertig" : "Weiter";
    }

    Gtk.Widget Wrap(string name, Gtk.Widget content)
    {
        var scroll = Gtk.ScrolledWindow.New();
        scroll.HscrollbarPolicy = Gtk.PolicyType.Never;
        var clamp = Adw.Clamp.New();
        clamp.MaximumSize = 580;
        clamp.Child = Ui.Margins(content, 24);
        scroll.Child = clamp;
        return scroll;
    }

    static Gtk.Box Head(string step, string heading, string text)
    {
        var s = Ui.Label(step.ToUpperInvariant(), "fl-step", "accent");
        var h = Ui.Label(heading, "title-1");
        var t = Ui.Label(text, "dim-label");
        return Ui.VBox(6, s, h, t);
    }

    static Gtk.ListBox List(params Gtk.Widget[] rows)
    {
        var l = Gtk.ListBox.New();
        l.AddCssClass("boxed-list");
        l.SelectionMode = Gtk.SelectionMode.None;
        foreach (var r in rows) l.Append(r);
        return l;
    }

    // ───────────── Schritte

    Gtk.Widget Welcome()
    {
        var icon = Ui.Icon(Integration.AppId, 128);
        icon.MarginTop = 12;
        var h = Ui.Label("Willkommen bei Flüster", "fl-hero");
        h.Halign = Gtk.Align.Center;
        var t = Ui.Label("Halten. Sprechen. Loslassen. In jeder App – kostenlos und auf Wunsch komplett offline.", "dim-label");
        t.Halign = Gtk.Align.Center;
        t.Justify = Gtk.Justification.Center;
        var key = main.Store.Current.Hotkey.Label(linux: true);
        Adw.ActionRow Feature(string icon, string color, string title, string sub)
        {
            var r = Ui.Row(title, sub);
            r.AddPrefix(Ui.Tile(icon, color));
            return r;
        }
        var list = List(
            Feature("audio-input-microphone-symbolic", "blue", $"{key} halten und sprechen", "Loslassen – der Text steht dort, wo der Cursor blinkt."),
            Feature("media-playback-start-symbolic", "green", "Doppelt tippen = freihändig", "Für längere Texte. Einmal tippen beendet."),
            Feature("starred-symbolic", "purple", "+ Umschalt = Command Mode", "Markierten Text per Sprache umschreiben lassen."));
        list.MarginTop = 18;
        return Ui.VBox(10, icon, h, t, list);
    }

    Gtk.Widget Rights()
    {
        var box = Ui.VBox(18, Head("Schritt 2", "Berechtigungen",
            "Damit Flüster die Diktat-Taste sieht und Text einfügen kann – unter X11 und Wayland. Einmal einrichten, fertig."));
        void Refresh() => Go(index);
        var rights = Pages.RightsGroup(main, Refresh);
        var tools = Pages.ToolsGroup(main, Refresh);
        box.Append(rights);
        box.Append(tools);
        var (g, u) = Setup.MissingRights();
        if (!g && !u && Setup.Tools().All(t => t.Found != null || !t.Required))
            box.Append(List(Ui.StatusRow("Alles bereit", "Weiter mit dem Mikrofon.", true)));
        return box;
    }

    Gtk.Widget Microphone()
    {
        var box = Ui.VBox(18, Head("Schritt 3", "Mikrofon", "Sprich ein paar Worte – der Balken muss deutlich ausschlagen."));
        var picker = new MicPicker(main.Store);
        var levelRow = Ui.Row("Pegel", "Hört zu …");
        levelRow.AddSuffix(meter.Bar);
        var mute = Ui.StatusRow("Mikrofon ist stummgeschaltet", null, false);
        mute.AddSuffix(Ui.Button("Einschalten", () =>
        {
            var err = LevelMeter.Unmute(picker.SelectedId);
            if (err == null) mute.Visible = false; else Ui.SetSubtitle(mute, err);
        }));
        mute.Visible = false;
        box.Append(List(picker.Row, levelRow, mute));
        box.Append(Ui.Label("Bleibt der Balken leer: anderes Mikrofon wählen oder in den Ton-Einstellungen die Stummschaltung aufheben.", "dim-label", "caption"));
        bool heard = false;
        void Listen()
        {
            // Mikrofone kommen verzögert – ist der Schritt schon verlassen, nicht mehr aufnehmen
            if (!main.Live || steps[index].Name != "Mikrofon") return;
            heard = false;
            meter.Start(main.Store.Current.MicrophoneId);
            Ui.Every(300, () =>
            {
                if (!meter.Running) return false;
                if (!heard && meter.Peak >= 0.2f) { heard = true; Ui.SetSubtitle(levelRow, "✓ Ton kommt an"); }
                return true;
            });
        }
        picker.Loaded += () => { mute.Visible = picker.SelectedMuted; Listen(); };
        picker.Changed += (_, muted) => { mute.Visible = muted; Ui.SetSubtitle(levelRow, "Hört zu …"); Listen(); };
        return box;
    }

    Gtk.Widget Hotkey()
    {
        var s = main.Store.Current;
        var box = Ui.VBox(18, Head("Schritt 4", "Diktat-Taste", "Halten = sprechen. Doppelt tippen = freihändig. + Umschalt = Command Mode."));
        box.Append(List(Ui.Combo("Taste", "Strg rechts stört beim Tippen am wenigsten.", Enum.GetValues<HotkeyChoice>(), h => h.Label(linux: true), s.Hotkey,
            v => main.Store.Update(x => x.Hotkey = v))));
        box.Append(Ui.Label("Unter Linux lässt sich die Taste nicht „schlucken“: die Feststelltaste schaltet weiter um, Alt rechts bleibt AltGr. Deshalb ist Strg rechts empfohlen.",
            "dim-label", "caption"));
        return box;
    }

    Gtk.Widget Recognition()
    {
        var s = main.Store.Current;
        var box = Ui.VBox(18, Head("Schritt 5", "Erkennung", "Whisper läuft auf deinem Rechner – offline und kostenlos. Die Cloud ist schneller, braucht aber einen Schlüssel."));
        box.Append(List(Ui.Combo("Erkennung", null, Enum.GetValues<EngineChoice>(), e => e.Label(), s.Engine, v =>
        {
            main.Store.Update(x => x.Engine = v);
            Go(index);
        })));
        var pages = new Pages(main);
        if (s.Engine == EngineChoice.WhisperLocal)
        {
            var g = Ui.Group("Modell laden", "Einmalig. Empfohlen: Large v3 Turbo. Ohne starke Grafikkarte: Small.");
            var rec = pages.BuildModelRows(m => m.Id is "turbo-q5" or "small");
            foreach (var r in rec) g.Add(r);
            box.Append(g);
            if (!WhisperModel.All.Any(ModelStore.IsInstalled))
                box.Append(Ui.Label("Ohne Modell erkennt Flüster nichts – einmal „Laden“ drücken (dauert je nach Leitung ein paar Minuten).", "dim-label", "caption"));
        }
        else
        {
            box.Append(pages.BuildProviderGroup(s.CloudTranscription, cfg => main.Store.Update(x => x.CloudTranscription = cfg)));
        }
        return box;
    }

    Gtk.Widget TryIt()
    {
        var s = main.Store.Current;
        var key = s.Hotkey.Label(linux: true);
        var box = Ui.VBox(18, Head("Schritt 6", "Ausprobieren", $"Klick ins Feld, halte {key}, sprich, lass los."));
        var serviceRow = Ui.Row("Flüster", main.Live ? "Startet …" : "Vorschau");
        var auto = Ui.Switch("Bei der Anmeldung starten", "Empfohlen – dann ist Flüster immer bereit.", true, _ => { }, out var sw);
        autostart = sw;
        var list = List(serviceRow, auto);
        box.Append(list);
        // Was das Ausprobieren verhindert (Modell fehlt, keine Tastatur …) – mit Weg zurück zum passenden Schritt
        var problemRows = new List<Gtk.Widget>();
        string shown = "";
        void ShowProblems(DaemonStatus? st)
        {
            var key2 = st == null ? "" : string.Join("|", st.Problems);
            if (key2 == shown) return;
            shown = key2;
            foreach (var r in problemRows) list.Remove(r);
            problemRows.Clear();
            if (st == null) return;
            foreach (var p in st.Problems)
            {
                var r = Ui.StatusRow(p, null, false);
                int target = p.StartsWith("Whisper-Modell") ? 4 : p.Contains("Mikrofon") || p.Contains("Aufnahme") ? 2 : 1;
                r.AddSuffix(Ui.Button("Zum Schritt", () => Go(target)));
                list.Append(r);
                problemRows.Add(r);
            }
        }
        var area = Ui.TextArea("", 120, out var framed);
        area.Buffer!.Text = "";
        box.Append(framed);
        var hint = Ui.Label("Die Leiste unten in der Bildschirmmitte zeigt, ob Flüster zuhört und schreibt.", "dim-label", "caption");
        box.Append(hint);
        if (main.Live)
        {
            Ui.Run(async () =>
            {
                // Neu starten, damit frisch eingerichtete Rechte (sg input) und alle Einstellungen gelten
                bool ok = Daemon.IsRunning() ? await Service.RestartAsync() : await Service.StartAsync();
                Ui.SetSubtitle(serviceRow, ok ? "Läuft – bereit zum Diktieren" : "Startet nicht – „fluester run“ im Terminal zeigt den Grund");
                main.Poll();
                Ui.After(300, () => area.GrabFocus());
            });
            Ui.Every(700, () =>
            {
                if (index != steps.Length - 1 || Window.GetVisible() == false) return false;
                var st = main.Status;
                if (st != null)
                    Ui.SetSubtitle(serviceRow, st.Problems.Length > 0 ? $"Läuft · {st.Problems.Length} Hinweis{(st.Problems.Length == 1 ? "" : "e")} – siehe unten" : $"Läuft · {st.StateLabel}");
                ShowProblems(st);
                return true;
            });
        }
        return box;
    }

    void Finish()
    {
        bool auto = autostart?.Active ?? true;
        if (main.Live)
        {
            Setup.Autostart(auto);
            main.Store.Update(x => { x.OnboardingDone = true; x.LaunchAtLogin = auto; });
        }
        Window.Close();
    }
}
