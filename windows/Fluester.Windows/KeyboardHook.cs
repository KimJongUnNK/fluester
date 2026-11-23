using System.Diagnostics;
using System.Runtime.InteropServices;
using Fluester.Core;
using static Fluester.Windows.Native;

namespace Fluester.Windows;

/// <summary>Systemweiter Tastatur-Hook (WH_KEYBOARD_LL): erkennt Drücken/Loslassen der Diktat-Taste, Umschalt als
/// Command-Mode-Zusatz, andere Tasten (→ kein Diktat) und Esc (→ Abbruch).
/// Der Hook läuft auf einem eigenen Thread mit eigener Nachrichtenschleife: Windows entfernt einen Hook still, wenn
/// sein Thread ein paar hundert Millisekunden nicht antwortet – und der UI-Thread ist beim Öffnen des Mikrofons oder
/// beim Kopieren der Markierung genau so lange beschäftigt, während die gehaltene Taste Wiederholungen schickt.
/// Die Rückmeldungen gehen asynchron an den UI-Thread.</summary>
sealed class KeyboardHook : IDisposable
{
    public event Action<bool>? Pressed;          // command?
    public event Action? Released;
    public event Action? CommandModifier;
    public event Action? OtherKey;
    public event Action? EscapePressed;

    /// <summary>Wird synchron im Hook abgefragt: Esc schlucken, weil gerade aufgenommen/verarbeitet wird?</summary>
    public Func<bool> ShouldSwallowEscape { get; set; } = () => false;

    /// <summary>Für den Selbsttest: auch fremde eingespeiste Tasten auswerten.</summary>
    public bool AcceptInjected { get; set; }

    public HotkeyChoice Choice { get; set; } = HotkeyChoice.RightCtrl;
    /// <summary>Virtueller Tastencode der eigenen Taste (<see cref="HotkeyChoice.Custom"/>).</summary>
    public uint CustomVk { get; set; }
    public bool IsInstalled => hook != IntPtr.Zero;
    public static readonly Stopwatch Clock = Stopwatch.StartNew();

    volatile IntPtr hook;
    readonly LowLevelKeyboardProc proc;
    SynchronizationContext? ui;
    Thread? thread;
    SynchronizationContext? hookContext;
    readonly object gate = new();
    bool hotkeyDown, ctrlDown, winDown, comboUsedWin, shiftDown;
    Action<uint, string>? capture;
    uint captureVk;

    public KeyboardHook() => proc = HookProc;

    public bool Install()
    {
        if (hook != IntPtr.Zero) return true;
        ui = SynchronizationContext.Current;
        using var ready = new ManualResetEventSlim();
        int error = 0;
        thread = new Thread(() =>
        {
            var ctx = new System.Windows.Forms.WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(ctx);
            hookContext = ctx;
            hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) error = Marshal.GetLastWin32Error();
            ready.Set();
            if (hook == IntPtr.Zero) return;
            System.Windows.Forms.Application.Run();   // Nachrichtenschleife nur für den Hook
        }) { IsBackground = true, Name = "Fluester.Tastatur" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        if (hook == IntPtr.Zero) Fluester.Engine.Log.Write("Tastatur-Hook fehlgeschlagen: " + error);
        return hook != IntPtr.Zero;
    }

    public void Dispose()
    {
        var ctx = hookContext;
        var t = thread;
        hookContext = null;
        thread = null;
        if (hook == IntPtr.Zero || ctx == null) return;
        // Auf dem Hook-Thread abmelden und dessen Schleife beenden
        ctx.Post(_ =>
        {
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
            System.Windows.Forms.Application.ExitThread();
        }, null);
        t?.Join(2000);
        hook = IntPtr.Zero;
    }

    void Post(Action a)
    {
        if (ui != null) ui.Post(_ => a(), null); else a();
    }

    static bool IsCtrl(uint vk) => vk is VK_LCONTROL or VK_RCONTROL or VK_CONTROL;
    static bool IsWin(uint vk) => vk is VK_LWIN or VK_RWIN;
    static bool IsShift(uint vk) => vk is VK_LSHIFT or VK_RSHIFT or VK_SHIFT;

    IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(hook, nCode, wParam, lParam);
        var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        int msg = wParam.ToInt32();
        bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
        bool up = msg is WM_KEYUP or WM_SYSKEYUP;
        // Eigene Eingaben (Einfügen, Tippen) nie auswerten
        if (k.dwExtraInfo == FluesterTag || ((k.flags & LLKHF_INJECTED) != 0 && !AcceptInjected))
            return CallNextHookEx(hook, nCode, wParam, lParam);
        try
        {
            if (Capturing(k, down, up)) return (IntPtr)1;
            bool swallow;
            lock (gate) swallow = Handle(k.vkCode, k.scanCode, down, up);
            if (swallow) return (IntPtr)1;   // schlucken
        }
        catch (Exception e)
        {
            Fluester.Engine.Log.Write("Hook: " + e.Message);
        }
        return CallNextHookEx(hook, nCode, wParam, lParam);
    }

    /// <summary>Nächste Taste als eigene Diktat-Taste aufnehmen (Einstellungen). Rückmeldung auf dem UI-Thread mit Code und Name;
    /// Code 0 = abgebrochen. Die Taste wird geschluckt, damit sie in keiner App etwas auslöst.</summary>
    public void CaptureNext(Action<uint, string> done)
    {
        lock (gate) { capture = done; captureVk = 0; }
    }

    public void CancelCapture()
    {
        lock (gate) { capture = null; captureVk = 0; }
    }

    bool Capturing(KBDLLHOOKSTRUCT k, bool down, bool up)
    {
        Action<uint, string>? done;
        lock (gate)
        {
            if (capture == null && captureVk == 0) return false;
            // Loslassen der aufgenommenen Taste auch noch schlucken
            if (capture == null) { if (up && k.vkCode == captureVk) { captureVk = 0; return true; } return false; }
            // AltGr schickt vorab ein unechtes Strg links
            if (k.vkCode == VK_LCONTROL && (k.scanCode & 0x200) != 0) return true;
            if (!down) return true;
            done = capture;
            capture = null;
            captureVk = k.vkCode;
        }
        var name = KeyName(k.vkCode, k.scanCode, (k.flags & LLKHF_EXTENDED) != 0);
        uint vk = k.vkCode;
        Post(() => done(vk, name));
        return true;
    }

    static string KeyName(uint vk, uint scan, bool extended)
    {
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        var named = vk switch
        {
            VK_LCONTROL => "Strg links", VK_RCONTROL => "Strg rechts", VK_LMENU => "Alt links", VK_RMENU => "Alt rechts / AltGr",
            VK_LWIN => "Win links", VK_RWIN => "Win rechts", 0x5D => "Menü-Taste", 0x13 => "Pause", 0x91 => "Rollen", 0x2C => "Druck",
            _ => null,
        };
        if (named != null) return named;
        var sb = new System.Text.StringBuilder(64);
        int lp = (int)((scan & 0xFF) << 16) | (extended ? 1 << 24 : 0);
        return GetKeyNameTextW(lp, sb, sb.Capacity) > 0 ? sb.ToString() : $"Taste {vk}";
    }

    /// <summary>true = Taste schlucken.</summary>
    bool Handle(uint vk, uint scan, bool down, bool up)
    {
        if (IsShift(vk))
        {
            if (down && !shiftDown && hotkeyDown) Post(() => CommandModifier?.Invoke());
            if (down) shiftDown = true;
            if (up) shiftDown = false;
            return false;
        }

        if (Choice == HotkeyChoice.CtrlWin)
        {
            if (IsCtrl(vk) || IsWin(vk))
            {
                if (IsCtrl(vk)) { if (down) ctrlDown = true; if (up) ctrlDown = false; }
                if (IsWin(vk)) { if (down) winDown = true; if (up) winDown = false; }
                if (!hotkeyDown && ctrlDown && winDown)
                {
                    hotkeyDown = true;
                    comboUsedWin = true;
                    bool cmd = shiftDown;
                    Post(() => Pressed?.Invoke(cmd));
                }
                else if (hotkeyDown && (!ctrlDown || !winDown))
                {
                    hotkeyDown = false;
                    Post(() => Released?.Invoke());
                }
                // Win losgelassen nach der Kombination: unbelegte Taste dazwischen, sonst öffnet das Startmenü
                if (up && IsWin(vk) && comboUsedWin)
                {
                    comboUsedWin = false;
                    Send(Key(VK_MASK, false), Key(VK_MASK, true));
                }
                return false;
            }
        }
        else if (IsHotkey(vk, scan))
        {
            if (down && !hotkeyDown)
            {
                hotkeyDown = true;
                bool cmd = shiftDown;
                Post(() => Pressed?.Invoke(cmd));
            }
            else if (up && hotkeyDown)
            {
                hotkeyDown = false;
                Post(() => Released?.Invoke());
            }
            // Feststelltaste/F8/AltGr/eigene Taste schlucken (kein Umschalten, kein Menü); Strg rechts und andere
            // Strg-/Win-Tasten durchlassen, sonst klemmen Kürzel wie Strg+C
            if (Choice == HotkeyChoice.RightCtrl) return false;
            if (Choice == HotkeyChoice.Custom && (IsCtrl(vk) || IsWin(vk) || vk == VK_LMENU))
            {
                // Win/Alt allein losgelassen öffnet Startmenü bzw. Menüleiste – unbelegte Taste dazwischen
                if (up && (IsWin(vk) || vk == VK_LMENU)) Send(Key(VK_MASK, false), Key(VK_MASK, true));
                return false;
            }
            return true;
        }
        else if ((Choice == HotkeyChoice.RightAlt || (Choice == HotkeyChoice.Custom && CustomVk == VK_RMENU)) && vk == VK_LCONTROL && (scan & 0x200) != 0)
        {
            return true; // AltGr erzeugt ein unechtes Strg links – mitschlucken
        }

        if (vk == VK_ESCAPE && down)
        {
            if (ShouldSwallowEscape())
            {
                Post(() => EscapePressed?.Invoke());
                return true;
            }
            return false;
        }

        if (down && hotkeyDown) Post(() => OtherKey?.Invoke());
        return false;
    }

    bool IsHotkey(uint vk, uint scan) => Choice switch
    {
        HotkeyChoice.RightCtrl => vk == VK_RCONTROL,
        HotkeyChoice.CapsLock => vk == VK_CAPITAL,
        HotkeyChoice.RightAlt => vk == VK_RMENU,
        HotkeyChoice.F8 => vk == VK_F8,
        HotkeyChoice.Custom => CustomVk != 0 && vk == CustomVk,
        _ => false,
    };

    /// <summary>Nach Wechsel der Taste oder Fokusverlust: Zustand zurücksetzen.</summary>
    public void Reset()
    {
        lock (gate) hotkeyDown = ctrlDown = winDown = comboUsedWin = shiftDown = false;
    }
}
