using System.Drawing;
using System.Windows.Forms;
using Fluester.Core;
using Fluester.Engine;

namespace Fluester.Windows.Ui;

enum SettingsPage { General, Recognition, Cleanup, Styles, Dictionary, Snippets, History, About }

/// <summary>Einstellungen mit Seitenleiste – gleiche Bereiche wie am Mac.</summary>
sealed class SettingsForm : Form
{
    readonly SettingsStore store;
    readonly HistoryStore history;
    readonly ISecretStore secrets;
    readonly DictationController controller;
    readonly Panel content = new() { Dock = DockStyle.Fill };
    readonly FlowLayoutPanel nav;
    readonly Dictionary<SettingsPage, Button> navButtons = new();
    SettingsPage current;

    static readonly (SettingsPage Page, string Glyph, string Title)[] Pages =
    {
        (SettingsPage.General, "", "Allgemein"),
        (SettingsPage.Recognition, "", "Erkennung"),
        (SettingsPage.Cleanup, "", "KI-Bereinigung"),
        (SettingsPage.Styles, "", "Stile"),
        (SettingsPage.Dictionary, "", "Wörterbuch"),
        (SettingsPage.Snippets, "", "Textbausteine"),
        (SettingsPage.History, "", "Verlauf"),
        (SettingsPage.About, "", "Über Flüster"),
    };

    public SettingsForm(SettingsStore store, HistoryStore history, ISecretStore secrets, DictationController controller)
    {
        this.store = store;
        this.history = history;
        this.secrets = secrets;
        this.controller = controller;
        Text = "Flüster – Einstellungen";
        Icon = TrayApp.AppIcon;
        Size = new Size(1000, 720);
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Theme.Apply(this);

        nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Left, Width = 230, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            BackColor = Theme.Sidebar, Padding = new Padding(10, 18, 10, 10),
        };
        var brand = new Label { Text = "Flüster", Font = Theme.Heading, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(10, 0, 0, 16) };
        nav.Controls.Add(brand);
        foreach (var (page, glyph, title) in Pages)
        {
            var b = new Button
            {
                Text = "  " + title, Width = 208, Height = 38, TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat,
                ForeColor = Theme.Text, BackColor = Theme.Sidebar, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 0, 2),
                Image = GlyphImage(glyph), ImageAlign = ContentAlignment.MiddleLeft, TextImageRelation = TextImageRelation.ImageBeforeText,
                Padding = new Padding(8, 0, 0, 0),
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Theme.Selected;
            b.Click += (_, _) => Show(page);
            navButtons[page] = b;
            nav.Controls.Add(b);
        }
        Controls.Add(content);
        Controls.Add(nav);
        Show(SettingsPage.General);
    }

    static Bitmap GlyphImage(string glyph)
    {
        var bmp = new Bitmap(20, 20);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var f = new Font("Segoe MDL2 Assets", 11f);
        TextRenderer.DrawText(g, glyph, f, new Rectangle(0, 0, 20, 20), Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return bmp;
    }

    public void Show(SettingsPage page)
    {
        current = page;
        foreach (var (p, b) in navButtons) b.BackColor = p == page ? Theme.Selected : Theme.Sidebar;
        content.SuspendLayout();
        foreach (Control c in content.Controls) c.Dispose();
        content.Controls.Clear();
        var pb = new PageBuilder();
        switch (page)
        {
            case SettingsPage.General: GeneralPage(pb); break;
            case SettingsPage.Recognition: RecognitionPage(pb); break;
            case SettingsPage.Cleanup: CleanupPage(pb); break;
            case SettingsPage.Styles: StylesPage(pb); break;
            case SettingsPage.Dictionary: DictionaryPage(pb); break;
            case SettingsPage.Snippets: SnippetsPage(pb); break;
            case SettingsPage.History: HistoryPage(pb); break;
            case SettingsPage.About: AboutPage(pb); break;
        }
        content.Controls.Add(pb.Root);
        content.ResumeLayout();
    }

    void Update(Action<Settings> change) => store.Update(change);

    // ───────────────────────── Allgemein

    void GeneralPage(PageBuilder pb)
    {
        var s = store.Current;
        pb.Header("Allgemein", "Taste halten, sprechen, loslassen – der Text landet dort, wo der Cursor steht.");
        pb.Section("Diktat-Taste");
        var c = pb.Card();
        c.Row("Taste", Controls2.Combo(Enum.GetValues<HotkeyChoice>(), h => h.Label(), s.Hotkey, v => Update(x => x.Hotkey = v)),
            "Halten zum Sprechen. Strg rechts stört beim Tippen am wenigsten.");
        c.Check("Doppeltipp: freihändig diktieren (nochmal tippen zum Beenden)", s.HandsFreeDoubleTap, v => Update(x => x.HandsFreeDoubleTap = v));
        c.Check("Command Mode: Taste + Umschalt halten, markierten Text per Sprache bearbeiten", s.CommandModeEnabled, v => Update(x => x.CommandModeEnabled = v),
            "z. B. Text markieren → Strg rechts + Umschalt halten → „mach das förmlicher“. Braucht ein Sprachmodell (KI-Bereinigung).");
        c.Row("Status", new Label { Text = controller.HotkeyActive ? "✓ Taste aktiv" : "✗ Taste nicht aktiv – Flüster neu starten", AutoSize = true,
            ForeColor = controller.HotkeyActive ? Theme.Good : Theme.Warn });

        pb.Section("Einfügen");
        var ins = pb.Card();
        ins.Row("Methode", Controls2.Combo(Enum.GetValues<InsertMethod>(), m => m.Label(), s.InsertMethod, v => Update(x => x.InsertMethod = v), 340),
            "„Tippen“ hilft bei Feldern, die kein Strg+V annehmen.");
        ins.Check("Leerzeichen automatisch ergänzen (zwischen zwei Diktaten im selben Fenster)", s.SmartSpacing, v => Update(x => x.SmartSpacing = v));

        pb.Section("Mikrofon");
        var mic = pb.Card();
        var devices = AudioRecorder.Devices();
        var options = new List<(string Id, string Name)> { ("", "Standard (Windows-Einstellung)") };
        options.AddRange(devices.Select(d => (d.Id, d.Name + (d.IsDefault ? " · Standard" : ""))));
        mic.Row("Eingang", Controls2.Combo(options, o => o.Name, options.FirstOrDefault(o => o.Id == s.MicrophoneId), v => Update(x => x.MicrophoneId = v.Id), 340));
        var probe = new Label { AutoSize = true, ForeColor = Theme.Secondary };
        var test = Theme.SecondaryButton("Mikrofon prüfen");
        test.Click += (_, _) =>
        {
            var err = AudioRecorder.Probe(store.Current.MicrophoneId);
            probe.Text = err ?? "✓ Mikrofon funktioniert";
            probe.ForeColor = err == null ? Theme.Good : Theme.Warn;
        };
        mic.Row("Zugriff", Controls2.Line(test, probe), "Gesperrt? Windows-Einstellungen › Datenschutz › Mikrofon › Desktop-Apps erlauben.");

        pb.Section("Darstellung & System");
        var sys = pb.Card();
        sys.Check("Töne beim Start und Ende", s.Sounds, v => Update(x => x.Sounds = v));
        sys.Check("Leiste auch im Ruhezustand zeigen", s.ShowIdleBar, v => Update(x => x.ShowIdleBar = v));
        sys.Check("Verlauf speichern (nur lokal)", s.KeepHistory, v => Update(x => x.KeepHistory = v));
        sys.Check("Mit Windows starten", Autostart.IsEnabled, v => { Autostart.Set(v); Update(x => x.LaunchAtLogin = v); });
    }

    // ───────────────────────── Erkennung

    void RecognitionPage(PageBuilder pb)
    {
        var s = store.Current;
        pb.Header("Erkennung", "Wie aus Sprache Text wird. Whisper lokal ist kostenlos und offline, die Cloud am schnellsten.");
        var c = pb.Card();
        c.Row("Erkennung", Controls2.Combo(Enum.GetValues<EngineChoice>(), e => e.Label(), s.Engine, v => { Update(x => x.Engine = v); controller.Prewarm(); Show(current); }, 300));
        c.Row("Sprache", Controls2.Combo(LanguageChoice.All, l => l.Name, LanguageChoice.All.FirstOrDefault(l => l.Code == s.Language) ?? LanguageChoice.All[0],
            v => Update(x => x.Language = v.Code), 300), "„Automatisch“ erkennt auch Deutsch-Englisch-Mix.");

        if (s.Engine == EngineChoice.WhisperLocal)
        {
            pb.Section("Whisper-Modell");
            var models = pb.Card();
            foreach (var m in WhisperModel.All) models.Full(new ModelRow(m, store, () => { controller.Prewarm(); }));
            pb.Note("Modelle liegen in " + Paths.Models + ". Das erste Diktat nach dem Start lädt das Modell in den Speicher.");
            var gpu = pb.Card();
            gpu.Check("Grafikkarte nutzen (Vulkan – NVIDIA, AMD, Intel)", s.UseGpu, v => Update(x => x.UseGpu = v),
                "Deutlich schneller. Änderung gilt nach dem Neustart von Flüster. " +
                (WhisperLocalEngine.Backend is { } b ? $"Läuft gerade auf: {(b == "Vulkan" ? "Grafikkarte (Vulkan)" : "Prozessor (" + b + ")")}." : ""));
        }
        else
        {
            pb.Section("Anbieter");
            pb.Add(new ProviderEditor(s.CloudTranscription, secrets, transcription: true, cfg => Update(x => x.CloudTranscription = cfg)));
            pb.Note("Groq hat ein großzügiges Gratis-Kontingent. Bei Cloud-Erkennung geht die Aufnahme an den Anbieter.");
        }
    }

    // ───────────────────────── KI-Bereinigung

    void CleanupPage(PageBuilder pb)
    {
        var s = store.Current;
        pb.Header("KI-Bereinigung", "Füllwörter raus, Satzzeichen rein, Selbstkorrekturen übernehmen. Regeln laufen immer, ein Sprachmodell formuliert zusätzlich sauber.");
        var c = pb.Card();
        c.Row("Bereinigung", Controls2.Combo(Enum.GetValues<CleanupChoice>(), e => e.Label(), s.Cleanup, v => { Update(x => x.Cleanup = v); Show(current); }, 320));
        if (s.Cleanup == CleanupChoice.Llm)
        {
            pb.Section("Sprachmodell");
            pb.Add(new ProviderEditor(s.CloudChat, secrets, transcription: false, cfg => Update(x => x.CloudChat = cfg)));
            pb.Note("Ollama läuft komplett lokal: ollama.com laden, dann im Terminal „ollama pull qwen2.5:7b“. Fällt die KI aus, gelten automatisch die Regeln.");
        }
        pb.Section("Regeln");
        var r = pb.Card();
        r.Check("Füllwörter und Stotterer entfernen (äh, ähm, um, uh …)", s.RemoveFillers, v => Update(x => x.RemoveFillers = v));
        r.Check("Selbstkorrekturen übernehmen („um 3, ich meine um 4“ → „um 4“)", s.ApplySelfCorrections, v => Update(x => x.ApplySelfCorrections = v));
        r.Check("Aufzählungen als Liste formatieren („erstens …, zweitens …“)", s.FormatLists, v => Update(x => x.FormatLists = v));
        r.Check("„Neue Zeile“ und „Neuer Absatz“ als Befehl", s.SpokenCommands, v => Update(x => x.SpokenCommands = v));
        r.Check("Satzzeichen sprechen („Komma“, „Fragezeichen“ …)", s.SpokenPunctuation, v => Update(x => x.SpokenPunctuation = v));

        pb.Section("Ausprobieren");
        var t = pb.Card();
        var input = new TextBox { Multiline = true, Height = 64, Text = "äh also wir treffen uns um 3, ich meine um 4 Uhr", BackColor = Theme.Window, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
        var output = new Label { AutoSize = true, MaximumSize = new Size(700, 0), ForeColor = Theme.Text, Font = Theme.Bold };
        var styleBox = Controls2.Combo(WritingStyleInfo.All, w => w.Label(), WritingStyle.Formal, _ => { }, 160);
        var run = Theme.PrimaryButton("Bereinigen");
        run.Click += async (_, _) =>
        {
            run.Enabled = false;
            output.Text = "…";
            try
            {
                var svc = new DictationService(secrets);
                var (text, warning) = await svc.CleanTextAsync(input.Text, WritingStyleInfo.All[styleBox.SelectedIndex], "Test", store.Current);
                output.Text = text + (warning != null ? "\n(" + warning + ")" : "");
            }
            catch (Exception e) { output.Text = e.Message; }
            run.Enabled = true;
        };
        t.Full(input);
        t.Full(Controls2.Line(run, styleBox));
        t.Full(output);
    }

    // ───────────────────────── Stile

    void StylesPage(PageBuilder pb)
    {
        var s = store.Current;
        pb.Header("Stile", "Wie der Text klingen soll – je nach App. Flüster erkennt die App im Vordergrund.");
        var c = pb.Card();
        foreach (var cat in AppCategoryInfo.All)
        {
            var example = new Label { AutoSize = true, ForeColor = Theme.Secondary, Font = Theme.Small };
            var combo = Controls2.Combo(WritingStyleInfo.All, w => w.Label(), s.Styles.StyleFor(cat), v =>
            {
                Update(x => x.Styles.Set(v, cat));
                example.Text = "„" + v.Example() + "“";
            }, 200);
            example.Text = "„" + s.Styles.StyleFor(cat).Example() + "“";
            c.Row(cat.Label(), combo, cat.AppsHint());
            c.Full(example);
        }
        pb.Note("Mit Sprachmodell wirkt der Stil am stärksten. Mit „Nur Regeln“ steuert er Großschreibung und den Punkt am Ende.");
    }

    // ───────────────────────── Wörterbuch

    void DictionaryPage(PageBuilder pb)
    {
        pb.Header("Wörterbuch", "Namen, Marken, Fachbegriffe – immer richtig geschrieben. Bei „klingt wie“ steht, wie die Erkennung es falsch schreibt.");
        var grid = Grid(("Schreibweise", 40), ("klingt wie (mit Komma trennen)", 60));
        foreach (var e in store.Current.Dictionary) grid.Rows.Add(e.Term, string.Join(", ", e.SoundsLike));
        void Save() => Update(x => x.Dictionary = grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow)
            .Select(r => new DictionaryEntry((r.Cells[0].Value as string ?? "").Trim(),
                ((r.Cells[1].Value as string) ?? "").Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).ToArray()))
            .Where(d => d.Term.Length > 0).ToList());
        grid.CellEndEdit += (_, _) => Save();
        grid.UserDeletedRow += (_, _) => Save();
        pb.Add(grid);
        pb.Note("Beispiel: Schreibweise „Wispr Flow“, klingt wie „Whisperflow, whisper flow“. Zeile löschen: markieren und Entf drücken.");
    }

    // ───────────────────────── Textbausteine

    void SnippetsPage(PageBuilder pb)
    {
        pb.Header("Textbausteine", "Ein Stichwort sagen, ein ganzer Text erscheint. Etwa „meine Adresse“ oder „mein Calendly-Link“.");
        var grid = Grid(("Stichwort", 30), ("Text", 70));
        foreach (var sn in store.Current.Snippets) grid.Rows.Add(sn.Trigger, sn.Text);
        void Save() => Update(x => x.Snippets = grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow)
            .Select(r => new Snippet((r.Cells[0].Value as string ?? "").Trim(), r.Cells[1].Value as string ?? ""))
            .Where(sn => sn.Trigger.Length > 0).ToList());
        grid.CellEndEdit += (_, _) => Save();
        grid.UserDeletedRow += (_, _) => Save();
        pb.Add(grid);
        pb.Note("Sagst du nur das Stichwort, kommt nur der Baustein. Steht es in einem Satz, wird es darin ersetzt.");
    }

    DataGridView Grid(params (string Title, int Weight)[] columns)
    {
        var g = new DataGridView
        {
            Height = 380, AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Theme.Card, BorderStyle = BorderStyle.None,
            GridColor = Theme.Border, EnableHeadersVisualStyles = false, ColumnHeadersHeight = 34,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        };
        g.DefaultCellStyle.BackColor = Theme.Card;
        g.DefaultCellStyle.ForeColor = Theme.Text;
        g.DefaultCellStyle.SelectionBackColor = Theme.Selected;
        g.DefaultCellStyle.SelectionForeColor = Theme.Text;
        g.ColumnHeadersDefaultCellStyle.BackColor = Theme.Window;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Secondary;
        g.RowTemplate.Height = 30;
        foreach (var (title, weight) in columns)
            g.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, FillWeight = weight });
        return g;
    }

    // ───────────────────────── Verlauf

    void HistoryPage(PageBuilder pb)
    {
        var st = history.Summary;
        pb.Header("Verlauf", "Alle Diktate bleiben auf diesem PC. Doppelklick kopiert den Text.");
        var tiles = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Theme.Window };
        void Tile(string value, string label)
        {
            var p = new Panel { Width = 170, Height = 74, BackColor = Theme.Card, Margin = new Padding(0, 0, 10, 10) };
            p.Controls.Add(new Label { Text = value, Font = Theme.Title, AutoSize = true, ForeColor = Theme.Text, Location = new Point(12, 8) });
            p.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Theme.Secondary, Font = Theme.Small, Location = new Point(14, 48) });
            tiles.Controls.Add(p);
        }
        Tile(st.WeekWords.ToString("N0"), "Wörter diese Woche");
        Tile(st.Wpm.ToString(), "Wörter pro Minute");
        Tile(st.MinutesSaved + " min", "gespart ggü. Tippen");
        Tile(st.StreakDays + (st.StreakDays == 1 ? " Tag" : " Tage"), "in Folge");
        pb.Add(tiles);

        var search = new TextBox { Width = 320, PlaceholderText = "Suchen …", BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
        var clear = Theme.SecondaryButton("Alles löschen");
        pb.Add(Controls2.Line(search, clear));
        var grid = Grid(("Zeit", 18), ("App", 18), ("Text", 64));
        grid.AllowUserToAddRows = false;
        grid.ReadOnly = true;
        grid.Height = 420;
        void Fill()
        {
            grid.Rows.Clear();
            var q = search.Text.Trim();
            foreach (var h in history.Items.Where(i => q.Length == 0 || i.Text.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(500))
            {
                int r = grid.Rows.Add(h.Date.ToString("dd.MM. HH:mm"), h.AppName, h.Text.Replace("\n", " ⏎ "));
                grid.Rows[r].Tag = h.Text;
            }
        }
        search.TextChanged += (_, _) => Fill();
        grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].Tag is string t) TextInserter.SetClipboardText(t); };
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Den gesamten Verlauf löschen?", "Flüster", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                history.Clear();
                Show(current);
            }
        };
        Fill();
        pb.Add(grid);
    }

    // ───────────────────────── Über

    void AboutPage(PageBuilder pb)
    {
        var version = typeof(SettingsForm).Assembly.GetName().Version?.ToString(3) ?? "dev";
        pb.Header("Flüster " + version, "Diktieren in jeder App: halten, sprechen, loslassen. Kostenlos – Erkennung und KI wahlweise komplett auf deinem PC.");
        var c = pb.Card();
        c.Full(Controls2.Line(
            Controls2.Link("Auf GitHub", "https://github.com/KimJongUnNK/fluester"),
            Controls2.Link("Neueste Version", "https://github.com/KimJongUnNK/fluester/releases/latest"),
            Controls2.Link("Hilfe", "https://github.com/KimJongUnNK/fluester/blob/main/docs/windows.md#häufige-probleme")));
        var folder = Theme.SecondaryButton("Datenordner öffnen");
        folder.Click += (_, _) => Controls2.Open(Paths.Data);
        var log = Theme.SecondaryButton("Protokoll öffnen");
        log.Click += (_, _) => { if (File.Exists(Paths.LogFile)) Controls2.Open(Paths.LogFile); };
        var onboarding = Theme.SecondaryButton("Einrichtung erneut starten");
        onboarding.Click += (_, _) => TrayApp.Instance?.ShowOnboarding();
        c.Full(Controls2.Line(folder, log, onboarding));
        pb.Note("Spracherkennung: whisper.cpp (MIT) über Whisper.net, oder OpenAI-kompatible APIs. Bereinigung: Regeln oder OpenAI-kompatible Sprachmodelle.\n" +
                "Flüster enthält keinen Code von Wispr Flow und ist nicht mit Wispr verbunden. Lizenz: MIT.");
    }
}

/// <summary>Eine Zeile pro Whisper-Modell: auswählen, laden (mit Fortschritt), löschen.</summary>
sealed class ModelRow : TableLayoutPanel
{
    readonly WhisperModel model;
    readonly SettingsStore store;
    readonly Action changed;
    readonly RadioButton pick;
    readonly Label status = new() { AutoSize = true, ForeColor = Theme.Secondary };
    readonly ProgressBar bar = new() { Width = 160, Height = 8, Visible = false };
    readonly Button action;
    CancellationTokenSource? cts;

    public ModelRow(WhisperModel model, SettingsStore store, Action changed)
    {
        this.model = model;
        this.store = store;
        this.changed = changed;
        ColumnCount = 3;
        AutoSize = true;
        BackColor = Color.Transparent;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pick = new RadioButton { Text = $"{model.Name}  ·  {model.SizeMB} MB", AutoSize = true, ForeColor = Theme.Text, Font = Theme.Bold };
        pick.Checked = store.Current.WhisperModelId == model.Id;
        pick.CheckedChanged += (_, _) => { if (pick.Checked) { store.Update(s => s.WhisperModelId = model.Id); changed(); } };
        var left = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Color.Transparent };
        left.Controls.Add(pick);
        left.Controls.Add(new Label { Text = model.Note, AutoSize = true, ForeColor = Theme.Secondary, Font = Theme.Small, Margin = new Padding(20, 0, 0, 4) });
        action = Theme.SecondaryButton("Laden");
        action.Click += OnAction;
        var right = Controls2.Line(bar, status);
        Controls.Add(left, 0, 0);
        Controls.Add(right, 1, 0);
        Controls.Add(action, 2, 0);
        Refresh2();
    }

    void Refresh2()
    {
        bool installed = ModelStore.IsInstalled(model);
        status.Text = installed ? "✓ geladen" : "";
        status.ForeColor = installed ? Theme.Good : Theme.Secondary;
        action.Text = cts != null ? "Stopp" : installed ? "Löschen" : "Laden";
    }

    async void OnAction(object? sender, EventArgs e)
    {
        if (cts != null) { cts.Cancel(); return; }
        if (ModelStore.IsInstalled(model))
        {
            if (MessageBox.Show($"„{model.Name}“ löschen ({model.SizeMB} MB)?", "Flüster", MessageBoxButtons.YesNo) == DialogResult.Yes)
                ModelStore.Delete(model);
            Refresh2();
            return;
        }
        cts = new CancellationTokenSource();
        bar.Visible = true;
        bar.Value = 0;
        Refresh2();
        try
        {
            var progress = new Progress<double>(p => { bar.Value = (int)(p * 100); status.Text = $"{p:P0}"; });
            await ModelStore.DownloadAsync(model, progress, cts.Token);
            store.Update(s => s.WhisperModelId = model.Id);
            pick.Checked = true;
            changed();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { MessageBox.Show("Download fehlgeschlagen: " + ex.Message, "Flüster"); }
        finally
        {
            cts = null;
            bar.Visible = false;
            Refresh2();
        }
    }
}

/// <summary>Anbieter, Adresse, Modell, API-Schlüssel, Verbindungstest.</summary>
sealed class ProviderEditor : Card
{
    public ProviderEditor(ApiConfig initial, ISecretStore secrets, bool transcription, Action<ApiConfig> changed)
    {
        var cfg = new ApiConfig(initial.ProviderId, initial.BaseUrl, initial.Model);
        var url = new TextBox { Width = 320, Text = cfg.BaseUrl, BackColor = Theme.Window, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
        var model = new TextBox { Width = 320, Text = cfg.Model, BackColor = Theme.Window, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
        var key = new TextBox { Width = 320, UseSystemPasswordChar = true, Text = secrets.Get(cfg.ProviderId) ?? "", BackColor = Theme.Window, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
        var keyLink = new LinkLabel { AutoSize = true, LinkColor = Theme.Accent, BackColor = Color.Transparent };
        var result = new Label { AutoSize = true, ForeColor = Theme.Secondary, MaximumSize = new Size(420, 0) };
        void UpdateKeyLink()
        {
            var p = ProviderPreset.ById(cfg.ProviderId);
            keyLink.Text = p.KeyUrl.Length > 0 ? (p.NeedsKey ? "Schlüssel holen ↗" : "Herunterladen ↗") : "";
            keyLink.Tag = p.KeyUrl;
        }
        keyLink.LinkClicked += (_, _) => { if (keyLink.Tag is string u && u.Length > 0) Controls2.Open(u); };
        var presets = transcription ? ProviderPreset.All.Where(p => p.Id != "ollama").ToArray() : ProviderPreset.All;
        Row("Anbieter", Controls2.Combo(presets, p => p.Name, ProviderPreset.ById(cfg.ProviderId), p =>
        {
            cfg.ProviderId = p.Id;
            if (p.Id != "custom")
            {
                cfg.BaseUrl = p.BaseUrl;
                cfg.Model = transcription ? p.TranscriptionModel : p.ChatModel;
                url.Text = cfg.BaseUrl;
                model.Text = cfg.Model;
            }
            key.Text = secrets.Get(p.Id) ?? "";
            UpdateKeyLink();
            changed(cfg);
        }, 320));
        Row("Server-Adresse", url);
        Row("Modell", model);
        Row("API-Schlüssel", key, "Wird verschlüsselt gespeichert (nur dein Windows-Konto kann ihn lesen).");
        url.Leave += (_, _) => { cfg.BaseUrl = url.Text.Trim(); changed(cfg); };
        model.Leave += (_, _) => { cfg.Model = model.Text.Trim(); changed(cfg); };
        key.Leave += (_, _) => secrets.Set(cfg.ProviderId, key.Text);
        var test = Theme.SecondaryButton("Verbindung testen");
        test.Click += async (_, _) =>
        {
            secrets.Set(cfg.ProviderId, key.Text);
            cfg.BaseUrl = url.Text.Trim();
            cfg.Model = model.Text.Trim();
            changed(cfg);
            test.Enabled = false;
            result.Text = "Teste …";
            try
            {
                result.Text = await DictationService.TestConnectionAsync(cfg, secrets.Get(cfg.ProviderId), transcription);
                result.ForeColor = Theme.Good;
            }
            catch (Exception e)
            {
                result.Text = e.Message;
                result.ForeColor = Theme.Warn;
            }
            test.Enabled = true;
        };
        UpdateKeyLink();
        Full(Controls2.Line(test, keyLink, result));
    }
}
