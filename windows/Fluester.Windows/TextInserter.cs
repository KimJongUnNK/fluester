using System.Diagnostics;
using static Fluester.Windows.Native;

namespace Fluester.Windows;

/// <summary>Programm/Fenster im Vordergrund – dorthin geht der Text.</summary>
sealed record ForegroundInfo(IntPtr Window, IntPtr Focus, string ProcessName, string Title)
{
    public static ForegroundInfo Current() => Of(GetForegroundWindow());

    /// <summary>Taskleiste, Infobereich, Startmenü – dorthin wird nie eingefügt.</summary>
    public static bool IsShell(IntPtr hwnd) => ClassName(hwnd) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow"
        or "TopLevelWindowForOverflowXamlIsland" or "Windows.UI.Core.CoreWindow" or "Progman" or "WorkerW";

    public static ForegroundInfo Of(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return new ForegroundInfo(IntPtr.Zero, IntPtr.Zero, "", "");
        uint tid = GetWindowThreadProcessId(hwnd, out var pid);
        var info = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
        GetGUIThreadInfo(tid, ref info);
        string name = "";
        try { name = Process.GetProcessById((int)pid).ProcessName; } catch { }
        return new ForegroundInfo(hwnd, info.hwndFocus, name, WindowTitle(hwnd));
    }

    public bool IsSelf => ProcessName.Equals(Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Lesbarer Name für Verlauf und Prompt („Outlook“, „WhatsApp“).</summary>
    public string DisplayName
    {
        get
        {
            try
            {
                GetWindowThreadProcessId(Window, out var pid);
                var desc = Process.GetProcessById((int)pid).MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(desc)) return desc!;
            }
            catch { /* geschützte Prozesse */ }
            return ProcessName;
        }
    }
}

/// <summary>Text einfügen: über die Zwischenablage (Strg+V, danach alter Inhalt zurück) oder als Tastatureingabe.</summary>
static class TextInserter
{
    static IntPtr lastWindow;
    static DateTime lastInsert;

    /// <summary>false = kein Ziel im Vordergrund; der Text liegt dann in der Zwischenablage.</summary>
    public static bool Insert(string text, Fluester.Core.InsertMethod method, bool smartSpacing, ForegroundInfo target, bool allowSelf = false)
    {
        if (target.Window == IntPtr.Zero || (target.IsSelf && !allowSelf))
        {
            SetClipboardText(text);
            return false;
        }
        // Gleiches Fenster, kurz nach dem letzten Diktat: Leerzeichen davor, damit Sätze nicht kleben
        if (smartSpacing && target.Window == lastWindow && (DateTime.UtcNow - lastInsert).TotalMinutes < 2
            && text.Length > 0 && !char.IsWhiteSpace(text[0]) && !".,;:!?)".Contains(text[0]))
            text = " " + text;
        if (GetForegroundWindow() != target.Window) SetForegroundWindow(target.Window);
        ReleaseModifiers();
        if (method == Fluester.Core.InsertMethod.Type) TypeText(text); else Paste(text);
        lastWindow = target.Window;
        lastInsert = DateTime.UtcNow;
        return true;
    }

    /// <summary>Gedrückte Strg/Umschalt/Alt/Win lösen, sonst wird aus Strg+V z. B. Strg+Umschalt+V.</summary>
    static void ReleaseModifiers()
    {
        var keys = new[] { VK_LCONTROL, VK_RCONTROL, VK_LSHIFT, VK_RSHIFT, VK_LMENU, VK_RMENU, VK_LWIN, VK_RWIN };
        var ups = keys.Where(k => (GetAsyncKeyState(k) & 0x8000) != 0).Select(k => Key(k, true)).ToArray();
        if (ups.Length > 0) Send(ups);
    }

    static void Paste(string text)
    {
        var backup = ClipboardBackup.Take();
        SetClipboardText(text);
        Send(Key(VK_CONTROL, false), Key(VK_V, false), Key(VK_V, true), Key(VK_CONTROL, true));
        // Alte Zwischenablage zurück, wenn die App eingefügt hat
        var restore = new System.Windows.Forms.Timer { Interval = 600 };
        restore.Tick += (_, _) => { restore.Dispose(); backup.Restore(); };
        restore.Start();
    }

    static void TypeText(string text)
    {
        var inputs = new List<INPUT>();
        foreach (var c in text.Replace("\r\n", "\n"))
        {
            if (c == '\n')
            {
                // Umschalt+Enter: neue Zeile statt „Senden“ in Chat-Apps
                inputs.Add(Key(VK_SHIFT, false)); inputs.Add(Key(VK_RETURN, false));
                inputs.Add(Key(VK_RETURN, true)); inputs.Add(Key(VK_SHIFT, true));
            }
            else
            {
                inputs.Add(Unicode(c, false));
                inputs.Add(Unicode(c, true));
            }
        }
        foreach (var chunk in inputs.Chunk(200))
        {
            Send(chunk);
            Thread.Sleep(5);
        }
    }

    /// <summary>Markierten Text holen (Command Mode): Strg+C senden, warten bis sich die Zwischenablage ändert.</summary>
    public static string? CopySelection()
    {
        var backup = ClipboardBackup.Take();
        uint seq = GetClipboardSequenceNumber();
        ReleaseModifiers();
        Send(Key(VK_CONTROL, false), Key(VK_C, false), Key(VK_C, true), Key(VK_CONTROL, true));
        string? text = null;
        for (int i = 0; i < 25; i++)
        {
            Thread.Sleep(20);
            System.Windows.Forms.Application.DoEvents();
            if (GetClipboardSequenceNumber() != seq)
            {
                Thread.Sleep(30);
                text = TryGetClipboardText();
                break;
            }
        }
        backup.Restore();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static void SetClipboardText(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try { System.Windows.Forms.Clipboard.SetText(text); return; }
            catch (System.Runtime.InteropServices.ExternalException) { Thread.Sleep(40); }
        }
    }

    static string? TryGetClipboardText()
    {
        try { return System.Windows.Forms.Clipboard.ContainsText() ? System.Windows.Forms.Clipboard.GetText() : null; }
        catch (System.Runtime.InteropServices.ExternalException) { return null; }
    }
}

/// <summary>Sichert die gängigen Formate der Zwischenablage (Text, RTF, HTML, Bild, Dateien) und stellt sie wieder her.</summary>
sealed class ClipboardBackup
{
    readonly System.Windows.Forms.DataObject? data;

    ClipboardBackup(System.Windows.Forms.DataObject? d) => data = d;

    public static ClipboardBackup Take()
    {
        try
        {
            var src = System.Windows.Forms.Clipboard.GetDataObject();
            if (src == null) return new ClipboardBackup(null);
            var copy = new System.Windows.Forms.DataObject();
            bool any = false;
            foreach (var f in new[] { System.Windows.Forms.DataFormats.UnicodeText, System.Windows.Forms.DataFormats.Rtf, System.Windows.Forms.DataFormats.Html })
            {
                if (src.GetDataPresent(f) && src.GetData(f) is string s) { copy.SetData(f, s); any = true; }
            }
            if (src.GetDataPresent(System.Windows.Forms.DataFormats.FileDrop) && src.GetData(System.Windows.Forms.DataFormats.FileDrop) is string[] files)
            {
                copy.SetData(System.Windows.Forms.DataFormats.FileDrop, files); any = true;
            }
            if (System.Windows.Forms.Clipboard.ContainsImage() && System.Windows.Forms.Clipboard.GetImage() is { } img)
            {
                copy.SetImage(img); any = true;
            }
            return new ClipboardBackup(any ? copy : null);
        }
        catch
        {
            return new ClipboardBackup(null);
        }
    }

    public void Restore()
    {
        try
        {
            if (data != null) System.Windows.Forms.Clipboard.SetDataObject(data, true);
            else System.Windows.Forms.Clipboard.Clear();
        }
        catch { /* Zwischenablage gerade blockiert – nicht schlimm */ }
    }
}
