using System.Runtime.InteropServices;
using Fluester.Core;

namespace Fluester.Linux;

/// <summary>Linux-Tastencodes (linux/input-event-codes.h).</summary>
static class Keys
{
    public const ushort EV_SYN = 0, EV_KEY = 1, SYN_REPORT = 0;
    public const ushort ESC = 1, LEFTCTRL = 29, LEFTSHIFT = 42, RIGHTSHIFT = 54, LEFTALT = 56, CAPSLOCK = 58, F8 = 66,
        RIGHTCTRL = 97, RIGHTALT = 100, INSERT = 110, LEFTMETA = 125, RIGHTMETA = 126, V = 47, C = 46;

    public static bool IsCtrl(ushort c) => c is LEFTCTRL or RIGHTCTRL;
    public static bool IsMeta(ushort c) => c is LEFTMETA or RIGHTMETA;
    public static bool IsShift(ushort c) => c is LEFTSHIFT or RIGHTSHIFT;
}

static class Libc
{
    [DllImport("libc", SetLastError = true)] public static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] public static extern int close(int fd);
    [DllImport("libc", SetLastError = true)] public static extern nint write(int fd, byte[] buf, nint count);
    [DllImport("libc", SetLastError = true)] public static extern int ioctl(int fd, nuint request, int arg);
    [DllImport("libc", SetLastError = true)] public static extern int ioctl(int fd, nuint request, byte[] arg);
    [DllImport("libc", SetLastError = true)] public static extern int ioctl(int fd, nuint request);
    [DllImport("libc")] public static extern uint geteuid();
    public const int O_WRONLY = 1, O_NONBLOCK = 0x800;
}

/// <summary>Zugriff auf /dev/input hängt an der Gruppe „input“. Die wirkt aber erst in Prozessen, die nach dem nächsten
/// Anmelden starten – bis dahin hilft „sg input“, das die frisch eingetragene Gruppe sofort mitgibt.</summary>
static class InputAccess
{
    static string[] Groups(params string[] user) =>
        Desktop.Run("id", new[] { "-nG" }.Concat(user)).Out.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Dieser Prozess hat die Gruppe – Tastatur ist lesbar.</summary>
    public static bool Active => Libc.geteuid() == 0 || Groups().Contains("input");

    /// <summary>Der Nutzer ist in /etc/group eingetragen (gilt für neue Anmeldungen).</summary>
    public static bool Registered => Groups(Environment.UserName).Contains("input");

    /// <summary>Eingetragen, aber in dieser Anmeldung noch nicht aktiv: dann mit „sg input“ starten.</summary>
    public static bool PendingRelogin => !Active && Registered && Desktop.Which("sg") != null;

    static (string Text, DateTime At)? cached;

    /// <summary>Was bei „Kein Zugriff“ wirklich hilft (höchstens alle 30 s neu ermittelt).</summary>
    public static string Hint(string path)
    {
        if (cached is { } c && (DateTime.UtcNow - c.At).TotalSeconds < 30) return c.Text;
        var text = Active
            ? $"Kein Zugriff auf {path} trotz Gruppe „input“ – Rechte prüfen: ls -l {path}"
            : Registered
                ? "Kein Zugriff auf die Tastatur: Gruppe „input“ ist eingetragen, wirkt aber erst nach Ab- und Anmelden. Sofort: fluester stop; fluester start"
                : "Kein Zugriff auf die Tastatur: du bist nicht in der Gruppe „input“. Einrichten: fluester setup, danach ab- und anmelden";
        cached = (text, DateTime.UtcNow);
        return text;
    }
}

/// <summary>Liest Tastendrücke direkt von den Eingabegeräten (/dev/input/event*). Funktioniert unter X11 und Wayland,
/// braucht Mitgliedschaft in der Gruppe „input“. Neue Tastaturen (USB, Bluetooth) werden alle paar Sekunden erkannt.</summary>
sealed class KeyboardWatcher : IDisposable
{
    public const string VirtualKeyboardName = "Fluester Virtual Keyboard";

    /// <summary>(Tastencode, 1 = gedrückt, 0 = losgelassen, 2 = Wiederholung)</summary>
    public event Action<ushort, int>? Key;
    public event Action<string>? Problem;

    readonly Dictionary<string, Thread> readers = new();
    readonly HashSet<string> open = new();
    readonly object gate = new();
    volatile bool running = true;
    Timer? rescan;
    /// <summary>Für den Selbsttest: auch die eigene virtuelle Tastatur lesen.</summary>
    public bool IncludeVirtual { get; init; }

    public sealed record Device(string Path, string Name);

    /// <summary>Tastaturen laut /proc/bus/input/devices (Handler „kbd“ + „event…“).</summary>
    public static List<Device> Keyboards(bool includeVirtual = false)
    {
        var list = new List<Device>();
        if (!File.Exists("/proc/bus/input/devices")) return list;
        foreach (var block in File.ReadAllText("/proc/bus/input/devices").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string name = "", handlers = "", ev = "";
            foreach (var line in block.Split('\n'))
            {
                if (line.StartsWith("N: Name=")) name = line[8..].Trim('"');
                else if (line.StartsWith("H: Handlers=")) handlers = line[12..];
                else if (line.StartsWith("B: EV=")) ev = line[6..].Trim();
            }
            if (!handlers.Contains("kbd")) continue;
            bool isVirtual = name == VirtualKeyboardName;
            if (isVirtual && !includeVirtual) continue;
            // EV_KEY und EV_REP (Wiederholung) – echte Tastaturen, keine Power-Buttons
            if (!isVirtual && (!long.TryParse(ev, System.Globalization.NumberStyles.HexNumber, null, out var mask) || (mask & 0x100002) != 0x100002)) continue;
            var evt = handlers.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(h => h.StartsWith("event"));
            if (evt != null) list.Add(new Device("/dev/input/" + evt, name));
        }
        return list;
    }

    public void Start()
    {
        Scan();
        rescan = new Timer(_ => Scan(), null, 5000, 5000);
    }

    void Scan()
    {
        var devices = Keyboards(IncludeVirtual);
        if (devices.Count == 0) Problem?.Invoke("Keine Tastatur gefunden");
        foreach (var d in devices)
        {
            lock (gate)
            {
                if (open.Contains(d.Path)) continue;
                open.Add(d.Path);
            }
            var t = new Thread(() => Read(d)) { IsBackground = true, Name = "evdev " + d.Path };
            lock (gate) readers[d.Path] = t;
            t.Start();
        }
    }

    void Read(Device d)
    {
        try
        {
            using var fs = new FileStream(d.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, false);
            var buf = new byte[24 * 64];
            while (running)
            {
                int n = fs.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                for (int at = 0; at + 24 <= n; at += 24)
                {
                    ushort type = BitConverter.ToUInt16(buf, at + 16);
                    ushort code = BitConverter.ToUInt16(buf, at + 18);
                    int value = BitConverter.ToInt32(buf, at + 20);
                    if (type == Keys.EV_KEY) Key?.Invoke(code, value);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            Problem?.Invoke(InputAccess.Hint(d.Path));
        }
        catch (IOException)
        {
            // Gerät abgesteckt
        }
        finally
        {
            lock (gate) { open.Remove(d.Path); readers.Remove(d.Path); }
        }
    }

    public void Dispose()
    {
        running = false;
        rescan?.Dispose();
    }
}

/// <summary>Erkennt die Diktat-Taste aus rohen Tastencodes. Andere Tasten während des Haltens → kein Diktat.</summary>
sealed class HotkeyTracker
{
    public event Action<bool>? Pressed;
    public event Action? Released;
    public event Action? CommandModifier;
    public event Action? OtherKey;
    public event Action? Escape;

    public HotkeyChoice Choice { get; set; } = HotkeyChoice.RightCtrl;
    bool hotkeyDown, ctrlDown, metaDown, shiftDown;

    public void Feed(ushort code, int value)
    {
        if (value == 2) return; // Wiederholung
        bool down = value == 1;
        if (Keys.IsShift(code))
        {
            if (down && !shiftDown && hotkeyDown) CommandModifier?.Invoke();
            shiftDown = down;
            return;
        }
        if (Choice == HotkeyChoice.CtrlWin && (Keys.IsCtrl(code) || Keys.IsMeta(code)))
        {
            if (Keys.IsCtrl(code)) ctrlDown = down;
            if (Keys.IsMeta(code)) metaDown = down;
            if (!hotkeyDown && ctrlDown && metaDown) { hotkeyDown = true; Pressed?.Invoke(shiftDown); }
            else if (hotkeyDown && (!ctrlDown || !metaDown)) { hotkeyDown = false; Released?.Invoke(); }
            return;
        }
        if (code == HotkeyCode)
        {
            if (down && !hotkeyDown) { hotkeyDown = true; Pressed?.Invoke(shiftDown); }
            else if (!down && hotkeyDown) { hotkeyDown = false; Released?.Invoke(); }
            return;
        }
        if (code == Keys.ESC && down) { Escape?.Invoke(); return; }
        if (down && hotkeyDown) OtherKey?.Invoke();
    }

    ushort HotkeyCode => Choice switch
    {
        HotkeyChoice.RightCtrl => Keys.RIGHTCTRL,
        HotkeyChoice.CapsLock => Keys.CAPSLOCK,
        HotkeyChoice.RightAlt => Keys.RIGHTALT,
        HotkeyChoice.F8 => Keys.F8,
        _ => 0,
    };

    public void Reset() => hotkeyDown = ctrlDown = metaDown = shiftDown = false;
}

/// <summary>Virtuelle Tastatur über /dev/uinput: sendet Strg+V & Co. – unter X11 und Wayland, auf GNOME, KDE, Sway, Hyprland.</summary>
sealed class VirtualKeyboard(string name = KeyboardWatcher.VirtualKeyboardName, bool repeat = false) : IDisposable
{
    const nuint UI_SET_EVBIT = 0x40045564, UI_SET_KEYBIT = 0x40045565, UI_DEV_SETUP = 0x405C5503, UI_DEV_CREATE = 0x5501, UI_DEV_DESTROY = 0x5502;
    const ushort EV_REP = 0x14;
    int fd = -1;

    public static bool Available => File.Exists("/dev/uinput");

    /// <summary>Gerät anlegen. Fehlermeldung oder null.</summary>
    public string? Open()
    {
        if (fd >= 0) return null;
        fd = Libc.open("/dev/uinput", Libc.O_WRONLY | Libc.O_NONBLOCK);
        if (fd < 0)
            return Marshal.GetLastWin32Error() == 13
                ? "Kein Zugriff auf /dev/uinput – „fluester setup“ richtet das ein (Gruppe input + udev-Regel)"
                : "/dev/uinput nicht verfügbar (Fehler " + Marshal.GetLastWin32Error() + ")";
        Libc.ioctl(fd, UI_SET_EVBIT, Keys.EV_KEY);
        Libc.ioctl(fd, UI_SET_EVBIT, Keys.EV_SYN);
        // Mit Tastenwiederholung sieht das Gerät aus wie eine echte Tastatur (für Ende-zu-Ende-Tests)
        if (repeat) Libc.ioctl(fd, UI_SET_EVBIT, EV_REP);
        foreach (var k in new ushort[] { Keys.LEFTCTRL, Keys.LEFTSHIFT, Keys.V, Keys.C, Keys.INSERT, Keys.RIGHTCTRL, Keys.F8, Keys.CAPSLOCK, Keys.RIGHTALT, Keys.LEFTMETA })
            Libc.ioctl(fd, UI_SET_KEYBIT, k);
        // struct uinput_setup { input_id {bustype, vendor, product, version}; char name[80]; u32 ff_effects_max; }
        var setup = new byte[92];
        BitConverter.TryWriteBytes(setup.AsSpan(0, 2), (ushort)0x06);  // BUS_VIRTUAL
        BitConverter.TryWriteBytes(setup.AsSpan(2, 2), (ushort)0x464C); // "FL"
        BitConverter.TryWriteBytes(setup.AsSpan(4, 2), (ushort)0x5354); // "ST"
        BitConverter.TryWriteBytes(setup.AsSpan(6, 2), (ushort)1);
        System.Text.Encoding.ASCII.GetBytes(name).CopyTo(setup, 8);
        if (Libc.ioctl(fd, UI_DEV_SETUP, setup) < 0 || Libc.ioctl(fd, UI_DEV_CREATE) < 0)
        {
            Libc.close(fd);
            fd = -1;
            return "Virtuelle Tastatur ließ sich nicht anlegen (Fehler " + Marshal.GetLastWin32Error() + ")";
        }
        Thread.Sleep(250); // Compositor muss das neue Gerät erst sehen
        return null;
    }

    void Emit(ushort type, ushort code, int value)
    {
        var ev = new byte[24];
        BitConverter.TryWriteBytes(ev.AsSpan(16, 2), type);
        BitConverter.TryWriteBytes(ev.AsSpan(18, 2), code);
        BitConverter.TryWriteBytes(ev.AsSpan(20, 4), value);
        Libc.write(fd, ev, ev.Length);
    }

    void Key(ushort code, bool down)
    {
        Emit(Keys.EV_KEY, code, down ? 1 : 0);
        Emit(Keys.EV_SYN, Keys.SYN_REPORT, 0);
        Thread.Sleep(8);
    }

    /// <summary>Tastenkombination drücken, z. B. Strg+V.</summary>
    public void Chord(params ushort[] keys)
    {
        if (fd < 0) throw new FluesterException("Virtuelle Tastatur nicht bereit");
        foreach (var k in keys) Key(k, true);
        foreach (var k in keys.Reverse()) Key(k, false);
    }

    /// <summary>Einzelne Taste drücken/loslassen (Selbsttest).</summary>
    public void Press(ushort key, bool down) => Key(key, down);

    public void Dispose()
    {
        if (fd < 0) return;
        Libc.ioctl(fd, UI_DEV_DESTROY);
        Libc.close(fd);
        fd = -1;
    }
}
