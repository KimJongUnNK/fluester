using Fluester.Core;
using Fluester.Engine;

namespace Fluester.Linux.Gui;

enum Page { General, Recognition, Cleanup, Styles, Dictionary, Snippets, History, Rights, About }

/// <summary><c>fluester app</c>: das App-Fenster – Einstellungen mit Seitenleiste wie am Mac, Verlauf, Rechte, Einrichtung.
/// Diktiert wird weiter vom Hintergrunddienst; das Fenster steuert ihn und schreibt settings.json, die er sofort übernimmt.</summary>
static class DesktopApp
{
    public static Adw.Application? App { get; private set; }

    public static int Run(string[] args)
    {
        Desktop.RepairEnvironment();
        if (args.Contains("--check"))
        {
            bool ok = Integration.CanShowWindow(out var why);
            Console.WriteLine(ok ? "✓ App-Fenster verfügbar" : "✗ " + why);
            return ok ? 0 : 1;
        }
        if (!Integration.CanShowWindow(out var reason))
        {
            Console.Error.WriteLine("✗ " + reason);
            if (Console.IsOutputRedirected) Desktop.Notify("Flüster", reason, urgent: true);
            return 1;
        }
        int i = Array.IndexOf(args, "--screenshots");
        string? shots = i >= 0 && i + 1 < args.Length ? Path.GetFullPath(args[i + 1]) : null;
        // App-Menü-Eintrag auf Stand halten – aber nie von einem Entwickler-Build aus überschreiben
        if (shots == null && (Integration.RunningInstalled || !File.Exists(Integration.DesktopFile)))
            try { Integration.Install(); } catch (Exception e) { Log.Write("App-Menü-Eintrag: " + e.Message); }

        Adw.Module.Initialize();
        // Wayland-App-ID und X11-WM_CLASS = Name der .desktop-Datei – nur so zeigen GNOME/KDE das richtige Symbol im Dock
        // (ohne Sitzungsbus fiele GTK sonst auf den Programmnamen „fluester“ zurück)
        GLib.Functions.SetPrgname(Integration.AppId);
        GLib.Functions.SetApplicationName("Flüster");
        // Ohne Sitzungsbus (z. B. CI) kann sich die App nicht als einzige Instanz anmelden – dann eben ohne
        bool bus = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is { Length: > 0 }
            || File.Exists(Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "/nonexistent", "bus"));
        var app = Adw.Application.New(Integration.AppId, shots != null || !bus ? Gio.ApplicationFlags.NonUnique : Gio.ApplicationFlags.DefaultFlags);
        App = app;
        MainWindow? main = null;
        int exit = 0;
        app.OnActivate += (_, _) => Ui.Safe(() =>
        {
            if (main != null) { main.Window.Present(); return; }
            Ui.LoadCss();
            Integration.RegisterIcon();
            Gtk.Window.SetDefaultIconName(Integration.AppId);
            main = new MainWindow(app, shots == null);
            main.Window.Present();
            if (shots != null) Screenshots.Run(main, shots, code => { exit = code; app.Quit(); });
            else main.Startup();
        });
        var quit = Gio.SimpleAction.New("quit", null);
        quit.OnActivate += (_, _) => app.Quit();
        app.AddAction(quit);
        app.SetAccelsForAction("app.quit", new[] { "<Control>q", "<Control>w" });
        int rc = app.RunWithSynchronizationContext(null);
        return exit != 0 ? exit : rc;
    }
}

/// <summary>Hauptfenster: Seitenleiste links (mit Dienst-Status unten), Seite rechts.</summary>
sealed class MainWindow
{
    public readonly Adw.ApplicationWindow Window;
    public readonly SettingsStore Store = new();
    public readonly FileSecretStore Secrets = new();
    public HistoryStore History { get; private set; } = new();
    public DaemonStatus? Status { get; private set; }
    public bool Live { get; }

    readonly Adw.ToastOverlay toasts = Adw.ToastOverlay.New();
    readonly Gtk.Stack stack = Gtk.Stack.New();
    readonly Adw.WindowTitle pageTitle = Adw.WindowTitle.New("", "");
    readonly Gtk.ListBox nav = Gtk.ListBox.New();
    readonly Dictionary<Page, Gtk.Widget> built = new();
    readonly HashSet<Page> stale = new();
    readonly Dictionary<Page, List<Action>> statusListeners = new();
    readonly Gtk.Label statusDot = Gtk.Label.New("●");
    readonly Gtk.Label statusText = Gtk.Label.New("");
    readonly Pages pages;
    Page current = (Page)(-1);
    Page building;
    bool polling;
    DateTime ownWrite;
    FileSystemWatcher? settingsWatcher, historyWatcher;

    public record PageInfo(Page Page, string Title, string Subtitle, string Icon, string Color);

    public static readonly PageInfo[] All =
    {
        new(Page.General, "Allgemein", "Taste, Mikrofon, Einfügen", "emblem-system-symbolic", "gray"),
        new(Page.Recognition, "Erkennung", "Whisper lokal oder Cloud", "audio-input-microphone-symbolic", "blue"),
        new(Page.Cleanup, "KI-Bereinigung", "Füllwörter raus, sauber formuliert", "starred-symbolic", "purple"),
        new(Page.Styles, "Stile", "Wie der Text klingt – je App", "format-text-bold-symbolic", "orange"),
        new(Page.Dictionary, "Wörterbuch", "Namen und Fachbegriffe", "accessories-dictionary-symbolic", "brown"),
        new(Page.Snippets, "Textbausteine", "Stichwort sagen, Text erscheint", "insert-text-symbolic", "teal"),
        new(Page.History, "Verlauf", "Alle Diktate, nur auf diesem Rechner", "document-open-recent-symbolic", "indigo"),
        new(Page.Rights, "Berechtigungen", "Tastatur, Einfügen, Werkzeuge", "security-high-symbolic", "red"),
        new(Page.About, "Über Flüster", "Version, Hilfe, Protokoll", "help-about-symbolic", "gray"),
    };

    public MainWindow(Adw.Application app, bool live)
    {
        Live = live;
        pages = new Pages(this);
        Window = Adw.ApplicationWindow.New(app);
        Window.Title = "Flüster";
        Window.SetDefaultSize(1000, 720);
        Window.SetSizeRequest(760, 520);
        Ui.ErrorSink = Toast;
        Store.Changed += _ => ownWrite = DateTime.UtcNow;

        // Seitenleiste
        var sideHeader = Adw.HeaderBar.New();
        sideHeader.ShowEndTitleButtons = false;
        var brand = Ui.HBox(8, Ui.Icon(Integration.AppId, 20), Ui.Label("Flüster", "heading"));
        sideHeader.TitleWidget = brand;
        nav.AddCssClass("navigation-sidebar");
        foreach (var p in All)
        {
            var row = Gtk.ListBoxRow.New();
            var label = Ui.Label(p.Title);
            label.Wrap = false;
            row.Child = Ui.Margins(Ui.HBox(12, Ui.Tile(p.Icon, p.Color), label), 3);
            nav.Append(row);
        }
        nav.OnRowSelected += (_, e) => Ui.Safe(() => { if (e.Row != null) Show((Page)e.Row.GetIndex()); });
        var navScroll = Gtk.ScrolledWindow.New();
        navScroll.Child = nav;
        navScroll.Vexpand = true;
        navScroll.HscrollbarPolicy = Gtk.PolicyType.Never;
        statusDot.AddCssClass("fl-dot");
        statusText.Xalign = 0;
        statusText.Ellipsize = Pango.EllipsizeMode.End;
        statusText.AddCssClass("caption");
        var statusBox = Ui.HBox(8, statusDot, statusText);
        statusBox.AddCssClass("fl-sidebar-status");
        var statusClick = Gtk.GestureClick.New();
        statusClick.OnReleased += (_, _) => Ui.Safe(() => Select(Page.General));
        statusBox.AddController(statusClick);
        var side = Ui.VBox(0, sideHeader, navScroll, Gtk.Separator.New(Gtk.Orientation.Horizontal), statusBox);
        side.WidthRequest = 230;

        // Inhalt
        var contentHeader = Adw.HeaderBar.New();
        contentHeader.ShowStartTitleButtons = false;
        contentHeader.TitleWidget = pageTitle;
        stack.TransitionType = Gtk.StackTransitionType.Crossfade;
        stack.TransitionDuration = 120;
        stack.Vexpand = stack.Hexpand = true;
        var content = Ui.VBox(0, contentHeader, stack);
        content.Hexpand = true;
        content.AddCssClass("view");

        toasts.Child = Ui.HBox(0, side, Gtk.Separator.New(Gtk.Orientation.Vertical), content);
        Window.Content = toasts;
        Window.OnCloseRequest += (_, _) => { settingsWatcher?.Dispose(); historyWatcher?.Dispose(); pages.Dispose(); return false; };

        UpdateStatusWidgets();
        Select(Page.General);
        if (Live)
        {
            Ui.Every(2000, () => { Poll(); return true; });
            Poll();
            Watch();
        }
    }

    /// <summary>Nach dem ersten Zeigen: Einrichtung beim allerersten Start, sonst den Dienst starten, falls er nicht läuft.</summary>
    public void Startup()
    {
        if (!Store.Current.OnboardingDone) { ShowOnboarding(); return; }
        _ = EnsureRunning();
    }

    async Task EnsureRunning()
    {
        if (await Task.Run(Daemon.IsRunning)) return;
        bool ok = await Service.StartAsync();
        Toast(ok ? "Flüster läuft jetzt im Hintergrund" : "Flüster startet nicht – siehe Über Flüster › Protokoll");
        Poll();
    }

    public Onboarding? OnboardingWindow { get; private set; }

    public void ShowOnboarding()
    {
        if (OnboardingWindow != null) { OnboardingWindow.Window.Present(); return; }
        OnboardingWindow = new Onboarding(this, () =>
        {
            OnboardingWindow = null;
            Store.Reload();
            RebuildAll();
            Poll();
        });
        OnboardingWindow.Window.Present();
    }

    public void Toast(string text)
    {
        // Ab libadwaita 1.4 lässt sich Markup abschalten, davor maskieren
        bool plain = Ui.AdwAtLeast(4);
        var t = Adw.Toast.New(plain ? text : Ui.Esc(text));
        if (plain) t.UseMarkup = false;
        t.Timeout = 4;
        toasts.AddToast(t);
    }

    public void Select(Page p)
    {
        var row = nav.GetRowAtIndex((int)p);
        if (row != null) nav.SelectRow(row);
    }

    void Show(Page p)
    {
        if (p == current && !stale.Contains(p)) return;
        if (stale.Remove(p) && built.Remove(p, out var old)) { pages.Leaving(p); stack.Remove(old); }
        if (current != p && built.ContainsKey(current)) pages.Hidden(current);
        if (!built.ContainsKey(p)) Build(p);
        current = p;
        stack.SetVisibleChildName(p.ToString());
        var info = All[(int)p];
        pageTitle.Title = info.Title;
        pageTitle.Subtitle = info.Subtitle;
        pages.Shown(p);
    }

    void Build(Page p)
    {
        building = p;
        statusListeners[p] = new();
        Gtk.Widget w;
        try { w = pages.Build(p); }
        catch (Exception e)
        {
            Log.Write($"Seite {p}: {e}");
            var sp = Adw.StatusPage.New();
            sp.IconName = "dialog-warning-symbolic";
            sp.Title = "Seite lässt sich nicht anzeigen";
            sp.Description = Ui.Esc(e.Message);
            w = sp;
        }
        built[p] = w;
        stack.AddNamed(w, p.ToString());
    }

    /// <summary>Seite neu aufbauen (z. B. nach Wechsel der Erkennung). Nicht sichtbare Seiten entstehen beim nächsten Zeigen neu.</summary>
    public void Rebuild(Page p)
    {
        if (built.Remove(p, out var old)) { pages.Leaving(p); stack.Remove(old); }
        stale.Remove(p);
        if (p != current) return;
        Build(p);
        stack.SetVisibleChildName(p.ToString());
        pages.Shown(p);
    }

    public void RebuildAll()
    {
        foreach (var p in built.Keys.ToList()) if (p != current) stale.Add(p);
        Rebuild(current);
    }

    // ───────────── Dienst-Status

    /// <summary>Wird bei jedem neuen Stand des Dienstes aufgerufen – nur solange die gerade gebaute Seite besteht.</summary>
    public void OnStatus(Action a)
    {
        if (statusListeners.TryGetValue(building, out var list)) list.Add(a);
        a();
    }

    public void Poll()
    {
        if (polling || !Live) return;
        polling = true;
        Task.Run(() => Daemon.Status()).ContinueWith(t => Ui.Idle(() =>
        {
            polling = false;
            var before = Status;
            Status = t.IsCompletedSuccessfully ? t.Result : null;
            UpdateStatusWidgets();
            foreach (var list in statusListeners.Values)
                foreach (var a in list.ToList()) Ui.Safe(a);
            if ((before == null) != (Status == null)) Log.Write("Fenster: Dienst " + (Status != null ? "läuft" : "aus"));
        }));
    }

    void UpdateStatusWidgets()
    {
        foreach (var c in new[] { "fl-dot-on", "fl-dot-busy", "fl-dot-off" }) statusDot.RemoveCssClass(c);
        var s = Status;
        if (s == null)
        {
            statusDot.AddCssClass("fl-dot-off");
            statusText.SetText(Live ? "Aus – Allgemein › Starten" : "Vorschau");
        }
        else
        {
            bool problem = s.Problems.Length > 0 || !s.KeyboardOk;
            statusDot.AddCssClass(problem || s.State != "idle" ? "fl-dot-busy" : "fl-dot-on");
            statusText.SetText(problem ? $"Läuft · {s.Problems.Length} Hinweis{(s.Problems.Length == 1 ? "" : "e")}" : $"Läuft · {s.StateLabel}");
        }
    }

    // ───────────── Änderungen von außen (Terminal, Dienst, Editor)

    void Watch()
    {
        try
        {
            settingsWatcher = new FileSystemWatcher(Paths.Config) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
            void Settings(string? name)
            {
                if (name != "settings.json") return;
                Ui.Idle(() =>
                {
                    if ((DateTime.UtcNow - ownWrite).TotalSeconds < 1.5) return;
                    Store.Reload();
                    ownWrite = DateTime.UtcNow;
                    foreach (var p in built.Keys) if (p != current) stale.Add(p);
                });
            }
            settingsWatcher.Changed += (_, e) => Settings(e.Name);
            settingsWatcher.Renamed += (_, e) => Settings(e.Name);
            settingsWatcher.Created += (_, e) => Settings(e.Name);
            settingsWatcher.EnableRaisingEvents = true;

            historyWatcher = new FileSystemWatcher(Paths.Data) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
            var refresh = Ui.Debounce(400, () =>
            {
                History = new HistoryStore();
                if (current == Page.History) Rebuild(Page.History);
                else if (built.ContainsKey(Page.History)) stale.Add(Page.History);
            });
            void Hist(string? name) { if (name == "history.json") Ui.Idle(refresh); }
            historyWatcher.Changed += (_, e) => Hist(e.Name);
            historyWatcher.Renamed += (_, e) => Hist(e.Name);
            historyWatcher.Created += (_, e) => Hist(e.Name);
            historyWatcher.EnableRaisingEvents = true;
        }
        catch (Exception e) { Log.Write("Fenster beobachtet Dateien nicht: " + e.Message); }
    }

    public Page Current => current;
    public Gtk.Widget? BuiltPage(Page p) => built.TryGetValue(p, out var w) ? w : null;
}
