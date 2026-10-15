using System.Drawing;
using System.Windows.Forms;
using Fluester.Core;
using Fluester.Engine;
using Fluester.Windows.Ui;

namespace Fluester.Windows;

/// <summary>Flüster im Infobereich der Taskleiste: Symbol, Menü, Fenster.</summary>
sealed class TrayApp : ApplicationContext
{
    public static TrayApp? Instance { get; private set; }

    static Icon? appIcon;
    public static Icon AppIcon => appIcon ??= LoadIcon();

    readonly SettingsStore store = new();
    readonly HistoryStore history = new();
    readonly ISecretStore secrets = new WinSecretStore();
    readonly DictationController controller;
    readonly NotifyIcon tray;
    readonly ContextMenuStrip menu = new();
    readonly Icon idleIcon, recordingIcon, busyIcon;
    SettingsForm? settingsForm;
    OnboardingForm? onboardingForm;

    public TrayApp(bool background)
    {
        Instance = this;
        controller = new DictationController(store, history, secrets);
        idleIcon = new Icon(AppIcon, SystemInformation.SmallIconSize);
        recordingIcon = Badge(idleIcon, Color.FromArgb(255, 59, 48));
        busyIcon = Badge(idleIcon, Color.FromArgb(255, 159, 10));
        menu.Renderer = Theme.Dark ? new Theme.MenuRenderer() : new ToolStripProfessionalRenderer();
        menu.Opening += (_, _) => BuildMenu();
        tray = new NotifyIcon { Icon = idleIcon, Text = "Flüster", Visible = true, ContextMenuStrip = menu };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(SettingsPage.General); };
        controller.StateChanged += RefreshIcon;
        controller.Start();
        RefreshIcon();

        var s = store.Current;
        if (!s.OnboardingDone) ShowOnboarding();
        // Von Hand gestartet (Startmenü, Doppelklick) → Fenster wie eine App; Autostart bleibt still im Infobereich
        else if (!background) ShowSettings(SettingsPage.General);
    }

    bool closedHintShown;

    static Icon LoadIcon()
    {
        using var s = typeof(TrayApp).Assembly.GetManifestResourceStream("fluester.ico");
        return s != null ? new Icon(s) : SystemIcons.Application;
    }

    /// <summary>Kleiner farbiger Punkt unten rechts auf dem Symbol (Aufnahme = rot, Verarbeitung = orange).</summary>
    static Icon Badge(Icon baseIcon, Color color)
    {
        using var bmp = baseIcon.ToBitmap();
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int d = bmp.Width / 2;
            using var b = new SolidBrush(color);
            using var p = new Pen(Color.White, Math.Max(1, bmp.Width / 16f));
            g.FillEllipse(b, bmp.Width - d - 1, bmp.Height - d - 1, d, d);
            g.DrawEllipse(p, bmp.Width - d - 1, bmp.Height - d - 1, d, d);
        }
        var h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        Native.DestroyIcon(h);
        return icon;
    }

    void RefreshIcon()
    {
        tray.Icon = controller.State switch
        {
            DictationState.Recording => recordingIcon,
            DictationState.Processing => busyIcon,
            _ => idleIcon,
        };
        tray.Text = controller.State switch
        {
            DictationState.Recording => "Flüster – hört zu",
            DictationState.Processing => "Flüster – schreibt",
            _ => "Flüster – " + store.Current.Hotkey.Label() + " halten",
        };
    }

    string StatusLine()
    {
        if (!controller.HotkeyActive) return "Taste nicht aktiv – Flüster neu starten";
        return controller.State switch
        {
            DictationState.Recording => "Hört zu …",
            DictationState.Processing => "Schreibt …",
            _ => $"Bereit – {store.Current.Hotkey.Label()} halten zum Diktieren",
        };
    }

    void BuildMenu()
    {
        menu.Items.Clear();
        var s = store.Current;
        var st = history.Summary;
        menu.Items.Add(new ToolStripMenuItem(StatusLine()) { Enabled = false });
        if (controller.LastError is { } err) menu.Items.Add(new ToolStripMenuItem("⚠ " + Shorten(err, 70)) { Enabled = false, ToolTipText = err });
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(controller.State == DictationState.Recording ? "Diktat beenden" : "Freihändig diktieren", null, (_, _) => controller.ToggleFromMenu());
        menu.Items.Add(new ToolStripMenuItem("Letztes Diktat erneut einfügen", null, (_, _) => controller.PasteLast()) { Enabled = history.Last != null });

        var hist = new ToolStripMenuItem("Verlauf");
        if (history.Items.Count == 0) hist.DropDownItems.Add(new ToolStripMenuItem("Noch keine Diktate") { Enabled = false });
        foreach (var h in history.Items.Take(12))
        {
            var text = h.Text;
            hist.DropDownItems.Add(new ToolStripMenuItem(Shorten(text.Replace("\n", " "), 60), null, (_, _) => TextInserter.SetClipboardText(text))
            {
                ToolTipText = "Klicken zum Kopieren · " + (h.AppName.Length > 0 ? h.AppName + " · " : "") + h.Date.ToString("g"),
            });
        }
        hist.DropDownItems.Add(new ToolStripSeparator());
        hist.DropDownItems.Add("Ganzen Verlauf zeigen …", null, (_, _) => ShowSettings(SettingsPage.History));
        menu.Items.Add(hist);
        menu.Items.Add(new ToolStripMenuItem($"{st.WeekWords:N0} Wörter diese Woche · {st.Wpm} WPM · {st.MinutesSaved} min gespart") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        var engine = new ToolStripMenuItem("Erkennung: " + s.Engine.Label());
        foreach (var e in Enum.GetValues<EngineChoice>())
            engine.DropDownItems.Add(new ToolStripMenuItem(e.Label(), null, (_, _) => { store.Update(x => x.Engine = e); controller.Prewarm(); }) { Checked = e == s.Engine });
        menu.Items.Add(engine);
        var cleanup = new ToolStripMenuItem("KI: " + s.Cleanup.Label());
        foreach (var c in Enum.GetValues<CleanupChoice>())
            cleanup.DropDownItems.Add(new ToolStripMenuItem(c.Label(), null, (_, _) => store.Update(x => x.Cleanup = c)) { Checked = c == s.Cleanup });
        menu.Items.Add(cleanup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Einstellungen …", null, (_, _) => ShowSettings(SettingsPage.General));
        menu.Items.Add("Einrichtung …", null, (_, _) => ShowOnboarding());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Flüster beenden", null, (_, _) => ExitThread());
        foreach (ToolStripItem i in menu.Items) if (Theme.Dark) i.ForeColor = Theme.Text;
    }

    static string Shorten(string s, int max) => s.Length > max ? s[..max] + "…" : s;

    public void ShowSettings(SettingsPage page)
    {
        if (settingsForm == null || settingsForm.IsDisposed)
        {
            settingsForm = new SettingsForm(store, history, secrets, controller);
            settingsForm.FormClosed += (_, _) =>
            {
                settingsForm = null;
                // Einmal pro Start erklären, dass Schließen nicht beendet
                if (closedHintShown) return;
                closedHintShown = true;
                tray.ShowBalloonTip(4000, "Flüster läuft weiter", $"{store.Current.Hotkey.Label()} halten, sprechen, loslassen. Fenster und Menü: Symbol hier unten rechts.", ToolTipIcon.None);
            };
        }
        settingsForm.Show(page);
        settingsForm.Show();
        if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
        settingsForm.Activate();
    }

    public void ShowOnboarding()
    {
        if (onboardingForm == null || onboardingForm.IsDisposed)
        {
            onboardingForm = new OnboardingForm(store, secrets, () => controller.Prewarm());
            onboardingForm.FormClosed += (_, _) => onboardingForm = null;
        }
        onboardingForm.Show();
        onboardingForm.Activate();
    }

    protected override void ExitThreadCore()
    {
        tray.Visible = false;
        tray.Dispose();
        controller.Dispose();
        WhisperLocalEngine.Unload();
        base.ExitThreadCore();
    }
}
