import Foundation

/// Ein Wort im persönlichen Wörterbuch: richtige Schreibweise plus optionale Varianten, wie die Erkennung es oft verhört.
public struct DictionaryEntry: Codable, Equatable, Identifiable, Hashable, Sendable {
    public var id: UUID
    /// So soll es geschrieben werden, z. B. "Voltage Events"
    public var term: String
    /// So kommt es aus der Erkennung, z. B. ["voltage events", "voltage event's"]
    public var soundsLike: [String]

    public init(id: UUID = UUID(), term: String, soundsLike: [String] = []) {
        self.id = id
        self.term = term
        self.soundsLike = soundsLike
    }
}

/// Textbaustein: gesprochenes Stichwort wird zu einem längeren Text.
public struct Snippet: Codable, Equatable, Identifiable, Hashable, Sendable {
    public var id: UUID
    /// z. B. "meine Adresse"
    public var trigger: String
    /// z. B. "Musterstraße 1, 6850 Dornbirn"
    public var text: String

    public init(id: UUID = UUID(), trigger: String, text: String) {
        self.id = id
        self.trigger = trigger
        self.text = text
    }
}

public enum Vocabulary {
    /// Wörter, die der Erkennung als Kontext/Prompt mitgegeben werden (Namen, Fachbegriffe).
    public static func hints(_ entries: [DictionaryEntry], snippets: [Snippet] = [], limit: Int = 80) -> [String] {
        var seen = Set<String>()
        var out: [String] = []
        for t in entries.map(\.term) + snippets.map(\.trigger) {
            let s = t.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !s.isEmpty, seen.insert(s.lowercased()).inserted else { continue }
            out.append(s)
            if out.count >= limit { break }
        }
        return out
    }

    /// Ersetzt Varianten und falsche Groß-/Kleinschreibung durch die Wörterbuch-Schreibweise.
    /// Ganze Wörter, ohne Rücksicht auf Groß/klein, längste Varianten zuerst.
    public static func applyDictionary(_ text: String, _ entries: [DictionaryEntry]) -> String {
        var pairs: [(String, String)] = []
        for e in entries {
            let term = e.term.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !term.isEmpty else { continue }
            pairs.append((term, term))
            for v in e.soundsLike {
                let s = v.trimmingCharacters(in: .whitespacesAndNewlines)
                if !s.isEmpty { pairs.append((s, term)) }
            }
        }
        pairs.sort { $0.0.count > $1.0.count }
        var result = text
        for (from, to) in pairs {
            result = replaceWholeWords(in: result, find: from, with: to)
        }
        return result
    }

    /// Text normalisieren für den Vergleich mit Snippet-Stichworten: klein, ohne Satzzeichen, einfache Leerzeichen.
    public static func normalize(_ s: String) -> String {
        let lowered = s.lowercased()
        var out = ""
        var lastSpace = true
        for ch in lowered {
            if ch.isLetter || ch.isNumber {
                out.append(ch)
                lastSpace = false
            } else if !lastSpace {
                out.append(" ")
                lastSpace = true
            }
        }
        return out.trimmingCharacters(in: .whitespaces)
    }

    /// Ganze Aussage ist ein Stichwort → nur der Baustein. Sonst Stichwort im Satz ersetzen.
    /// Liefert (Text, ob ein Baustein verwendet wurde).
    public static func expandSnippets(_ text: String, _ snippets: [Snippet]) -> (text: String, matchedWhole: Bool) {
        let valid = snippets.filter { !normalize($0.trigger).isEmpty }
        guard !valid.isEmpty else { return (text, false) }
        let norm = normalize(text)
        // Auch "Snippet meine Adresse" oder "füge meine Adresse ein" zählt als ganzes Stichwort
        let fillers = ["snippet ", "baustein ", "füge ", "einfügen ", "insert ", "paste "]
        var stripped = norm
        for f in fillers where stripped.hasPrefix(f) { stripped = String(stripped.dropFirst(f.count)) }
        for suffix in [" ein", " einfügen", " please", " bitte"] where stripped.hasSuffix(suffix) { stripped = String(stripped.dropLast(suffix.count)) }
        if let s = valid.first(where: { normalize($0.trigger) == norm || normalize($0.trigger) == stripped }) {
            return (s.text, true)
        }
        var result = text
        for s in valid.sorted(by: { $0.trigger.count > $1.trigger.count }) {
            result = replaceWholeWords(in: result, find: s.trigger, with: s.text)
        }
        return (result, false)
    }

    /// Ersetzt `find` als ganzes Wort/ganze Wortfolge (Groß/klein egal). Mehrere Leerzeichen im Text werden toleriert.
    public static func replaceWholeWords(in text: String, find: String, with replacement: String) -> String {
        let words = find.split(whereSeparator: { $0.isWhitespace }).map { NSRegularExpression.escapedPattern(for: String($0)) }
        guard !words.isEmpty else { return text }
        let pattern = "(?<![\\p{L}\\p{N}])" + words.joined(separator: "\\s+") + "(?![\\p{L}\\p{N}])"
        guard let re = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]) else { return text }
        let range = NSRange(text.startIndex..., in: text)
        return re.stringByReplacingMatches(in: text, options: [], range: range, withTemplate: NSRegularExpression.escapedTemplate(for: replacement))
    }
}
