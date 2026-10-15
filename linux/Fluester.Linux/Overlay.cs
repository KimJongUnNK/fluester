using System.Diagnostics;
using System.Runtime.InteropServices;
using Fluester.Engine;

namespace Fluester.Linux;

/// <summary>Die Flow-Leiste unter Linux – wie auf Mac und Windows: schwarze Pille unten in der Bildschirmmitte mit
/// Live-Wellenform, „Schreibt …“, „✓ 12 Wörter“ oder Fehler.
/// Technisch ein X11-Fenster ohne Fensterverwaltung (override-redirect, wie Tooltips und Menüs): liegt immer oben,
/// nimmt nie den Fokus (der Text landet weiter im richtigen Fenster), Klicks gehen durch. Läuft unter X11 und unter
/// Wayland über Xwayland (GNOME, KDE, Sway, Hyprland). Gezeichnet mit cairo, mit Transparenz.
/// Alle X11-Aufrufe laufen auf einem eigenen Thread; die öffentlichen Methoden sind von überall aufrufbar.</summary>
sealed class Overlay : IDisposable
{
    public enum Phase { Hidden, Idle, Listening, Processing, Done, Error }

    /// <summary>Breite beim Zuhören: Punkt, 30 Pegelbalken und die Dauer haben Platz.</summary>
    const int ListeningWidth = 266;

    // Gewünschter Zustand – von beliebigen Threads gesetzt, vom UI-Thread gezeichnet
    readonly object gate = new();
    Phase phase = Phase.Hidden;
    string message = "";
    bool handsFree, command;
    double startedAt, hideAt = double.MaxValue;
    readonly float[] levels = new float[30];
    int levelIndex;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly AutoResetEvent wake = new(false);
    volatile bool disposed, dead;
    public bool ShowWhenIdle { get; set; }

    /// <summary>Wo die Leiste sitzt (für Protokoll und Selbsttest).</summary>
    public string Where { get; private set; } = "";
    public bool Composited { get; private set; }
    /// <summary>Letzte Lage auf dem Bildschirm (Selbsttest).</summary>
    public (int X, int Y, int W, int H) Bounds { get; private set; }

    IntPtr dpy, visual, surface;
    ulong root, win, colormap;
    int screen, depth;
    bool mapped;
    double scale = 1;

    Overlay() { }

    /// <summary>Leiste anlegen. null + Grund, wenn es keine X11-Anzeige (oder kein cairo) gibt – dann bleiben Benachrichtigungen.</summary>
    public static Overlay? TryCreate(out string reason, bool showWhenIdle = false)
    {
        reason = "";
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            reason = "keine X11-/Xwayland-Anzeige (DISPLAY fehlt)";
            return null;
        }
        var o = new Overlay { ShowWhenIdle = showWhenIdle };
        var ready = new TaskCompletionSource<string?>();
        var t = new Thread(() => o.Run(ready)) { IsBackground = true, Name = "Flow-Leiste" };
        t.Start();
        if (!ready.Task.Wait(4000)) { reason = "Anzeige antwortet nicht"; o.disposed = true; return null; }
        if (ready.Task.Result is { } err) { reason = err; return null; }
        return o;
    }

    // ───────────── Zustand setzen (beliebiger Thread)

    public void Listening(bool handsFree, bool command)
    {
        lock (gate)
        {
            if (phase != Phase.Listening) { Array.Clear(levels); startedAt = Now; }
            this.handsFree = handsFree;
            this.command = command;
            Set(Phase.Listening, "", double.MaxValue);
        }
    }

    public void PushLevel(float level)
    {
        lock (gate)
        {
            levels[levelIndex] = level;
            levelIndex = (levelIndex + 1) % levels.Length;
        }
    }

    public void Processing(bool command) { lock (gate) { this.command = command; Set(Phase.Processing, command ? "Denkt nach …" : "Schreibt …", double.MaxValue); } }

    public void Done(string text, double seconds = 1.4) { lock (gate) Set(Phase.Done, text, seconds); }

    public void Error(string text, double seconds = 4) { lock (gate) Set(Phase.Error, text, seconds); }

    public void Idle() { lock (gate) Set(ShowWhenIdle ? Phase.Idle : Phase.Hidden, "", double.MaxValue); }

    public Phase Current { get { lock (gate) return phase; } }

    /// <summary>false, wenn die Verbindung zur Anzeige verloren ging (dann übernehmen Benachrichtigungen).</summary>
    public bool Alive => !dead && !disposed;

    double Now => clock.Elapsed.TotalSeconds;

    void Set(Phase p, string text, double autoHide)
    {
        phase = p;
        message = text.Length > 90 ? text[..88] + " …" : text;
        hideAt = autoHide == double.MaxValue ? double.MaxValue : Now + autoHide;
        wake.Set();
    }

    // ───────────── UI-Thread

    void Run(TaskCompletionSource<string?> ready)
    {
        try
        {
            Init();
            ready.SetResult(null);
        }
        catch (Exception e)
        {
            ready.SetResult(e is InvalidOperationException ? e.Message
                : e is DllNotFoundException or EntryPointNotFoundException ? "Bibliothek fehlt (" + e.Message.Split('\n')[0] + ")"
                : "Fehler: " + e.Message);
            return;
        }
        while (!disposed && !dead)
        {
            Phase p;
            double wait;
            lock (gate)
            {
                if (Now >= hideAt) { phase = ShowWhenIdle ? Phase.Idle : Phase.Hidden; message = ""; hideAt = double.MaxValue; }
                p = phase;
                wait = p is Phase.Listening or Phase.Processing ? 0.033 : hideAt == double.MaxValue ? -1 : Math.Max(0.01, hideAt - Now);
            }
            try { Render(); }
            catch (Exception e) { Log.Write("Flow-Leiste: " + e.Message); }
            if (wait < 0) wake.WaitOne(); else wake.WaitOne(TimeSpan.FromSeconds(wait));
        }
        try
        {
            if (surface != IntPtr.Zero) Cairo.cairo_surface_destroy(surface);
            if (win != 0) X.XDestroyWindow(dpy, win);
            if (dpy != IntPtr.Zero) X.XCloseDisplay(dpy);
        }
        catch { }
    }

    // Xlib beendet sonst den ganzen Prozess bei Fehlern
    static readonly X.ErrorHandler OnError = (_, ev) =>
    {
        var e = Marshal.PtrToStructure<X.XErrorEvent>(ev);
        Log.Write($"Flow-Leiste: X11-Fehler {e.error_code} (Anfrage {e.request_code})");
        return 0;
    };
    static Overlay? current;
    static readonly X.IOErrorHandler OnIOError = _ =>
    {
        // Verbindung zur Anzeige weg (Abmelden, Xwayland beendet): Leiste aufgeben, Flüster läuft weiter
        if (current != null) current.dead = true;
        Log.Write("Flow-Leiste: Verbindung zur Anzeige verloren");
        Thread.Sleep(Timeout.Infinite);
        return 0;
    };

    void Init()
    {
        X.XInitThreads();
        dpy = X.XOpenDisplay(IntPtr.Zero);
        if (dpy == IntPtr.Zero) throw new InvalidOperationException("Anzeige " + Environment.GetEnvironmentVariable("DISPLAY") + " nicht erreichbar");
        current = this;
        X.XSetErrorHandler(OnError);
        X.XSetIOErrorHandler(OnIOError);
        screen = X.XDefaultScreen(dpy);
        root = X.XRootWindow(dpy, screen);
        Composited = X.XGetSelectionOwner(dpy, X.XInternAtom(dpy, "_NET_WM_CM_S" + screen, false)) != 0;
        scale = ReadScale();

        // 32-Bit-Visual = Transparenz (weiche Kanten); sonst Standard
        if (X.XMatchVisualInfo(dpy, screen, 32, 4 /* TrueColor */, out var vi) != 0) { visual = vi.visual; depth = 32; }
        else { visual = X.XDefaultVisual(dpy, screen); depth = X.XDefaultDepth(dpy, screen); }
        colormap = X.XCreateColormap(dpy, root, visual, 0);
        var attrs = new X.XSetWindowAttributes { override_redirect = 1, colormap = colormap, background_pixel = 0, border_pixel = 0 };
        const ulong mask = (1 << 1) | (1 << 3) | (1 << 9) | (1 << 13); // CWBackPixel | CWBorderPixel | CWOverrideRedirect | CWColormap
        win = X.XCreateWindow(dpy, root, 0, 0, 10, 10, 0, depth, 1 /* InputOutput */, visual, mask, ref attrs);
        if (win == 0) throw new InvalidOperationException("Fenster ließ sich nicht anlegen");
        X.XStoreName(dpy, win, "Flüster");
        // Als Benachrichtigung kennzeichnen (Compositoren legen sie nach oben, ohne Schatten/Animation eines Fensters)
        SetAtomProperty("_NET_WM_WINDOW_TYPE", "_NET_WM_WINDOW_TYPE_NOTIFICATION");
        // Klicks gehen durch die Leiste hindurch
        try { X.XShapeCombineRectangles(dpy, win, 2 /* ShapeInput */, 0, 0, IntPtr.Zero, 0, 0 /* ShapeSet */, 0); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        surface = Cairo.cairo_xlib_surface_create(dpy, win, visual, 10, 10);
        X.XFlush(dpy);
        var (mx, my, mw, mh, name) = PrimaryMonitor();
        Where = $"{name} {mw}×{mh}{(Composited ? "" : ", ohne Compositor")}{(scale != 1 ? $", Skalierung {scale:0.##}" : "")}";
    }

    void SetAtomProperty(string property, string value)
    {
        var atom = X.XInternAtom(dpy, value, false);
        var data = new[] { (long)atom };
        X.XChangeProperty(dpy, win, X.XInternAtom(dpy, property, false), 4 /* XA_ATOM */, 32, 0 /* PropModeReplace */, data, 1);
    }

    /// <summary>Skalierung aus Xft.dpi (X11-HiDPI); unter Xwayland meist 1 (der Compositor skaliert selbst).</summary>
    double ReadScale()
    {
        try
        {
            var p = X.XResourceManagerString(dpy);
            if (p == IntPtr.Zero) return 1;
            var res = Marshal.PtrToStringUTF8(p) ?? "";
            var line = res.Split('\n').FirstOrDefault(l => l.StartsWith("Xft.dpi:"));
            if (line != null && double.TryParse(line[8..].Trim(), System.Globalization.CultureInfo.InvariantCulture, out var dpi) && dpi > 0)
                return Math.Clamp(Math.Round(dpi / 96 * 4) / 4, 1, 3);
        }
        catch { }
        return 1;
    }

    /// <summary>Hauptbildschirm (XRandR), sonst der ganze X-Bildschirm. Unten wird ein Panel (z. B. KDE) berücksichtigt.</summary>
    (int X, int Y, int W, int H, string Name) PrimaryMonitor()
    {
        int rootW = X.XDisplayWidth(dpy, screen), rootH = X.XDisplayHeight(dpy, screen);
        (int x, int y, int w, int h, string name) m = (0, 0, rootW, rootH, "Bildschirm");
        try
        {
            var list = X.XRRGetMonitors(dpy, root, true, out int n);
            if (list != IntPtr.Zero)
            {
                int size = Marshal.SizeOf<X.XRRMonitorInfo>();
                X.XRRMonitorInfo? pick = null;
                for (int i = 0; i < n; i++)
                {
                    var info = Marshal.PtrToStructure<X.XRRMonitorInfo>(list + i * size);
                    if (pick == null || info.primary != 0) pick = info;
                    if (info.primary != 0) break;
                }
                if (pick is { } p && p.width > 0 && p.height > 0)
                {
                    var namePtr = X.XGetAtomName(dpy, p.name);
                    m = (p.x, p.y, p.width, p.height, namePtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(namePtr) ?? "Monitor" : "Monitor");
                    if (namePtr != IntPtr.Zero) X.XFree(namePtr);
                }
                X.XRRFreeMonitors(list);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        // Arbeitsbereich (ohne Panels), wenn der Monitor unten am Rand liegt
        try
        {
            // Ohne Fenstermanager gibt es das Atom nicht – dann gar nicht erst fragen
            var workarea = X.XInternAtom(dpy, "_NET_WORKAREA", true);
            if (workarea != 0 && X.XGetWindowProperty(dpy, root, workarea, 0, 4, false, 6 /* XA_CARDINAL */,
                    out _, out int format, out ulong items, out _, out IntPtr prop) == 0 && prop != IntPtr.Zero)
            {
                if (format == 32 && items >= 4)
                {
                    long wy = Marshal.ReadInt64(prop, 8), wh = Marshal.ReadInt64(prop, 24);
                    int workBottom = (int)(wy + wh);
                    if (m.y + m.h == rootH && workBottom < rootH && workBottom > m.y + m.h / 2) m.h = workBottom - m.y;
                }
                X.XFree(prop);
            }
        }
        catch { }
        return m;
    }

    // ───────────── Zeichnen

    void Render()
    {
        Phase p;
        string msg;
        bool hf, cmd;
        double since;
        float[] lv = new float[levels.Length];
        lock (gate)
        {
            p = phase; msg = message; hf = handsFree; cmd = command; since = Now - startedAt;
            for (int i = 0; i < lv.Length; i++) lv[i] = levels[(levelIndex + i) % levels.Length];
        }
        if (p == Phase.Hidden)
        {
            if (mapped) { X.XUnmapWindow(dpy, win); X.XFlush(dpy); mapped = false; }
            return;
        }
        double s = scale;
        const string font = "Sans";
        double fontSize = 13 * s;
        msg = Fit(msg, 500 * s, font, fontSize);
        int h = (int)Math.Round(40 * s);
        int w = p switch
        {
            Phase.Idle => (int)(56 * s),
            Phase.Listening => (int)(ListeningWidth * s),
            _ => (int)Math.Min(560 * s, 58 * s + TextWidth(msg, font, fontSize)),
        };
        if (p == Phase.Idle) h = (int)(10 * s);
        var (mx, my, mw, mh, _) = PrimaryMonitor();
        int x = mx + (mw - w) / 2, y = my + mh - h - (int)(28 * s);
        if (Bounds != (x, y, w, h))
        {
            X.XMoveResizeWindow(dpy, win, x, y, (uint)w, (uint)h);
            Cairo.cairo_xlib_surface_set_size(surface, w, h);
            Bounds = (x, y, w, h);
        }
        if (!mapped) { X.XMapRaised(dpy, win); mapped = true; }
        else X.XRaiseWindow(dpy, win);

        var cr = Cairo.cairo_create(surface);
        try { Draw(cr, p, msg, hf, cmd, since, lv, w, h, s, font, fontSize); }
        finally { Cairo.cairo_destroy(cr); }
        Cairo.cairo_surface_flush(surface);
        X.XFlush(dpy);
    }

    /// <summary>Zeichnet die Leiste. Statisch und ohne X11, damit der Selbsttest dasselbe Bild als PNG prüfen kann.</summary>
    internal void Draw(IntPtr cr, Phase p, string msg, bool hf, bool cmd, double since, float[] lv, int w, int h, double s, string font, double fontSize)
    {
        double t = Now;
        // Doppelt gepuffert: erst fertig malen, dann in einem Rutsch auf das Fenster
        Cairo.cairo_push_group(cr);
        Cairo.cairo_set_operator(cr, Cairo.OPERATOR_SOURCE);
        Cairo.cairo_set_source_rgba(cr, 0, 0, 0, 0);
        Cairo.cairo_paint(cr);
        Cairo.cairo_set_operator(cr, Cairo.OPERATOR_OVER);
        Pill(cr, 0.5, 0.5, w - 1, h - 1);
        Cairo.cairo_set_source_rgba(cr, 14 / 255.0, 14 / 255.0, 18 / 255.0, p == Phase.Idle ? 0.6 : 0.93);
        Cairo.cairo_fill_preserve(cr);
        Cairo.cairo_set_source_rgba(cr, 1, 1, 1, 0.24);
        Cairo.cairo_set_line_width(cr, 1);
        Cairo.cairo_stroke(cr);

        double cy = h / 2.0, left = 16 * s;
        var (ar, ag, ab) = cmd ? (180 / 255.0, 140 / 255.0, 1.0) : (1.0, 72 / 255.0, 88 / 255.0);
        Cairo.cairo_select_font_face(cr, font, 0, 1 /* bold */);
        Cairo.cairo_set_font_size(cr, fontSize);
        switch (p)
        {
            case Phase.Listening:
            {
                // Pulsierender Punkt (rot) bzw. Command Mode (violett)
                double pulse = 4.5 * s + (Math.Sin(t * 6) + 1) * 1.2 * s;
                Cairo.cairo_set_source_rgba(cr, ar, ag, ab, 1);
                Cairo.cairo_arc(cr, left + 4 * s, cy, pulse, 0, 2 * Math.PI);
                Cairo.cairo_fill(cr);
                // Wellenform
                double bx = left + 22 * s, bw = 3 * s, gap = 2.6 * s, maxH = h - 16 * s;
                Cairo.cairo_set_source_rgba(cr, 1, 1, 1, 0.94);
                for (int i = 0; i < lv.Length; i++)
                {
                    double bh = Math.Max(3 * s, lv[i] * maxH);
                    Pill(cr, bx + i * (bw + gap), cy - bh / 2, bw, bh);
                    Cairo.cairo_fill(cr);
                }
                // Dauer oder „frei“ (freihändig)
                string label = hf && since < 3 ? "frei" : $"{(int)since / 60}:{(int)since % 60:00}";
                double lw = TextWidth(cr, label);
                Text(cr, label, w - lw - 13 * s, cy, 1, 1, 1, 0.66);
                break;
            }
            case Phase.Processing:
            {
                for (int i = 0; i < 3; i++)
                {
                    double a = 0.35 + 0.65 * Math.Max(0, Math.Sin(t * 5 - i * 0.7));
                    Cairo.cairo_set_source_rgba(cr, ar, ag, ab, a);
                    Cairo.cairo_arc(cr, left + 3 * s + i * 9 * s, cy, 3 * s, 0, 2 * Math.PI);
                    Cairo.cairo_fill(cr);
                }
                Text(cr, msg, left + 32 * s, cy, 1, 1, 1, 1);
                break;
            }
            case Phase.Done:
            case Phase.Error:
            {
                bool ok = p == Phase.Done;
                Cairo.cairo_set_source_rgba(cr, ok ? 52 / 255.0 : 1, ok ? 199 / 255.0 : 159 / 255.0, ok ? 89 / 255.0 : 10 / 255.0, 1);
                Cairo.cairo_arc(cr, left + 9 * s, cy, 9 * s, 0, 2 * Math.PI);
                Cairo.cairo_fill(cr);
                Cairo.cairo_set_source_rgba(cr, 0, 0, 0, 1);
                Cairo.cairo_set_line_width(cr, 2.2 * s);
                Cairo.cairo_set_line_cap(cr, 1 /* round */);
                if (ok)
                {
                    Cairo.cairo_move_to(cr, left + 5 * s, cy);
                    Cairo.cairo_line_to(cr, left + 8 * s, cy + 3.5 * s);
                    Cairo.cairo_line_to(cr, left + 13 * s, cy - 3.5 * s);
                }
                else
                {
                    Cairo.cairo_move_to(cr, left + 9 * s, cy - 5 * s);
                    Cairo.cairo_line_to(cr, left + 9 * s, cy + 1 * s);
                    Cairo.cairo_move_to(cr, left + 9 * s, cy + 4.5 * s);
                    Cairo.cairo_line_to(cr, left + 9 * s, cy + 4.6 * s);
                }
                Cairo.cairo_stroke(cr);
                Text(cr, msg, left + 28 * s, cy, 1, 1, 1, 1);
                break;
            }
        }
        Cairo.cairo_pop_group_to_source(cr);
        Cairo.cairo_set_operator(cr, Cairo.OPERATOR_SOURCE);
        Cairo.cairo_paint(cr);
    }

    /// <summary>Abgerundetes Rechteck mit Radius = halbe kurze Seite – waagrecht die Leiste, senkrecht die Pegelbalken.</summary>
    static void Pill(IntPtr cr, double x, double y, double w, double h)
    {
        double r = Math.Min(w, h) / 2;
        Cairo.cairo_new_path(cr);
        Cairo.cairo_arc(cr, x + w - r, y + r, r, -Math.PI / 2, 0);
        Cairo.cairo_arc(cr, x + w - r, y + h - r, r, 0, Math.PI / 2);
        Cairo.cairo_arc(cr, x + r, y + h - r, r, Math.PI / 2, Math.PI);
        Cairo.cairo_arc(cr, x + r, y + r, r, Math.PI, 3 * Math.PI / 2);
        Cairo.cairo_close_path(cr);
    }

    static void Text(IntPtr cr, string text, double x, double cy, double r, double g, double b, double a)
    {
        Cairo.cairo_text_extents(cr, "Ag", out var ext);
        Cairo.cairo_set_source_rgba(cr, r, g, b, a);
        Cairo.cairo_move_to(cr, x, cy - ext.y_bearing - ext.height / 2);
        Cairo.cairo_show_text(cr, text);
    }

    static double TextWidth(IntPtr cr, string text)
    {
        Cairo.cairo_text_extents(cr, text, out var ext);
        return ext.x_advance;
    }

    /// <summary>Text kürzen, bis er in die Leiste passt („…“ am Ende).</summary>
    static string Fit(string text, double maxWidth, string font, double size)
    {
        if (text.Length == 0 || TextWidth(text, font, size) <= maxWidth) return text;
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (TextWidth(text[..mid].TrimEnd() + " …", font, size) <= maxWidth) lo = mid; else hi = mid - 1;
        }
        return text[..lo].TrimEnd() + " …";
    }

    /// <summary>Textbreite ohne Fenster (für die Größe der Leiste).</summary>
    static double TextWidth(string text, string font, double size)
    {
        var img = Cairo.cairo_image_surface_create(0, 1, 1);
        var cr = Cairo.cairo_create(img);
        try
        {
            Cairo.cairo_select_font_face(cr, font, 0, 1);
            Cairo.cairo_set_font_size(cr, size);
            return TextWidth(cr, text);
        }
        finally { Cairo.cairo_destroy(cr); Cairo.cairo_surface_destroy(img); }
    }

    /// <summary>Selbsttest: einen Zustand ohne Bildschirm als PNG zeichnen.</summary>
    public static bool RenderPng(string path, Phase p, string msg, bool command = false)
    {
        var o = new Overlay();
        const string font = "Sans";
        double s = 1, fontSize = 13;
        int h = p == Phase.Idle ? 10 : 40;
        int w = p switch { Phase.Idle => 56, Phase.Listening => ListeningWidth, _ => (int)Math.Min(560, 58 + TextWidth(msg, font, fontSize)) };
        var lv = Enumerable.Range(0, 30).Select(i => (float)(0.25 + 0.6 * Math.Abs(Math.Sin(i * 0.55)))).ToArray();
        var img = Cairo.cairo_image_surface_create(0 /* ARGB32 */, w, h);
        var cr = Cairo.cairo_create(img);
        try
        {
            o.Draw(cr, p, msg, false, command, 7.4, lv, w, h, s, font, fontSize);
            Cairo.cairo_surface_flush(img);
            return Cairo.cairo_surface_write_to_png(img, path) == 0;
        }
        finally { Cairo.cairo_destroy(cr); Cairo.cairo_surface_destroy(img); }
    }

    /// <summary>Selbsttest: ist das Fenster wirklich sichtbar, ohne Fensterverwaltung, und wo?</summary>
    public (bool Viewable, bool OverrideRedirect, int X, int Y, int W, int H)? WindowState()
    {
        if (dead || dpy == IntPtr.Zero) return null;
        if (X.XGetWindowAttributes(dpy, win, out var a) == 0) return null;
        X.XTranslateCoordinates(dpy, win, root, 0, 0, out int rx, out int ry, out _);
        return (a.map_state == 2 /* IsViewable */, a.override_redirect != 0, rx, ry, a.width, a.height);
    }

    public void Dispose()
    {
        disposed = true;
        wake.Set();
    }

    // ───────────── Native Bibliotheken

    static class X
    {
        const string Lib = "libX11.so.6";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int ErrorHandler(IntPtr display, IntPtr errorEvent);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int IOErrorHandler(IntPtr display);

        [StructLayout(LayoutKind.Sequential)]
        public struct XErrorEvent { public int type; public IntPtr display; public ulong resourceid; public ulong serial; public byte error_code; public byte request_code; public byte minor_code; }

        [StructLayout(LayoutKind.Sequential)]
        public struct XVisualInfo { public IntPtr visual; public ulong visualid; public int screen; public int depth; public int @class; public ulong red_mask, green_mask, blue_mask; public int colormap_size, bits_per_rgb; }

        [StructLayout(LayoutKind.Sequential)]
        public struct XSetWindowAttributes
        {
            public ulong background_pixmap, background_pixel, border_pixmap, border_pixel;
            public int bit_gravity, win_gravity, backing_store;
            public ulong backing_planes, backing_pixel;
            public int save_under;
            public long event_mask, do_not_propagate_mask;
            public int override_redirect;
            public ulong colormap, cursor;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XRRMonitorInfo { public ulong name; public int primary, automatic, noutput, x, y, width, height, mwidth, mheight; public IntPtr outputs; }

        [DllImport(Lib)] public static extern int XInitThreads();
        [DllImport(Lib)] public static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport(Lib)] public static extern int XCloseDisplay(IntPtr dpy);
        [DllImport(Lib)] public static extern IntPtr XSetErrorHandler(ErrorHandler handler);
        [DllImport(Lib)] public static extern IntPtr XSetIOErrorHandler(IOErrorHandler handler);
        [DllImport(Lib)] public static extern int XDefaultScreen(IntPtr dpy);
        [DllImport(Lib)] public static extern ulong XRootWindow(IntPtr dpy, int screen);
        [DllImport(Lib)] public static extern IntPtr XDefaultVisual(IntPtr dpy, int screen);
        [DllImport(Lib)] public static extern int XDefaultDepth(IntPtr dpy, int screen);
        [DllImport(Lib)] public static extern int XDisplayWidth(IntPtr dpy, int screen);
        [DllImport(Lib)] public static extern int XDisplayHeight(IntPtr dpy, int screen);
        [DllImport(Lib)] public static extern int XMatchVisualInfo(IntPtr dpy, int screen, int depth, int @class, out XVisualInfo info);
        [DllImport(Lib)] public static extern ulong XCreateColormap(IntPtr dpy, ulong window, IntPtr visual, int alloc);
        [DllImport(Lib)] public static extern ulong XCreateWindow(IntPtr dpy, ulong parent, int x, int y, uint w, uint h, uint border, int depth, uint @class, IntPtr visual, ulong mask, ref XSetWindowAttributes attrs);
        [DllImport(Lib)] public static extern int XDestroyWindow(IntPtr dpy, ulong w);
        [DllImport(Lib)] public static extern int XMapRaised(IntPtr dpy, ulong w);
        [DllImport(Lib)] public static extern int XRaiseWindow(IntPtr dpy, ulong w);
        [DllImport(Lib)] public static extern int XUnmapWindow(IntPtr dpy, ulong w);
        [DllImport(Lib)] public static extern int XMoveResizeWindow(IntPtr dpy, ulong w, int x, int y, uint width, uint height);
        [DllImport(Lib)] public static extern int XFlush(IntPtr dpy);
        [DllImport(Lib)] public static extern int XSync(IntPtr dpy, bool discard);
        [DllImport(Lib)] public static extern int XStoreName(IntPtr dpy, ulong w, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(Lib)] public static extern ulong XInternAtom(IntPtr dpy, string name, bool onlyIfExists);
        [DllImport(Lib)] public static extern IntPtr XGetAtomName(IntPtr dpy, ulong atom);
        [DllImport(Lib)] public static extern ulong XGetSelectionOwner(IntPtr dpy, ulong selection);
        [DllImport(Lib)] public static extern int XChangeProperty(IntPtr dpy, ulong w, ulong property, ulong type, int format, int mode, long[] data, int count);
        [DllImport(Lib)] public static extern int XGetWindowProperty(IntPtr dpy, ulong w, ulong property, long offset, long length, bool delete, ulong reqType,
            out ulong actualType, out int actualFormat, out ulong items, out ulong bytesAfter, out IntPtr prop);
        [DllImport(Lib)] public static extern IntPtr XResourceManagerString(IntPtr dpy);
        [DllImport(Lib)] public static extern int XFree(IntPtr data);
        [StructLayout(LayoutKind.Sequential)]
        public struct XWindowAttributes
        {
            public int x, y, width, height, border_width, depth;
            public IntPtr visual;
            public ulong root;
            public int @class, bit_gravity, win_gravity, backing_store;
            public ulong backing_planes, backing_pixel;
            public int save_under;
            public ulong colormap;
            public int map_installed, map_state;
            public long all_event_masks, your_event_mask, do_not_propagate_mask;
            public int override_redirect;
            public IntPtr screen;
        }

        [DllImport(Lib)] public static extern int XGetWindowAttributes(IntPtr dpy, ulong w, out XWindowAttributes attrs);
        [DllImport(Lib)] public static extern bool XTranslateCoordinates(IntPtr dpy, ulong src, ulong dest, int x, int y, out int destX, out int destY, out ulong child);

        [DllImport("libXext.so.6")] public static extern void XShapeCombineRectangles(IntPtr dpy, ulong w, int kind, int x, int y, IntPtr rects, int n, int op, int ordering);
        [DllImport("libXrandr.so.2")] public static extern IntPtr XRRGetMonitors(IntPtr dpy, ulong window, bool active, out int n);
        [DllImport("libXrandr.so.2")] public static extern void XRRFreeMonitors(IntPtr monitors);
    }

    static class Cairo
    {
        const string Lib = "libcairo.so.2";
        public const int OPERATOR_SOURCE = 1, OPERATOR_OVER = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct TextExtents { public double x_bearing, y_bearing, width, height, x_advance, y_advance; }

        [DllImport(Lib)] public static extern IntPtr cairo_xlib_surface_create(IntPtr dpy, ulong drawable, IntPtr visual, int w, int h);
        [DllImport(Lib)] public static extern void cairo_xlib_surface_set_size(IntPtr surface, int w, int h);
        [DllImport(Lib)] public static extern IntPtr cairo_image_surface_create(int format, int w, int h);
        [DllImport(Lib)] public static extern int cairo_surface_write_to_png(IntPtr surface, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
        [DllImport(Lib)] public static extern void cairo_surface_flush(IntPtr surface);
        [DllImport(Lib)] public static extern void cairo_surface_destroy(IntPtr surface);
        [DllImport(Lib)] public static extern IntPtr cairo_create(IntPtr surface);
        [DllImport(Lib)] public static extern void cairo_destroy(IntPtr cr);
        [DllImport(Lib)] public static extern void cairo_push_group(IntPtr cr);
        [DllImport(Lib)] public static extern void cairo_pop_group_to_source(IntPtr cr);
        [DllImport(Lib)] public static extern void cairo_set_operator(IntPtr cr, int op);
        [DllImport(Lib)] public static extern void cairo_set_source_rgba(IntPtr cr, double r, double g, double b, double a);
        [DllImport(Lib)] public static extern void cairo_paint(IntPtr cr);
        [DllImport(Lib)] public static extern void cairo_new_path(IntPtr cr);
        [DllImport(Lib)] public static extern void cairo_close_path(IntPtr cr);
        [DllImport(Lib)] public static extern void cairo_arc(IntPtr cr, double xc, double yc, double r, double a1, double a2);
        [DllImport(Lib)] public static extern void cairo_move_to(IntPtr cr, double x, double y);
        [DllImport(Lib)] public static extern void cairo_line_to(IntPtr cr, double x, double y);
        [DllImport(Lib)] public static extern void cairo_fill(IntPtr cr);
        [DllImport(Lib)] public static extern void cairo_fill_preserve(IntPtr cr);
        [DllImport(Lib)] public static extern void cairo_stroke(IntPtr cr);
        [DllImport(Lib)] public static extern void cairo_set_line_width(IntPtr cr, double w);
        [DllImport(Lib)] public static extern void cairo_set_line_cap(IntPtr cr, int cap);
        [DllImport(Lib)] public static extern void cairo_select_font_face(IntPtr cr, [MarshalAs(UnmanagedType.LPUTF8Str)] string family, int slant, int weight);
        [DllImport(Lib)] public static extern void cairo_set_font_size(IntPtr cr, double size);
        [DllImport(Lib)] public static extern void cairo_text_extents(IntPtr cr, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, out TextExtents ext);
        [DllImport(Lib)] public static extern void cairo_show_text(IntPtr cr, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    }
}
