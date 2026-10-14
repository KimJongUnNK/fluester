using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using static Fluester.Windows.Native;

namespace Fluester.Windows;

/// <summary>Was die Leiste gerade zeigt.</summary>
enum BarPhase { Hidden, Idle, Listening, Processing, Done, Error }

/// <summary>Die „Flow-Leiste“: schwarze Pille unten in der Bildschirmmitte mit Live-Wellenform.
/// Layered Window mit Alpha pro Pixel – weiche Kanten, kein Fokus, Klicks gehen durch.</summary>
sealed class FlowBar : System.Windows.Forms.Form
{
    BarPhase phase = BarPhase.Hidden;
    string message = "";
    bool handsFree, command;
    readonly float[] levels = new float[30];
    int levelIndex;
    double startedAt;
    readonly System.Windows.Forms.Timer anim = new() { Interval = 33 };
    readonly System.Windows.Forms.Timer hide = new();
    public bool ShowWhenIdle { get; set; }

    public FlowBar()
    {
        FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = System.Windows.Forms.FormStartPosition.Manual;
        anim.Tick += (_, _) => Render();
        hide.Tick += (_, _) => { hide.Stop(); Idle(); };
    }

    protected override bool ShowWithoutActivation => true;

    protected override System.Windows.Forms.CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public BarPhase Phase => phase;

    public void Listening(bool handsFree, bool command)
    {
        this.handsFree = handsFree;
        this.command = command;
        if (phase != BarPhase.Listening)
        {
            Array.Clear(levels);
            startedAt = KeyboardHook.Clock.Elapsed.TotalSeconds;
        }
        Set(BarPhase.Listening, "");
    }

    public void PushLevel(float level)
    {
        levels[levelIndex] = level;
        levelIndex = (levelIndex + 1) % levels.Length;
    }

    public void Processing(bool command) { this.command = command; Set(BarPhase.Processing, command ? "Denkt nach …" : "Schreibt …"); }

    public void Done(string text, double autoHide = 1.0) { Set(BarPhase.Done, text); AutoHide(autoHide); }

    public void Error(string text, double autoHide = 3.5) { Set(BarPhase.Error, text); AutoHide(autoHide); }

    public void Idle() => Set(ShowWhenIdle ? BarPhase.Idle : BarPhase.Hidden, "");

    void AutoHide(double seconds)
    {
        hide.Stop();
        hide.Interval = (int)(seconds * 1000);
        hide.Start();
    }

    void Set(BarPhase p, string text)
    {
        if (p is BarPhase.Listening or BarPhase.Processing) hide.Stop();
        phase = p;
        message = text;
        if (p == BarPhase.Hidden)
        {
            anim.Stop();
            if (Visible) Hide();
            return;
        }
        if (!IsHandleCreated) CreateHandle();
        Render();
        if (!Visible) Show();
        if (p is BarPhase.Listening or BarPhase.Processing) anim.Start(); else anim.Stop();
    }

    float Dpi => DeviceDpi / 96f;

    void Render()
    {
        if (!IsHandleCreated) return;
        float s = Dpi;
        using var font = new Font(FontName, 12.5f * s, FontStyle.Bold, GraphicsUnit.Pixel);
        int h = (int)(40 * s);
        int w = phase switch
        {
            BarPhase.Idle => (int)(56 * s),
            BarPhase.Listening => (int)(236 * s),
            _ => Math.Min((int)(560 * s), (int)(56 * s + MeasureText(message, font))),
        };
        int idleH = (int)(10 * s);
        if (phase == BarPhase.Idle) h = idleH;

        var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        int x = area.Left + (area.Width - w) / 2;
        int y = area.Bottom - h - (int)(22 * s);

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using var pill = Pill(new RectangleF(0.5f, 0.5f, w - 1, h - 1));
            using (var bg = new SolidBrush(Color.FromArgb(phase == BarPhase.Idle ? 150 : 236, 14, 14, 18))) g.FillPath(bg, pill);
            using (var border = new Pen(Color.FromArgb(60, 255, 255, 255), 1f)) g.DrawPath(border, pill);
            if (phase != BarPhase.Idle) DrawContent(g, w, h, s, font);
        }
        Blit(bmp, x, y);
    }

    void DrawContent(Graphics g, int w, int h, float s, Font font)
    {
        float cy = h / 2f;
        float left = 16 * s;
        var accent = command ? Color.FromArgb(180, 140, 255) : Color.FromArgb(255, 72, 88);
        double t = KeyboardHook.Clock.Elapsed.TotalSeconds;
        switch (phase)
        {
            case BarPhase.Listening:
            {
                // Pulsierender Punkt (rot) bzw. Funke (Command Mode, violett)
                float pulse = 4.5f * s + (float)(Math.Sin(t * 6) + 1) * 1.2f * s;
                using (var b = new SolidBrush(accent)) g.FillEllipse(b, left - pulse + 4 * s, cy - pulse, pulse * 2, pulse * 2);
                // Wellenform
                float bx = left + 22 * s, bw = 3f * s, gap = 2.6f * s, maxH = h - 16 * s;
                using var bar = new SolidBrush(Color.FromArgb(240, 255, 255, 255));
                for (int i = 0; i < levels.Length; i++)
                {
                    float lv = levels[(levelIndex + i) % levels.Length];
                    float bh = Math.Max(3 * s, lv * maxH);
                    using var path = Pill(new RectangleF(bx + i * (bw + gap), cy - bh / 2, bw, bh));
                    g.FillPath(bar, path);
                }
                // Zeit oder „freihändig“
                double secs = t - startedAt;
                string label = handsFree ? (secs >= 3 ? $"{(int)secs / 60}:{(int)secs % 60:00}" : "frei") : $"{(int)secs / 60}:{(int)secs % 60:00}";
                using var tb = new SolidBrush(Color.FromArgb(170, 255, 255, 255));
                var size = g.MeasureString(label, font);
                g.DrawString(label, font, tb, w - size.Width - 12 * s, cy - size.Height / 2);
                break;
            }
            case BarPhase.Processing:
            {
                for (int i = 0; i < 3; i++)
                {
                    float a = (float)(0.35 + 0.65 * Math.Max(0, Math.Sin(t * 5 - i * 0.7)));
                    using var b = new SolidBrush(Color.FromArgb((int)(a * 255), accent));
                    g.FillEllipse(b, left + i * 9 * s, cy - 3 * s, 6 * s, 6 * s);
                }
                DrawText(g, message, font, left + 32 * s, cy, Color.White);
                break;
            }
            case BarPhase.Done:
            case BarPhase.Error:
            {
                var c = phase == BarPhase.Done ? Color.FromArgb(52, 199, 89) : Color.FromArgb(255, 159, 10);
                using (var b = new SolidBrush(c)) g.FillEllipse(b, left, cy - 9 * s, 18 * s, 18 * s);
                using (var pen = new Pen(Color.Black, 2.2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    if (phase == BarPhase.Done)
                        g.DrawLines(pen, new[] { new PointF(left + 5 * s, cy), new PointF(left + 8 * s, cy + 3.5f * s), new PointF(left + 13 * s, cy - 3.5f * s) });
                    else
                    {
                        g.DrawLine(pen, left + 9 * s, cy - 5 * s, left + 9 * s, cy + 1 * s);
                        g.DrawLine(pen, left + 9 * s, cy + 4.5f * s, left + 9 * s, cy + 4.6f * s);
                    }
                }
                DrawText(g, message, font, left + 28 * s, cy, Color.White);
                break;
            }
        }
    }

    static void DrawText(Graphics g, string text, Font font, float x, float cy, Color color)
    {
        using var b = new SolidBrush(color);
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, b, x, cy - size.Height / 2);
    }

    static float MeasureText(string text, Font font)
    {
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        return g.MeasureString(text, font).Width;
    }

    static string? fontName;
    static string FontName
    {
        get
        {
            if (fontName != null) return fontName;
            using var fonts = new InstalledFontCollection();
            return fontName = fonts.Families.Any(f => f.Name == "Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        }
    }

    static GraphicsPath Pill(RectangleF r)
    {
        var p = new GraphicsPath();
        float d = Math.Min(r.Width, r.Height);
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 90, 180);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        p.CloseFigure();
        return p;
    }

    void Blit(Bitmap bmp, int x, int y)
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(mem, hbmp);
        try
        {
            var size = new SIZE(bmp.Width, bmp.Height);
            var src = new POINT(0, 0);
            var dst = new POINT(x, y);
            var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
            UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(hbmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { anim.Dispose(); hide.Dispose(); }
        base.Dispose(disposing);
    }
}
