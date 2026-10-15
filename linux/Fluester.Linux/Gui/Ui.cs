using System.Diagnostics;
using Fluester.Core;
using Fluester.Engine;

namespace Fluester.Linux.Gui;

/// <summary>Bausteine für die Fenster: Zeilen, Schalter, Auswahl, Texteingaben im Stil von libadwaita.
/// Nutzt nur, was es seit libadwaita 1.1 gibt (Ubuntu 22.04); Neueres nur mit Versionsprüfung.</summary>
static class Ui
{
    /// <summary>Fehler in einem Ereignis dürfen das Fenster nie beenden.</summary>
    public static Action<string>? ErrorSink;

    public static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e)
        {
            Log.Write("Fenster: " + e);
            ErrorSink?.Invoke(e is FluesterException ? e.Message : "Fehler: " + e.Message);
        }
    }

    /// <summary>Für Klick-Handler mit await: Fehler landen im Protokoll und als Hinweis, statt das Fenster zu beenden.</summary>
    public static async void Run(Func<Task> f)
    {
        try { await f(); }
        catch (Exception e)
        {
            Log.Write("Fenster: " + e);
            ErrorSink?.Invoke(e is FluesterException ? e.Message : "Fehler: " + e.Message);
        }
    }

    public static bool AdwAtLeast(uint minor) => Adw.Functions.GetMajorVersion() > 1 || Adw.Functions.GetMinorVersion() >= minor;

    /// <summary>Für Texte, die als Pango-Markup gelesen werden (Statusseiten, Hinweise vor libadwaita 1.4).</summary>
    public static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // ───────────── Hauptschleife

    /// <summary>Auf dem Fenster-Thread ausführen (aus Hintergrund-Threads).</summary>
    public static void Idle(Action a) => GLib.Functions.IdleAdd(0, () => { Safe(a); return false; });

    /// <summary>Nach <paramref name="ms"/> Millisekunden; solange <paramref name="tick"/> true liefert, wiederholt.</summary>
    public static void Every(uint ms, Func<bool> tick) => GLib.Functions.TimeoutAdd(0, ms, () =>
    {
        try { return tick(); }
        catch (Exception e) { Log.Write("Fenster-Timer: " + e); return false; }
    });

    public static void After(uint ms, Action a) => Every(ms, () => { Safe(a); return false; });

    /// <summary>Ruft <paramref name="a"/> erst auf, wenn <paramref name="ms"/> lang nichts Neues kam (z. B. beim Tippen).</summary>
    public static Action Debounce(uint ms, Action a)
    {
        int generation = 0;
        return () =>
        {
            int mine = ++generation;
            After(ms, () => { if (mine == generation) a(); });
        };
    }

    // ───────────── Bausteine

    public static Gtk.Label Label(string text, params string[] css)
    {
        var l = Gtk.Label.New(text);
        l.Wrap = true;
        l.Xalign = 0;
        foreach (var c in css) l.AddCssClass(c);
        return l;
    }

    public static Gtk.Box Box(Gtk.Orientation o, int spacing, params Gtk.Widget[] children)
    {
        var b = Gtk.Box.New(o, spacing);
        foreach (var c in children) b.Append(c);
        return b;
    }

    public static Gtk.Box HBox(int spacing, params Gtk.Widget[] children) => Box(Gtk.Orientation.Horizontal, spacing, children);
    public static Gtk.Box VBox(int spacing, params Gtk.Widget[] children) => Box(Gtk.Orientation.Vertical, spacing, children);

    public static T Margins<T>(T w, int all) where T : Gtk.Widget
    {
        w.MarginTop = w.MarginBottom = w.MarginStart = w.MarginEnd = all;
        return w;
    }

    public static Gtk.Button Button(string label, Action click, params string[] css)
    {
        var b = Gtk.Button.NewWithLabel(label);
        b.Valign = Gtk.Align.Center;
        foreach (var c in css) b.AddCssClass(c);
        b.OnClicked += (_, _) => Safe(click);
        return b;
    }

    public static Gtk.Button IconButton(string icon, string tooltip, Action click)
    {
        var b = Gtk.Button.NewFromIconName(icon);
        b.TooltipText = tooltip;
        b.Valign = Gtk.Align.Center;
        b.AddCssClass("flat");
        b.OnClicked += (_, _) => Safe(click);
        return b;
    }

    /// <summary>Knopf für Folgenreiches: erster Klick fragt nach („Wirklich …?“), zweiter Klick innerhalb von 4 s führt aus.</summary>
    public static Gtk.Button ConfirmButton(string label, string confirm, Action action, bool flatIcon = false, string? icon = null)
    {
        var b = icon != null ? Gtk.Button.NewFromIconName(icon) : Gtk.Button.NewWithLabel(label);
        b.Valign = Gtk.Align.Center;
        if (icon != null) { b.TooltipText = label; b.AddCssClass("flat"); }
        bool armed = false;
        int generation = 0;
        b.OnClicked += (_, _) => Safe(() =>
        {
            if (armed) { armed = false; Reset(); action(); return; }
            armed = true;
            int mine = ++generation;
            if (icon != null) b.TooltipText = confirm;
            b.Label = confirm;
            b.AddCssClass("destructive-action");
            After(4000, () => { if (mine == generation && armed) { armed = false; Reset(); } });
        });
        void Reset()
        {
            b.RemoveCssClass("destructive-action");
            if (icon != null) { b.IconName = icon; b.TooltipText = label; }
            else b.Label = label;
        }
        return b;
    }

    public static Adw.PreferencesGroup Group(string title, string? description = null)
    {
        var g = Adw.PreferencesGroup.New();
        if (title.Length > 0) g.Title = title;
        if (description != null) g.Description = description;
        return g;
    }

    public static Adw.PreferencesPage Page(params Adw.PreferencesGroup[] groups)
    {
        var p = Adw.PreferencesPage.New();
        foreach (var g in groups) p.Add(g);
        return p;
    }

    public static Adw.ActionRow Row(string title, string? subtitle = null)
    {
        var r = Adw.ActionRow.New();
        r.UseMarkup = false;   // Titel als reiner Text: „&“, „<“ in Namen und Fehlermeldungen bleiben, wie sie sind
        r.Title = title;
        if (!string.IsNullOrEmpty(subtitle)) r.Subtitle = subtitle;
        return r;
    }

    public static void SetSubtitle(Adw.ActionRow r, string? subtitle) => r.Subtitle = subtitle ?? "";

    public static Gtk.Image Icon(string name, int size = 16)
    {
        var i = Gtk.Image.NewFromIconName(name);
        i.PixelSize = size;
        return i;
    }

    /// <summary>Zeile mit Symbol vorne: ✓ grün, ! gelb, ✗ rot.</summary>
    public static Adw.ActionRow StatusRow(string title, string? subtitle, bool? ok)
    {
        var r = Row(title, subtitle);
        var icon = Icon(ok switch { true => "emblem-ok-symbolic", false => "dialog-warning-symbolic", null => "dialog-information-symbolic" });
        icon.AddCssClass(ok switch { true => "success", false => "warning", null => "dim-label" });
        r.AddPrefix(icon);
        return r;
    }

    /// <summary>Schalter-Zeile (Adw.SwitchRow gibt es erst ab 1.4 – so läuft es auch auf älteren Systemen).</summary>
    public static Adw.ActionRow Switch(string title, string? subtitle, bool value, Action<bool> changed) => Switch(title, subtitle, value, changed, out _);

    public static Adw.ActionRow Switch(string title, string? subtitle, bool value, Action<bool> changed, out Gtk.Switch sw)
    {
        var r = Row(title, subtitle);
        var s = Gtk.Switch.New();
        s.Active = value;
        s.Valign = Gtk.Align.Center;
        r.AddSuffix(s);
        r.ActivatableWidget = s;
        GObject.Object.NotifySignal.Connect(s, (_, _) => Safe(() => changed(s.Active)), false, "active");
        sw = s;
        return r;
    }

    public static Adw.ComboRow Combo(string title, string? subtitle, IReadOnlyList<string> labels, int selected, Action<int> changed)
    {
        var r = Adw.ComboRow.New();
        r.UseMarkup = false;
        r.Title = title;
        if (!string.IsNullOrEmpty(subtitle)) r.Subtitle = subtitle;
        r.Model = Gtk.StringList.New(labels.ToArray());
        r.Selected = (uint)Math.Clamp(selected, 0, Math.Max(0, labels.Count - 1));
        GObject.Object.NotifySignal.Connect(r, (_, _) => Safe(() => { if (r.Selected < labels.Count) changed((int)r.Selected); }), false, "selected");
        return r;
    }

    public static Adw.ComboRow Combo<T>(string title, string? subtitle, IReadOnlyList<T> items, Func<T, string> label, T? selected, Action<T> changed)
    {
        int idx = selected == null ? 0 : Math.Max(0, items.ToList().IndexOf(selected));
        return Combo(title, subtitle, items.Select(label).ToList(), idx, i => changed(items[i]));
    }

    /// <summary>Texteingabe als Zeile: Zeile, aktueller Text, Text setzen (ohne „geändert“ auszulösen).</summary>
    public sealed record TextField(Gtk.Widget Row, Func<string> Get, Action<string> Set);

    /// <summary>Texteingabe als Zeile; <paramref name="changed"/> kommt kurz nach dem letzten Tastendruck.
    /// libadwaita ≥ 1.2: Adw.EntryRow / PasswordEntryRow, davor ActionRow mit Eingabefeld.</summary>
    public static TextField TextRow(string title, string text, bool password = false, Action<string>? changed = null, string? placeholder = null)
    {
        Gtk.Editable editable;
        Gtk.Widget row;
        if (AdwAtLeast(2))
        {
            Adw.EntryRow er = password ? Adw.PasswordEntryRow.New() : Adw.EntryRow.New();
            er.UseMarkup = false;
            er.Title = title;
            row = er;
            editable = er;
        }
        else
        {
            var ar = Row(title);
            Gtk.Widget entry;
            if (password)
            {
                var pe = Gtk.PasswordEntry.New();
                pe.ShowPeekIcon = true;
                entry = pe;
                editable = pe;
            }
            else
            {
                var e = Gtk.Entry.New();
                if (placeholder != null) e.PlaceholderText = placeholder;
                entry = e;
                editable = e;
            }
            entry.Valign = Gtk.Align.Center;
            entry.WidthRequest = 300;
            ar.AddSuffix(entry);
            row = ar;
        }
        editable.SetText(text);
        bool quiet = false;
        if (changed != null)
        {
            var commit = Debounce(600, () => changed(editable.GetText()));
            editable.OnChanged += (_, _) => { if (!quiet) commit(); };
        }
        return new TextField(row, () => editable.GetText() ?? "", t => { quiet = true; editable.SetText(t); quiet = false; });
    }

    /// <summary>Mehrzeiliges Textfeld mit Rahmen.</summary>
    public static Gtk.TextView TextArea(string text, int minHeight, out Gtk.Widget framed)
    {
        var tv = Gtk.TextView.New();
        tv.WrapMode = Gtk.WrapMode.WordChar;
        tv.Buffer!.Text = text;
        tv.TopMargin = tv.BottomMargin = 10;
        tv.LeftMargin = tv.RightMargin = 12;
        var scroll = Gtk.ScrolledWindow.New();
        scroll.Child = tv;
        scroll.MinContentHeight = minHeight;
        scroll.HscrollbarPolicy = Gtk.PolicyType.Never;
        scroll.AddCssClass("card");
        scroll.AddCssClass("fl-textarea");
        framed = scroll;
        return tv;
    }

    // ───────────── System

    /// <summary>Webseite, Datei oder Ordner mit dem Standardprogramm öffnen.</summary>
    public static void Open(string target)
    {
        var opener = Desktop.Which("xdg-open") ?? Desktop.Which("gio");
        if (opener == null) throw new FluesterException("Kein xdg-open gefunden – " + target);
        var psi = new ProcessStartInfo(opener) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        if (opener.EndsWith("gio")) psi.ArgumentList.Add("open");
        psi.ArgumentList.Add(target);
        Process.Start(psi);
    }

    public static void Copy(string text) => Gdk.Display.GetDefault()?.GetClipboard().SetText(text);

    // ───────────── Aussehen

    public const string Css = """
        .fl-tile { border-radius: 7px; padding: 4px; color: white; -gtk-icon-size: 16px; }
        .fl-tile-big { border-radius: 22px; padding: 18px; color: white; -gtk-icon-size: 48px; }
        .fl-gray { background-image: linear-gradient(#a3a3aa, #6f6e75); }
        .fl-blue { background-image: linear-gradient(#5aa3ff, #1c63c9); }
        .fl-purple { background-image: linear-gradient(#b585f5, #7a35b8); }
        .fl-orange { background-image: linear-gradient(#ffad5c, #e05a00); }
        .fl-brown { background-image: linear-gradient(#c09068, #7f5530); }
        .fl-teal { background-image: linear-gradient(#4fd1c5, #1b8f86); }
        .fl-indigo { background-image: linear-gradient(#8a8ff7, #4a4fc4); }
        .fl-red { background-image: linear-gradient(#ff7a6b, #c01c28); }
        .fl-green { background-image: linear-gradient(#57e389, #26a269); }
        .fl-brand { background-image: linear-gradient(60deg, #0f1233, #4f29c7 62%, #db409e); }
        .fl-stat { padding: 14px 18px; min-width: 150px; }
        .fl-stat-value { font-size: 20pt; font-weight: 800; }
        .fl-dot { font-size: 9pt; }
        .fl-dot-on { color: #2ec27e; }
        .fl-dot-busy { color: #e5a50a; }
        .fl-dot-off { color: alpha(currentColor, 0.35); }
        .fl-hero { font-size: 22pt; font-weight: 800; }
        .fl-step { font-size: 9pt; font-weight: 700; letter-spacing: 1px; }
        .fl-textarea textview, .fl-textarea text { background: transparent; }
        .fl-sidebar-status { padding: 10px 14px; }
        levelbar.fl-level trough { min-height: 8px; }
        """;

    public static void LoadCss()
    {
        var display = Gdk.Display.GetDefault();
        if (display == null) return;
        var provider = Gtk.CssProvider.New();
        provider.LoadFromData(Css, -1);
        Gtk.StyleContext.AddProviderForDisplay(display, provider, 600);
    }

    /// <summary>Farbiges Symbol wie in den Systemeinstellungen am Mac.</summary>
    public static Gtk.Image Tile(string icon, string color, bool big = false)
    {
        var i = Gtk.Image.NewFromIconName(icon);
        i.AddCssClass(big ? "fl-tile-big" : "fl-tile");
        i.AddCssClass("fl-" + color);
        i.Valign = Gtk.Align.Center;
        return i;
    }
}
