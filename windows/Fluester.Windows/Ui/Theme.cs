using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Fluester.Windows.Ui;

/// <summary>Farben und Schriften passend zum Windows-Design (hell/dunkel), angelehnt an Windows 11.</summary>
static class Theme
{
    public static bool Dark
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { return false; }
        }
    }

    public static Color Window => Dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
    public static Color Card => Dark ? Color.FromArgb(43, 43, 43) : Color.White;
    public static Color Sidebar => Dark ? Color.FromArgb(28, 28, 28) : Color.FromArgb(235, 235, 238);
    public static Color Text => Dark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(26, 26, 26);
    public static Color Secondary => Dark ? Color.FromArgb(170, 170, 170) : Color.FromArgb(96, 96, 96);
    public static Color Border => Dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(222, 222, 222);
    public static Color Accent => Color.FromArgb(91, 61, 214);       // Violett aus dem App-Symbol
    public static Color AccentHover => Color.FromArgb(111, 84, 230);
    public static Color Selected => Dark ? Color.FromArgb(55, 55, 60) : Color.FromArgb(221, 221, 228);
    public static Color Good => Color.FromArgb(52, 199, 89);
    public static Color Warn => Color.FromArgb(255, 159, 10);

    static string? family;
    public static string Family
    {
        get
        {
            if (family != null) return family;
            using var fonts = new System.Drawing.Text.InstalledFontCollection();
            return family = fonts.Families.Any(f => f.Name == "Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        }
    }

    public static Font Body => new(Family, 10f);
    public static Font Small => new(Family, 9f);
    public static Font Bold => new(Family, 10f, FontStyle.Bold);
    public static Font Title => new(Family.Replace(" Text", " Display"), 20f, FontStyle.Bold);
    public static Font Heading => new(Family, 12.5f, FontStyle.Bold);

    /// <summary>Formular einfärben, dunkle Titelleiste setzen.</summary>
    public static void Apply(Form f)
    {
        f.BackColor = Window;
        f.ForeColor = Text;
        f.Font = Body;
        f.HandleCreated += (_, _) => Native.UseDarkTitleBar(f.Handle, Dark);
    }

    public static Button PrimaryButton(string text)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.White,
            Font = Bold, Padding = new Padding(14, 5, 14, 5), Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = AccentHover;
        return b;
    }

    public static Button SecondaryButton(string text)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Card, ForeColor = Text,
            Padding = new Padding(10, 3, 10, 3), Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderColor = Border;
        b.FlatAppearance.MouseOverBackColor = Selected;
        return b;
    }

    /// <summary>Dunkles Kontextmenü (Tray).</summary>
    public sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { RoundedEdges = true; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : Secondary;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Text;
            base.OnRenderArrow(e);
        }

        sealed class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Card;
            public override Color ImageMarginGradientBegin => Card;
            public override Color ImageMarginGradientMiddle => Card;
            public override Color ImageMarginGradientEnd => Card;
            public override Color MenuBorder => Border;
            public override Color MenuItemBorder => Selected;
            public override Color MenuItemSelected => Selected;
            public override Color MenuItemSelectedGradientBegin => Selected;
            public override Color MenuItemSelectedGradientEnd => Selected;
            public override Color SeparatorDark => Border;
            public override Color SeparatorLight => Border;
            public override Color CheckBackground => Selected;
            public override Color CheckSelectedBackground => Selected;
        }
    }
}
