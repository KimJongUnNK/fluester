using System.Drawing;
using System.Windows.Forms;

namespace Fluester.Windows.Ui;

/// <summary>Kleiner Baukasten für einheitliche Einstellungsseiten: Überschrift, Karten, Zeilen, Hinweise.</summary>
sealed class PageBuilder
{
    public readonly FlowLayoutPanel Root;
    readonly List<Control> cards = new();

    public PageBuilder()
    {
        Root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            Padding = new Padding(28, 22, 28, 28), BackColor = Theme.Window,
        };
        Root.Resize += (_, _) => FitWidths();
    }

    int Inner => Math.Max(300, Root.ClientSize.Width - Root.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4);

    void FitWidths()
    {
        foreach (var c in cards) c.Width = Inner;
    }

    public void Header(string title, string? subtitle = null)
    {
        Root.Controls.Add(new Label { Text = title, Font = Theme.Title, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 4) });
        if (subtitle != null)
            Add(new Label { Text = subtitle, AutoSize = false, Height = 40, ForeColor = Theme.Secondary, Margin = new Padding(0, 0, 0, 10) });
    }

    public Label Section(string title)
    {
        var l = new Label { Text = title, Font = Theme.Heading, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 14, 0, 6) };
        Root.Controls.Add(l);
        return l;
    }

    /// <summary>Weiße bzw. dunkle Karte mit Zeilen untereinander.</summary>
    public Card Card()
    {
        var c = new Card();
        Add(c);
        return c;
    }

    public T Add<T>(T control) where T : Control
    {
        control.Width = Inner;
        cards.Add(control);
        Root.Controls.Add(control);
        return control;
    }

    public Label Note(string text)
    {
        var l = new Label { Text = text, ForeColor = Theme.Secondary, Font = Theme.Small, AutoSize = false, Margin = new Padding(2, 4, 0, 4) };
        l.Height = TextRenderer.MeasureText(text, l.Font, new Size(700, 0), TextFormatFlags.WordBreak).Height + 6;
        return Add(l);
    }
}

/// <summary>Karte: Zeilen mit Beschriftung links und Bedienelement rechts.</summary>
class Card : TableLayoutPanel
{
    public Card()
    {
        BackColor = Theme.Card;
        ColumnCount = 2;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16, 10, 16, 10);
        Margin = new Padding(0, 0, 0, 10);
        CellBorderStyle = TableLayoutPanelCellBorderStyle.None;
    }

    /// <summary>Zeile: Titel (+ kleiner Hilfetext) links, Steuerelement rechts.</summary>
    public T Row<T>(string title, T control, string? help = null) where T : Control
    {
        var left = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 8, 6), BackColor = Color.Transparent };
        left.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0) });
        if (help != null) left.Controls.Add(new Label { Text = help, AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = Theme.Secondary, Font = Theme.Small, Margin = new Padding(0, 2, 0, 0) });
        control.Anchor = AnchorStyles.Right;
        control.Margin = new Padding(0, 6, 0, 6);
        int r = RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(left, 0, r);
        Controls.Add(control, 1, r);
        return control;
    }

    /// <summary>Zeile über die ganze Breite.</summary>
    public T Full<T>(T control) where T : Control
    {
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 6, 0, 6);
        int r = RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(control, 0, r);
        SetColumnSpan(control, 2);
        return control;
    }

    public CheckBox Check(string text, bool value, Action<bool> changed, string? help = null)
    {
        var cb = new CheckBox { Text = text, Checked = value, AutoSize = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
        cb.CheckedChanged += (_, _) => changed(cb.Checked);
        if (help == null) return Full(cb);
        var box = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Color.Transparent };
        box.Controls.Add(cb);
        box.Controls.Add(new Label { Text = help, AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = Theme.Secondary, Font = Theme.Small, Margin = new Padding(20, 0, 0, 2) });
        Full(box);
        return cb;
    }
}

static class Controls2
{
    public static ComboBox Combo<T>(IEnumerable<T> items, Func<T, string> label, T selected, Action<T> changed, int width = 260)
    {
        var list = items.ToList();
        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, FlatStyle = FlatStyle.Flat };
        foreach (var i in list) cb.Items.Add(label(i));
        int idx = list.FindIndex(i => EqualityComparer<T>.Default.Equals(i, selected));
        cb.SelectedIndex = idx >= 0 ? idx : (list.Count > 0 ? 0 : -1);
        cb.SelectedIndexChanged += (_, _) => { if (cb.SelectedIndex >= 0) changed(list[cb.SelectedIndex]); };
        return cb;
    }

    public static LinkLabel Link(string text, string url)
    {
        var l = new LinkLabel { Text = text, AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentHover, BackColor = Color.Transparent };
        l.LinkClicked += (_, _) => Open(url);
        return l;
    }

    public static void Open(string target)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) { Fluester.Engine.Log.Write("Öffnen: " + e.Message); }
    }

    public static FlowLayoutPanel Line(params Control[] controls)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
        foreach (var c in controls) { c.Margin = new Padding(0, 0, 8, 0); p.Controls.Add(c); }
        return p;
    }
}
