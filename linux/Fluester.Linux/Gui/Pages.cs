using Fluester.Core;
using Fluester.Engine;

namespace Fluester.Linux.Gui;

/// <summary>Die Seiten des Fensters – gleiche Bereiche und Texte wie am Mac und unter Windows.</summary>
sealed partial class Pages : IDisposable
{
    readonly MainWindow w;
    readonly LevelMeter meter = new();

    public Pages(MainWindow w) => this.w = w;

    SettingsStore Store => w.Store;
    void Update(Action<Settings> change) => Store.Update(change);

    public Gtk.Widget Build(Page p) => p switch
    {
        Page.General => General(),
        Page.Recognition => Recognition(),
        Page.Cleanup => Cleanup(),
        Page.Styles => Styles(),
        Page.Dictionary => Dictionary(),
        Page.Snippets => Snippets(),
        Page.History => History(),
        Page.Rights => Rights(),
        _ => About(),
    };

    /// <summary>Seite wird sichtbar.</summary>
    public void Shown(Page p) { }

    /// <summary>Seite wird verlassen (bleibt aber gebaut).</summary>
    public void Hidden(Page p) { if (p == Page.General) meter.Stop(); }

    /// <summary>Seite wird abgebaut.</summary>
    public void Leaving(Page p) { if (p == Page.General) meter.Stop(); }

    public void Dispose() => meter.Dispose();

    // ───────────────────────── Allgemein

    Gtk.Widget General()
    {
        var s = Store.Current;
        var page = Ui.Page();

        // Dienst
        var service = Ui.Group("Flüster", "Diktiert wird im Hintergrund – das Fenster darf geschlossen sein.");
        Gtk.Switch? runSwitch = null;
        bool syncing = false;
        var runRow = Ui.Switch("Diktieren aktiv", null, w.Status != null, on => { if (!syncing) ToggleService(on); });
        runSwitch = (Gtk.Switch)runRow.ActivatableWidget!;
        service.Add(runRow);
        var barRow = Ui.Row("Flow-Leiste", "–");
        service.Add(barRow);
        var problemRows = new List<Gtk.Widget>();
        w.OnStatus(() =>
        {
            var st = w.Status;
            syncing = true;
            runSwitch.Active = st != null;
            syncing = false;
            Ui.SetSubtitle(runRow, st == null
                ? (w.Live ? "Aus – einschalten, dann Taste halten und sprechen" : "Vorschau")
                : $"{char.ToUpper(st.StateLabel[0])}{st.StateLabel[1..]} · {st.Hotkey} halten, sprechen, loslassen");
            Ui.SetSubtitle(barRow, st?.Bar is { Length: > 0 } bar ? bar : "zeigt sich beim Diktieren unten in der Bildschirmmitte");
            foreach (var r in problemRows) service.Remove(r);
            problemRows.Clear();
            if (st == null) return;
            foreach (var problem in st.Problems)
            {
                var r = Ui.StatusRow(problem, null, false);
                if (problem.Contains("input") || problem.Contains("uinput") || problem.Contains("Tastatur") || problem.Contains("Werkzeug") || problem.Contains("Zwischenablage"))
                    r.AddSuffix(Ui.Button("Beheben …", () => w.Select(Page.Rights)));
                else if (problem.Contains("Modell"))
                    r.AddSuffix(Ui.Button("Modell laden …", () => w.Select(Page.Recognition)));
                service.Add(r);
                problemRows.Add(r);
            }
        });
        page.Add(service);

        // Taste
        var key = Ui.Group("Diktat-Taste");
        key.Add(Ui.Combo("Taste", "Halten zum Sprechen. Strg rechts stört beim Tippen am wenigsten.",
            Enum.GetValues<HotkeyChoice>(), h => h.Label(linux: true), s.Hotkey, v => Update(x => x.Hotkey = v)));
        key.Add(Ui.Switch("Doppeltipp: freihändig diktieren", "Zweimal kurz tippen startet, einmal tippen beendet.",
            s.HandsFreeDoubleTap, v => Update(x => x.HandsFreeDoubleTap = v)));
        key.Add(Ui.Switch("Command Mode", "Text markieren, Taste + Umschalt halten, „mach das förmlicher“ sagen. Braucht ein Sprachmodell (KI-Bereinigung).",
            s.CommandModeEnabled, v => Update(x => x.CommandModeEnabled = v)));
        page.Add(key);

        // Mikrofon
        var mic = Ui.Group("Mikrofon");
        var picker = new MicPicker(Store);
        mic.Add(picker.Row);
        var levelRow = Ui.Row("Pegel", "Testen und ein paar Worte sprechen");
        levelRow.AddSuffix(meter.Bar);
        Gtk.Button? test = null;
        test = Ui.Button("Testen", () =>
        {
            test!.Sensitive = false;
            Ui.SetSubtitle(levelRow, "Sprich jetzt ein paar Worte …");
            meter.Start(Store.Current.MicrophoneId, 4, (peak, err) =>
            {
                test.Sensitive = true;
                Ui.SetSubtitle(levelRow, LevelMeter.Verdict(peak, err));
            });
        });
        levelRow.AddSuffix(test);
        mic.Add(levelRow);
        var muteRow = Ui.StatusRow("Mikrofon ist stummgeschaltet", "Flüster würde nur Stille aufnehmen.", false);
        muteRow.AddSuffix(Ui.Button("Einschalten", () =>
        {
            var err = LevelMeter.Unmute(picker.SelectedId);
            w.Toast(err ?? "Stummschaltung aufgehoben");
            if (err == null) muteRow.Visible = false;
        }));
        muteRow.Visible = false;
        mic.Add(muteRow);
        picker.Loaded += () => muteRow.Visible = picker.SelectedMuted;
        picker.Changed += (_, muted) => { muteRow.Visible = muted; w.Toast("Flüster nimmt das Mikrofon ab dem nächsten Diktat"); };
        page.Add(mic);

        // Einfügen
        var insert = Ui.Group("Einfügen");
        insert.Add(Ui.Combo("Methode", "„Tippen“ braucht wtype (Wayland) oder xdotool (X11) und hilft bei Feldern, die nichts einfügen lassen.",
            Enum.GetValues<InsertMethod>(), m => m.Label(), s.InsertMethod, v => Update(x => x.InsertMethod = v)));
        var shortcuts = new[] { ("auto", "Automatisch (Terminals: Strg+Umschalt+V)"), ("ctrl+v", "Immer Strg+V"), ("ctrl+shift+v", "Immer Strg+Umschalt+V"), ("shift+insert", "Umschalt+Einfg") };
        insert.Add(Ui.Combo("Tastenkürzel", "Womit Flüster einfügt. Die alte Zwischenablage kommt danach zurück.",
            shortcuts, x => x.Item2, shortcuts.FirstOrDefault(x => x.Item1 == s.PasteShortcut, shortcuts[0]), v => Update(x => x.PasteShortcut = v.Item1)));
        page.Add(insert);

        // Rückmeldung und System
        var feedback = Ui.Group("Rückmeldung");
        feedback.Add(Ui.Switch("Töne beim Start und Ende", null, s.Sounds, v => Update(x => x.Sounds = v)));
        feedback.Add(Ui.Switch("Leiste auch im Ruhezustand zeigen", "Kleine Leiste unten in der Mitte, auch wenn Flüster nur wartet.",
            s.ShowIdleBar, v => Update(x => x.ShowIdleBar = v)));
        feedback.Add(Ui.Switch("Benachrichtigungen", "Wenn etwas fehlt – oder statt der Leiste, falls sie nicht angezeigt werden kann.",
            s.Notifications, v => Update(x => x.Notifications = v)));
        page.Add(feedback);

        var system = Ui.Group("System");
        system.Add(Ui.Switch("Bei der Anmeldung starten", "Flüster läuft dann immer im Hintergrund.", Setup.AutostartEnabled, v =>
        {
            Setup.Autostart(v);
            Update(x => x.LaunchAtLogin = v);
        }));
        system.Add(Ui.Switch("Verlauf speichern", "Nur auf diesem Rechner, nie in der Cloud.", s.KeepHistory, v => Update(x => x.KeepHistory = v)));
        page.Add(system);
        return page;
    }

    void ToggleService(bool on) => Ui.Run(async () =>
    {
        if (on == (w.Status != null)) return;
        if (on)
        {
            bool ok = await Service.StartAsync();
            w.Toast(ok ? "Flüster läuft – Taste halten und sprechen" : "Start fehlgeschlagen – Über Flüster › Protokoll zeigt den Grund");
        }
        else
        {
            await Service.StopAsync();
            w.Toast("Flüster ist aus – die Taste diktiert nicht mehr");
        }
        w.Poll();
    });

    // ───────────────────────── Erkennung

    Gtk.Widget Recognition()
    {
        var s = Store.Current;
        var main = Ui.Group("", "Wie aus Sprache Text wird. Whisper lokal ist kostenlos und offline, die Cloud am schnellsten.");
        main.Add(Ui.Combo("Erkennung", null, Enum.GetValues<EngineChoice>(), e => e.Label(), s.Engine, v =>
        {
            Update(x => x.Engine = v);
            w.Rebuild(Page.Recognition);
        }));
        main.Add(Ui.Combo("Sprache", "„Automatisch“ erkennt auch Deutsch-Englisch-Mix.", LanguageChoice.All, l => l.Name,
            LanguageChoice.All.FirstOrDefault(l => l.Code == s.Language) ?? LanguageChoice.All[0], v => Update(x => x.Language = v.Code)));
        var page = Ui.Page(main);

        if (s.Engine == EngineChoice.WhisperLocal)
        {
            var models = Ui.Group("Whisper-Modell", "Läuft komplett auf diesem Rechner. Ordner: " + Paths.Models);
            Gtk.CheckButton? leader = null;
            foreach (var m in WhisperModel.All) models.Add(ModelRow(m, ref leader));
            page.Add(models);

            var gpu = Ui.Group("Grafikkarte");
            var restart = Ui.Row("Gilt nach einem Neustart von Flüster");
            restart.AddSuffix(Ui.Button("Jetzt neu starten", () => Ui.Run(async () =>
            {
                restart.Sensitive = false;
                bool ok = await Service.RestartAsync();
                restart.Sensitive = true;
                restart.Visible = false;
                w.Toast(ok ? "Flüster neu gestartet" : "Neustart fehlgeschlagen");
                w.Poll();
            }), "suggested-action"));
            restart.Visible = false;
            var gpuRow = Ui.Switch("Grafikkarte nutzen (Vulkan)", null, s.UseGpu, v =>
            {
                Update(x => x.UseGpu = v);
                restart.Visible = w.Status != null;
            });
            w.OnStatus(() => Ui.SetSubtitle(gpuRow, "NVIDIA, AMD, Intel – deutlich schneller. " + w.Status switch
            {
                null => "",
                { Backend: null } => "Modell noch nicht geladen.",
                { Backend: "Vulkan" } => "Läuft gerade auf: Grafikkarte (Vulkan).",
                var st => $"Läuft gerade auf: Prozessor ({st.Backend}).",
            }));
            gpu.Add(gpuRow);
            gpu.Add(restart);
            page.Add(gpu);
        }
        else
        {
            page.Add(ProviderGroup("Anbieter", s.CloudTranscription, transcription: true, cfg => Update(x => x.CloudTranscription = cfg)));
            page.Add(Ui.Group("", "Groq hat ein großzügiges Gratis-Kontingent. Bei Cloud-Erkennung geht die Aufnahme an den Anbieter."));
        }
        return page;
    }

    /// <summary>Modell-Zeilen für die Einrichtung.</summary>
    public List<Adw.ActionRow> BuildModelRows(Func<WhisperModel, bool> filter)
    {
        Gtk.CheckButton? leader = null;
        var rows = new List<Adw.ActionRow>();
        foreach (var m in WhisperModel.All.Where(filter)) rows.Add(ModelRow(m, ref leader));
        return rows;
    }

    public Adw.PreferencesGroup BuildProviderGroup(ApiConfig cfg, Action<ApiConfig> changed) =>
        ProviderGroup("Anbieter", cfg, transcription: true, changed);

    /// <summary>Eine Zeile pro Whisper-Modell: auswählen, laden (mit Fortschritt), löschen.</summary>
    Adw.ActionRow ModelRow(WhisperModel m, ref Gtk.CheckButton? leader)
    {
        var row = Ui.Row(m.Name, $"{m.SizeMB} MB · {m.Note}");
        var pick = Gtk.CheckButton.New();
        if (leader != null) pick.SetGroup(leader); else leader = pick;
        pick.Valign = Gtk.Align.Center;
        pick.Active = Store.Current.WhisperModelId == m.Id;
        row.AddPrefix(pick);
        var progress = Gtk.ProgressBar.New();
        progress.Valign = Gtk.Align.Center;
        progress.WidthRequest = 110;
        progress.Visible = false;
        var status = Ui.Label("", "dim-label", "numeric");
        status.Wrap = false;
        status.Valign = Gtk.Align.Center;
        CancellationTokenSource? cts = null;
        Gtk.Button action = Gtk.Button.NewWithLabel("Laden");
        action.Valign = Gtk.Align.Center;
        bool armed = false;

        void Refresh()
        {
            bool installed = ModelStore.IsInstalled(m);
            pick.Sensitive = installed;
            status.SetText(cts != null ? status.GetText() : installed ? "✓ geladen" : "");
            action.Label = cts != null ? "Stopp" : installed ? "Löschen" : "Laden";
            action.RemoveCssClass("destructive-action");
            action.RemoveCssClass("suggested-action");
            if (!installed && cts == null && m.Id == "turbo-q5" && !WhisperModel.All.Any(ModelStore.IsInstalled)) action.AddCssClass("suggested-action");
        }

        GObject.Object.NotifySignal.Connect(pick, (_, _) => Ui.Safe(() =>
        {
            if (pick.Active && ModelStore.IsInstalled(m) && Store.Current.WhisperModelId != m.Id)
            {
                Update(x => x.WhisperModelId = m.Id);
                w.Toast($"{m.Name} – gilt ab dem nächsten Diktat");
            }
        }), false, "active");
        row.ActivatableWidget = pick;

        action.OnClicked += (_, _) => Ui.Run(async () =>
        {
            if (cts != null) { cts.Cancel(); return; }
            if (ModelStore.IsInstalled(m))
            {
                if (!armed)
                {
                    armed = true;
                    action.Label = "Wirklich löschen?";
                    action.AddCssClass("destructive-action");
                    Ui.After(4000, () => { if (armed) { armed = false; Refresh(); } });
                    return;
                }
                armed = false;
                ModelStore.Delete(m);
                w.Toast($"{m.Name} gelöscht ({m.SizeMB} MB frei)");
                Refresh();
                return;
            }
            cts = new CancellationTokenSource();
            progress.Fraction = 0;
            progress.Visible = true;
            status.SetText("0 %");
            Refresh();
            try
            {
                var token = cts.Token;
                var report = new Progress<double>(p => Ui.Idle(() => { progress.Fraction = p; if (cts != null) status.SetText($"{p * 100:0} %"); }));
                await Task.Run(() => ModelStore.DownloadAsync(m, report, token));
                Update(x => { x.WhisperModelId = m.Id; x.Engine = EngineChoice.WhisperLocal; });
                cts = null;
                Refresh();
                pick.Active = true;
                w.Toast($"{m.Name} geladen – Flüster nutzt es ab dem nächsten Diktat");
            }
            catch (OperationCanceledException) { w.Toast("Download abgebrochen"); }
            catch (Exception e) { w.Toast("Download fehlgeschlagen: " + e.Message); }
            finally
            {
                cts = null;
                progress.Visible = false;
                Refresh();
            }
        });
        row.AddSuffix(progress);
        row.AddSuffix(status);
        row.AddSuffix(action);
        Refresh();
        return row;
    }

    /// <summary>Anbieter, Adresse, Modell, API-Schlüssel, Verbindungstest.</summary>
    Adw.PreferencesGroup ProviderGroup(string title, ApiConfig initial, bool transcription, Action<ApiConfig> changed)
    {
        var cfg = new ApiConfig(initial.ProviderId, initial.BaseUrl, initial.Model);
        ApiConfig Copy() => new(cfg.ProviderId, cfg.BaseUrl, cfg.Model);
        var g = Ui.Group(title);
        var presets = transcription ? ProviderPreset.All.Where(p => p.Id != "ollama").ToArray() : ProviderPreset.All;
        Ui.TextField? url = null, model = null, key = null;
        Gtk.Button keyLink = Ui.Button("Schlüssel holen", () =>
        {
            var p = ProviderPreset.ById(cfg.ProviderId);
            if (p.KeyUrl.Length > 0) Ui.Open(p.KeyUrl);
        }, "flat");
        void UpdateLink()
        {
            var p = ProviderPreset.ById(cfg.ProviderId);
            keyLink.Visible = p.KeyUrl.Length > 0;
            keyLink.Label = p.NeedsKey ? "Schlüssel holen ↗" : "Herunterladen ↗";
        }
        g.Add(Ui.Combo("Anbieter", null, presets, p => p.Name, ProviderPreset.ById(cfg.ProviderId), p =>
        {
            cfg.ProviderId = p.Id;
            if (p.Id != "custom")
            {
                cfg.BaseUrl = p.BaseUrl;
                cfg.Model = transcription ? p.TranscriptionModel : p.ChatModel;
                url?.Set(cfg.BaseUrl);
                model?.Set(cfg.Model);
            }
            key?.Set(w.Secrets.Get(p.Id) ?? "");
            UpdateLink();
            changed(Copy());
        }));
        url = Ui.TextRow("Server-Adresse", cfg.BaseUrl, changed: t => { cfg.BaseUrl = t.Trim(); changed(Copy()); });
        model = Ui.TextRow("Modell", cfg.Model, changed: t => { cfg.Model = t.Trim(); changed(Copy()); });
        key = Ui.TextRow("API-Schlüssel", w.Secrets.Get(cfg.ProviderId) ?? "", password: true, changed: t => w.Secrets.Set(cfg.ProviderId, t));
        g.Add(url.Row);
        g.Add(model.Row);
        g.Add(key.Row);
        var testRow = Ui.Row("Verbindung testen", "Der Schlüssel liegt in ~/.config/fluester/keys.json, nur für dich lesbar.");
        Gtk.Button? test = null;
        test = Ui.Button("Testen", () => Ui.Run(async () =>
        {
            // Eingaben sofort übernehmen, nicht erst nach der Tipp-Pause
            w.Secrets.Set(cfg.ProviderId, key.Get());
            cfg.BaseUrl = url.Get().Trim();
            cfg.Model = model.Get().Trim();
            changed(Copy());
            test!.Sensitive = false;
            Ui.SetSubtitle(testRow, "Teste …");
            try
            {
                var c = Copy();
                var k = w.Secrets.Get(c.ProviderId);
                Ui.SetSubtitle(testRow, await Task.Run(() => DictationService.TestConnectionAsync(c, k, transcription)));
            }
            catch (Exception e) { Ui.SetSubtitle(testRow, "✗ " + e.Message); }
            finally { test.Sensitive = true; }
        }));
        testRow.AddSuffix(keyLink);
        testRow.AddSuffix(test);
        g.Add(testRow);
        UpdateLink();
        return g;
    }

    // ───────────────────────── KI-Bereinigung

    Gtk.Widget Cleanup()
    {
        var s = Store.Current;
        var main = Ui.Group("", "Füllwörter raus, Satzzeichen rein, Selbstkorrekturen übernehmen. Regeln laufen immer, ein Sprachmodell formuliert zusätzlich sauber.");
        main.Add(Ui.Combo("Bereinigung", null, Enum.GetValues<CleanupChoice>(), c => c.Label(), s.Cleanup, v =>
        {
            Update(x => x.Cleanup = v);
            w.Rebuild(Page.Cleanup);
        }));
        var page = Ui.Page(main);
        if (s.Cleanup == CleanupChoice.Llm)
        {
            page.Add(ProviderGroup("Sprachmodell", s.CloudChat, transcription: false, cfg => Update(x => x.CloudChat = cfg)));
            page.Add(Ui.Group("", "Ollama läuft komplett lokal: ollama.com laden, dann im Terminal „ollama pull qwen2.5:7b“. Fällt die KI aus, gelten automatisch die Regeln."));
        }
        var rules = Ui.Group("Regeln");
        rules.Add(Ui.Switch("Füllwörter und Stotterer entfernen", "äh, ähm, um, uh …", s.RemoveFillers, v => Update(x => x.RemoveFillers = v)));
        rules.Add(Ui.Switch("Selbstkorrekturen übernehmen", "„um 3, ich meine um 4“ → „um 4“", s.ApplySelfCorrections, v => Update(x => x.ApplySelfCorrections = v)));
        rules.Add(Ui.Switch("Aufzählungen als Liste", "„erstens …, zweitens …“", s.FormatLists, v => Update(x => x.FormatLists = v)));
        rules.Add(Ui.Switch("„Neue Zeile“ und „Neuer Absatz“ als Befehl", null, s.SpokenCommands, v => Update(x => x.SpokenCommands = v)));
        rules.Add(Ui.Switch("Satzzeichen sprechen", "„Komma“, „Fragezeichen“ …", s.SpokenPunctuation, v => Update(x => x.SpokenPunctuation = v)));
        page.Add(rules);

        var tryIt = Ui.Group("Ausprobieren", "Text eingeben, wie er gesprochen klingt – Flüster zeigt, was daraus wird.");
        var input = Ui.TextArea("äh also wir treffen uns um 3, ich meine um 4 Uhr", 70, out var framed);
        tryIt.Add(framed);
        var style = WritingStyle.Formal;
        var styleRow = Ui.Combo("Stil", null, WritingStyleInfo.All, x => x.Label(), style, v => style = v);
        var resultRow = Ui.Row("Ergebnis", "–");
        resultRow.SubtitleSelectable = true;
        Gtk.Button? run = null;
        run = Ui.Button("Bereinigen", () => Ui.Run(async () =>
        {
            run!.Sensitive = false;
            Ui.SetSubtitle(resultRow, "…");
            try
            {
                var text = input.Buffer!.Text ?? "";
                var settings = Store.Current;
                var st = style;
                var (clean, warning) = await Task.Run(() => new DictationService(w.Secrets).CleanTextAsync(text, st, "Test", settings));
                Ui.SetSubtitle(resultRow, clean + (warning != null ? "\n(" + warning + ")" : ""));
            }
            catch (Exception e) { Ui.SetSubtitle(resultRow, "✗ " + e.Message); }
            finally { run.Sensitive = true; }
        }), "suggested-action");
        styleRow.AddSuffix(run);
        var list = Gtk.ListBox.New();
        list.AddCssClass("boxed-list");
        list.SelectionMode = Gtk.SelectionMode.None;
        list.MarginTop = 12;
        list.Append(styleRow);
        list.Append(resultRow);
        tryIt.Add(list);
        page.Add(tryIt);
        return page;
    }

    // ───────────────────────── Stile

    Gtk.Widget Styles()
    {
        var s = Store.Current;
        var g = Ui.Group("", "Wie der Text klingen soll – je nach App. Flüster erkennt die App im Vordergrund " +
            "(unter X11, Sway und Hyprland; sonst gilt „Alle anderen Apps“).");
        foreach (var cat in AppCategoryInfo.All)
        {
            Adw.ComboRow? row = null;
            string Sub(WritingStyle st) => $"{cat.AppsHint()}\n„{st.Example()}“";
            row = Ui.Combo(cat.Label(), Sub(s.Styles.StyleFor(cat)), WritingStyleInfo.All, x => x.Label(), s.Styles.StyleFor(cat), v =>
            {
                Update(x => x.Styles.Set(v, cat));
                row!.Subtitle = Sub(v);
            });
            g.Add(row);
        }
        return Ui.Page(g, Ui.Group("", "Mit Sprachmodell wirkt der Stil am stärksten. Mit „Nur Regeln“ steuert er Großschreibung und den Punkt am Ende."));
    }
}
