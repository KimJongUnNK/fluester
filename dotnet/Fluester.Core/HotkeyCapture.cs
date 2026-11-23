namespace Fluester.Core;

/// <summary>Prüft eine im Einstellungsfenster gedrückte Taste für „Eigene Taste …“: feste Wahl, eigene Taste oder Ablehnung.
/// Abgelehnt werden Esc (bricht ab), Umschalt (Command Mode) und Tasten, die Text tippen – die wären danach zum Schreiben weg.</summary>
public static class HotkeyCapture
{
    public readonly record struct Verdict(HotkeyChoice Choice, string? Error)
    {
        public bool Ok => Error == null;
    }

    public const string TypingKey = "Diese Taste tippt Text – nimm eine ohne Zeichen (z. B. F1–F24, Pause, Einfg, Strg/Alt links, Ziffernblock).";
    public const string ShiftKey = "Umschalt ist für den Command Mode reserviert – nimm eine andere Taste.";

    /// <summary>Windows: virtueller Tastencode aus dem Tastatur-Hook (seitenspezifisch, z. B. 0xA3 = Strg rechts).</summary>
    public static Verdict Windows(uint vk) => vk switch
    {
        0x1B => new(HotkeyChoice.Custom, "Abgebrochen"),
        0x10 or 0xA0 or 0xA1 => new(HotkeyChoice.Custom, ShiftKey),
        0xA3 => new(HotkeyChoice.RightCtrl, null),
        0xA5 => new(HotkeyChoice.RightAlt, null),
        0x14 => new(HotkeyChoice.CapsLock, null),
        0x77 => new(HotkeyChoice.F8, null),
        0x08 or 0x09 or 0x0D or 0x20 or (>= 0x30 and <= 0x39) or (>= 0x41 and <= 0x5A)
            or (>= 0xBA and <= 0xC0) or (>= 0xDB and <= 0xDF) or 0xE2 => new(HotkeyChoice.Custom, TypingKey),
        0 or > 0xFE => new(HotkeyChoice.Custom, "Unbekannte Taste"),
        _ => new(HotkeyChoice.Custom, null),
    };

    /// <summary>Linux: evdev-Tastencode (/usr/include/linux/input-event-codes.h).</summary>
    public static Verdict Linux(int code) => code switch
    {
        1 => new(HotkeyChoice.Custom, "Abgebrochen"),
        42 or 54 => new(HotkeyChoice.Custom, ShiftKey),
        97 => new(HotkeyChoice.RightCtrl, null),
        100 => new(HotkeyChoice.RightAlt, null),
        58 => new(HotkeyChoice.CapsLock, null),
        66 => new(HotkeyChoice.F8, null),
        (>= 2 and <= 28) or (>= 30 and <= 41) or (>= 43 and <= 53) or 57 or 86 => new(HotkeyChoice.Custom, TypingKey),
        <= 0 or > 0x2FF => new(HotkeyChoice.Custom, "Unbekannte Taste"),
        _ => new(HotkeyChoice.Custom, null),
    };

    /// <summary>Linux: lesbarer Name aus dem GDK-Tastennamen („Control_L“ → „Strg links“, „F13“ bleibt „F13“).</summary>
    public static string LinuxKeyName(string? keyval, int code)
    {
        var n = keyval ?? "";
        return n switch
        {
            "" or "VoidSymbol" => $"Taste {code}",
            "Control_L" => "Strg links", "Control_R" => "Strg rechts", "Alt_L" => "Alt links", "Alt_R" or "ISO_Level3_Shift" => "Alt rechts / AltGr",
            "Super_L" => "Super links", "Super_R" => "Super rechts", "Meta_L" => "Meta links", "Meta_R" => "Meta rechts",
            "Menu" => "Menü-Taste", "Insert" => "Einfg", "Delete" => "Entf", "Home" => "Pos1", "End" => "Ende",
            "Prior" or "Page_Up" => "Bild auf", "Next" or "Page_Down" => "Bild ab", "Scroll_Lock" => "Rollen", "Print" => "Druck",
            "Num_Lock" => "Num", "Caps_Lock" => "Feststelltaste ⇪",
            _ when n.StartsWith("KP_") => "Ziffernblock " + n[3..],
            _ when n.StartsWith("XF86") => n[4..],
            _ => n.Replace('_', ' '),
        };
    }

    /// <summary>Ergebnis übernehmen: feste Wahl, wenn die Taste eine ist, sonst eigene Taste mit Code und Name.</summary>
    public static void Apply(Settings s, Verdict v, int code, string name)
    {
        if (!v.Ok) return;
        s.Hotkey = v.Choice;
        if (v.Choice != HotkeyChoice.Custom) return;
        s.CustomHotkeyCode = code;
        s.CustomHotkeyName = string.IsNullOrWhiteSpace(name) ? $"Taste {code}" : name.Trim();
    }
}
