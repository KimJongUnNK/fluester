using System.Diagnostics;
using System.Runtime.InteropServices;
using Fluester.Core;
using static Fluester.Windows.Native;

namespace Fluester.Windows;

/// <summary>Systemweiter Tastatur-Hook (WH_KEYBOARD_LL): erkennt Drücken/Loslassen der Diktat-Taste, Umschalt als
/// Command-Mode-Zusatz, andere Tasten (→ kein Diktat) und Esc (→ Abbruch). Die Rückmeldungen laufen asynchron
/// über den UI-Thread, damit der Hook nie blockiert (Windows entfernt sonst langsame Hooks).</summary>
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
    public bool IsInstalled => hook != IntPtr.Zero;
    public static readonly Stopwatch Clock = Stopwatch.StartNew();

    IntPtr hook;
    readonly LowLevelKeyboardProc proc;
    SynchronizationContext? ui;
    bool hotkeyDown, ctrlDown, winDown, comboUsedWin, shiftDown;

    public KeyboardHook() => proc = HookProc;

    public bool Install()
    {
        if (hook != IntPtr.Zero) return true;
        ui = SynchronizationContext.Current;
        hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) Fluester.Engine.Log.Write("Tastatur-Hook fehlgeschlagen: " + Marshal.GetLastWin32Error());
        return hook != IntPtr.Zero;
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
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
            if (Handle(k.vkCode, k.scanCode, down, up)) return (IntPtr)1;   // schlucken
        }
        catch (Exception e)
        {
            Fluester.Engine.Log.Write("Hook: " + e.Message);
        }
        return CallNextHookEx(hook, nCode, wParam, lParam);
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
            // Feststelltaste/F8/AltGr schlucken (kein Umschalten, kein Menü); Strg rechts durchlassen
            return Choice != HotkeyChoice.RightCtrl;
        }
        else if (Choice == HotkeyChoice.RightAlt && vk == VK_LCONTROL && (scan & 0x200) != 0)
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
        _ => false,
    };

    /// <summary>Nach Wechsel der Taste oder Fokusverlust: Zustand zurücksetzen.</summary>
    public void Reset()
    {
        hotkeyDown = ctrlDown = winDown = comboUsedWin = shiftDown = false;
    }
}
