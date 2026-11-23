import Foundation

/// Prüft eine im Einstellungsfenster gedrückte Taste für „Eigene Taste …“: feste Wahl, eigene Taste oder Ablehnung.
/// Abgelehnt werden Esc (bricht ab), ⇧/⇪ (kein sauberes Halten) und Tasten, die Text tippen.
/// Tastencodes: Carbon kVK_… (HIToolbox/Events.h).
public enum HotkeyCapture {
    public struct Verdict: Equatable, Sendable {
        public let choice: HotkeyChoice
        public let error: String?
        public var ok: Bool { error == nil }
    }

    public static let typingKey = "Diese Taste tippt Text – nimm eine ohne Zeichen (z. B. F13–F20, eine ⌥/⌘/⌃-Taste, Ziffernblock)."
    public static let cancelled = "Abgebrochen"

    /// Modifier-Tasten (kommen als flagsChanged, nicht als keyDown).
    public static let modifierCodes: Set<Int> = [0x36, 0x37, 0x38, 0x39, 0x3A, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F]

    public static func mac(_ code: Int) -> Verdict {
        switch code {
        case 0x35: return Verdict(choice: .custom, error: cancelled)
        case 0x38, 0x3C: return Verdict(choice: .custom, error: "⇧ Umschalt lässt sich nicht als Diktat-Taste nutzen – nimm eine andere.")
        case 0x39: return Verdict(choice: .custom, error: "⇪ Feststelltaste rastet ein – nimm eine andere Taste.")
        case 0x3F: return Verdict(choice: .fn, error: nil)
        case 0x3D: return Verdict(choice: .rightOption, error: nil)
        case 0x36: return Verdict(choice: .rightCommand, error: nil)
        case 0x3E: return Verdict(choice: .rightControl, error: nil)
        case 0...0x33: return Verdict(choice: .custom, error: typingKey)
        case let c where c < 0 || c > 0x7F: return Verdict(choice: .custom, error: "Unbekannte Taste")
        default: return Verdict(choice: .custom, error: nil)
        }
    }

    /// Lesbarer Name; `characters` = Zeichen der Taste (für Ziffernblock und Sondertasten ohne festen Namen).
    public static func macName(_ code: Int, characters: String? = nil) -> String {
        let names: [Int: String] = [
            0x7A: "F1", 0x78: "F2", 0x63: "F3", 0x76: "F4", 0x60: "F5", 0x61: "F6", 0x62: "F7", 0x64: "F8", 0x65: "F9", 0x6D: "F10",
            0x67: "F11", 0x6F: "F12", 0x69: "F13", 0x6B: "F14", 0x71: "F15", 0x6A: "F16", 0x40: "F17", 0x4F: "F18", 0x50: "F19", 0x5A: "F20",
            0x37: "Linke ⌘ Command", 0x3A: "Linke ⌥ Option", 0x3B: "Linke ⌃ Control",
            0x72: "Hilfe / Einfg", 0x73: "Pos1", 0x77: "Ende", 0x74: "Bild auf", 0x79: "Bild ab", 0x75: "Entf ⌦",
            0x7B: "←", 0x7C: "→", 0x7D: "↓", 0x7E: "↑", 0x47: "Ziffernblock Clear", 0x4C: "Ziffernblock Enter",
        ]
        if let n = names[code] { return n }
        if (0x41...0x5C).contains(code), let ch = characters?.trimmingCharacters(in: .whitespacesAndNewlines), !ch.isEmpty {
            return "Ziffernblock " + ch.uppercased()
        }
        if let ch = characters?.trimmingCharacters(in: .whitespacesAndNewlines), !ch.isEmpty,
           ch.unicodeScalars.allSatisfy({ !CharacterSet.controlCharacters.contains($0) && ($0.value < 0xF700 || $0.value > 0xF8FF) }) {
            return ch.uppercased()
        }
        return "Taste \(code)"
    }

    /// Ergebnis übernehmen: feste Wahl, wenn die Taste eine ist, sonst eigene Taste mit Code und Name.
    public static func apply(_ s: inout Settings, _ v: Verdict, code: Int, name: String) {
        guard v.ok else { return }
        s.hotkey = v.choice
        guard v.choice == .custom else { return }
        s.customHotkeyCode = code
        s.customHotkeyName = name.isEmpty ? "Taste \(code)" : name
    }
}
