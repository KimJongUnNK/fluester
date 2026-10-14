import Foundation

/// Optionen für die regelbasierte Bereinigung.
public struct CleanupOptions: Equatable, Sendable {
    public var removeFillers = true
    public var selfCorrections = true
    public var spokenCommands = true
    public var spokenPunctuation = false
    public var formatLists = true
    public var style: WritingStyle = .formal

    public init(removeFillers: Bool = true, selfCorrections: Bool = true, spokenCommands: Bool = true,
                spokenPunctuation: Bool = false, formatLists: Bool = true, style: WritingStyle = .formal) {
        self.removeFillers = removeFillers
        self.selfCorrections = selfCorrections
        self.spokenCommands = spokenCommands
        self.spokenPunctuation = spokenPunctuation
        self.formatLists = formatLists
        self.style = style
    }

    public init(_ s: Settings, style: WritingStyle) {
        self.init(removeFillers: s.removeFillers, selfCorrections: s.applySelfCorrections, spokenCommands: s.spokenCommands,
                  spokenPunctuation: s.spokenPunctuation, formatLists: s.formatLists, style: style)
    }
}

/// Regelbasierte Textbereinigung. Läuft immer: vor dem Sprachmodell (Füllwörter, Befehle) und danach
/// (Wörterbuch, Leerzeichen, Stil). Ohne Sprachmodell erledigt sie alles allein.
public enum TextPipeline {

    // MARK: Gesamtablauf

    /// Nur Regeln, kein Sprachmodell.
    public static func ruleBased(_ raw: String, options: CleanupOptions, dictionary: [DictionaryEntry] = []) -> String {
        var t = preprocess(raw, options: options)
        if options.selfCorrections && options.style != .raw { t = applySelfCorrections(t) }
        if options.formatLists && options.style != .raw { t = formatLists(t) }
        return finalize(t, options: options, dictionary: dictionary)
    }

    /// Vor dem Sprachmodell: was Regeln sicher können.
    public static func preprocess(_ raw: String, options: CleanupOptions) -> String {
        var t = raw.replacingOccurrences(of: "\r\n", with: "\n")
        t = collapseSpaces(t)
        if options.spokenCommands { t = applySpokenCommands(t) }
        if options.spokenPunctuation { t = applySpokenPunctuation(t) }
        if options.removeFillers {
            t = removeFillers(t)
            t = removeStutters(t)
        }
        return fixSpacing(t)
    }

    /// Nach dem Sprachmodell: Wörterbuch, Leerzeichen, Großschreibung, Stil.
    public static func finalize(_ text: String, options: CleanupOptions, dictionary: [DictionaryEntry] = []) -> String {
        var t = Vocabulary.applyDictionary(text, dictionary)
        t = fixSpacing(t)
        switch options.style {
        case .raw:
            t = capitalizeSentences(t)
        case .formal, .excited:
            t = capitalizeSentences(t)
            t = ensureFinalPunctuation(t)
        case .casual:
            t = capitalizeSentences(t)
            t = dropFinalPeriod(t)
        case .veryCasual:
            t = dropFinalPeriod(t).lowercased()
        }
        return t.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    // MARK: Whisper-Halluzinationen

    /// Bekannte Phantom-Sätze, die Whisper bei Stille/Rauschen erfindet, und Klammer-Marker wie [BLANK_AUDIO].
    static let hallucinations = [
        "untertitel im auftrag des zdf", "untertitelung des zdf", "untertitel von stephanie geiges", "untertitelung im auftrag des zdf",
        "vielen dank fürs zuschauen", "danke fürs zuschauen", "bis zum nächsten mal", "copyright wdr", "swr 2020", "amara.org",
        "thank you for watching", "thanks for watching", "please subscribe", "subtitles by the amara.org community"
    ]

    public static func stripHallucinations(_ text: String) -> String {
        var t = regexReplace(text, "\\[[^\\]]{1,40}\\]|\\((?:musik|music|applaus|applause|lachen|laughter|stille|silence|räuspern|seufzen)[^)]{0,20}\\)|\\*[^*]{1,30}\\*", " ")
        let low = t.lowercased()
        for h in hallucinations where low.contains(h) {
            // Nur entfernen, wenn der Satz allein steht (nicht mitten in echtem Inhalt)
            t = regexReplace(t, "(?:^|(?<=[.!?]))\\s*[^.!?]*" + NSRegularExpression.escapedPattern(for: h) + "[^.!?]*[.!?]?", " ")
        }
        return collapseSpaces(t).trimmingCharacters(in: .whitespacesAndNewlines)
    }

    // MARK: Füllwörter und Stotterer

    /// Reine Verlegenheitslaute, die nie Inhalt sind.
    static let hesitation = "(?:ä+h+m*|ö+h+m*|e+h+m+|u+h+m+|u+m{2,}|u+h{2,}|e+r+m+|h+m{2,}|ä+m+)"
    /// Mehrdeutig (dt. "um" = Präposition): nur wenn mit Komma/Auslassung abgesetzt.
    static let ambiguous = "(?:uh|um|hm|eh)"
    /// Nach diesen Wörtern bleibt das Komma vor dem entfernten Füllwort stehen ("…, äh, dass" → "…, dass").
    static let commaKeepers: Set<String> = ["dass", "weil", "wenn", "ob", "obwohl", "damit", "sodass", "bevor", "nachdem", "während", "falls",
                                             "sobald", "aber", "sondern", "denn", "doch", "jedoch", "als", "wie", "welche", "welcher", "welches",
                                             "but", "which", "who", "because"]

    public static func removeFillers(_ text: String) -> String {
        var t = text
        // 1) eindeutige Laute überall: optional mit Kommas/Auslassung drumherum
        t = replaceFiller(t, core: hesitation, requireDelimiter: false)
        // 2) mehrdeutige nur, wenn sie per Komma/Auslassung abgesetzt sind
        t = replaceFiller(t, core: ambiguous, requireDelimiter: true)
        return t
    }

    private static func replaceFiller(_ text: String, core: String, requireDelimiter: Bool) -> String {
        let after = requireDelimiter ? "(?:[ \\t]*(?:,|…|\\.\\.\\.))" : "(?:[ \\t]*(?:,|…|\\.\\.\\.))?"
        let pattern = "([ \\t]*,)?[ \\t]*(?<![\\p{L}\\p{N}])" + core + "(?![\\p{L}\\p{N}])" + after
        guard let re = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]) else { return text }
        var result = ""
        var last = text.startIndex
        let ns = NSRange(text.startIndex..., in: text)
        for m in re.matches(in: text, options: [], range: ns) {
            guard let r = Range(m.range, in: text) else { continue }
            // Mehrdeutige am Satzanfang nur mit Komma danach – sonst überall nur mit Komma davor ODER Satzanfang
            if requireDelimiter {
                let before = text[text.startIndex..<r.lowerBound]
                let hadComma = m.range(at: 1).location != NSNotFound
                let atStart = before.trimmingCharacters(in: .whitespaces).isEmpty || before.last == "\n"
                    || ".!?".contains(before.trimmingCharacters(in: .whitespaces).last ?? " ")
                if !hadComma && !atStart { continue }
            }
            result += text[last..<r.lowerBound]
            let hadLeadingComma = m.range(at: 1).location != NSNotFound
            let rest = text[r.upperBound...]
            let nextWord = rest.drop(while: { $0.isWhitespace }).prefix(while: { $0.isLetter }).lowercased()
            if hadLeadingComma && commaKeepers.contains(nextWord) {
                result += ", "
            } else {
                result += " "
            }
            last = r.upperBound
        }
        result += text[last...]
        return fixSpacing(result)
    }

    /// Stotterer: "ich ich wollte" → "ich wollte". Artikel nur ab dreifacher Wiederholung ("die die" ist oft korrekt).
    static let doubleOK: Set<String> = ["der", "die", "das", "den", "dem", "des", "had", "that", "sie", "is", "es", "ja", "nein", "no", "so",
                                         "na", "ha", "haha", "bye", "very", "sehr", "yeah", "tschüss", "danke", "bitte", "gut", "mal"]

    public static func removeStutters(_ text: String) -> String {
        guard let re = try? NSRegularExpression(pattern: "(?<![\\p{L}\\p{N}])(\\p{L}+)((?:[ ,]+\\1)+)(?![\\p{L}\\p{N}])", options: [.caseInsensitive]) else { return text }
        var result = ""
        var last = text.startIndex
        for m in re.matches(in: text, options: [], range: NSRange(text.startIndex..., in: text)) {
            guard let r = Range(m.range, in: text), let w = Range(m.range(at: 1), in: text), let rep = Range(m.range(at: 2), in: text) else { continue }
            let word = String(text[w])
            let repeats = text[rep].split(whereSeparator: { $0 == " " || $0 == "," }).count
            if doubleOK.contains(word.lowercased()) && repeats < 2 { continue }
            result += text[last..<r.lowerBound]
            result += word
            last = r.upperBound
        }
        result += text[last...]
        return result
    }

    // MARK: Gesprochene Befehle

    public static func applySpokenCommands(_ text: String) -> String {
        var t = text
        let paragraph = "(?:neuer|neuen|nächster|nächsten)\\s+absatz|new\\s+paragraph|next\\s+paragraph"
        let line = "(?:neue|nächste)\\s+zeile|zeilenumbruch|new\\s+line|next\\s+line|line\\s+break"
        t = regexReplace(t, "[ \\t]*(?:[,;:][ \\t]*)?(?<![\\p{L}])(?:" + paragraph + ")(?![\\p{L}])[.,;:!]?[ \\t]*", "\n\n")
        t = regexReplace(t, "[ \\t]*(?:[,;:][ \\t]*)?(?<![\\p{L}])(?:" + line + ")(?![\\p{L}])[.,;:!]?[ \\t]*", "\n")
        return t
    }

    static let punctuationWords: [(String, String)] = [
        ("fragezeichen", "?"), ("ausrufezeichen", "!"), ("doppelpunkt", ":"), ("semikolon", ";"), ("strichpunkt", ";"),
        ("komma", ","), ("punkt", "."), ("bindestrich", "-"), ("gedankenstrich", " –"), ("klammer auf", " ("), ("klammer zu", ")"),
        ("question mark", "?"), ("exclamation mark", "!"), ("exclamation point", "!"), ("full stop", "."), ("period", "."),
        ("comma", ","), ("colon", ":"), ("semicolon", ";"), ("open paren", " ("), ("close paren", ")"), ("dash", " –")
    ]

    /// "Hallo Komma wie geht's Fragezeichen" → "Hallo, wie geht's?" (nur wenn eingeschaltet).
    public static func applySpokenPunctuation(_ text: String) -> String {
        var t = text
        for (word, sign) in punctuationWords {
            let w = word.split(separator: " ").map { NSRegularExpression.escapedPattern(for: String($0)) }.joined(separator: "\\s+")
            // Satzzeichen, das die Erkennung selbst schon gesetzt hat, wird ersetzt
            t = regexReplace(t, "[ \\t]*[,.]?[ \\t]*(?<![\\p{L}])" + w + "(?![\\p{L}])[,.]?", NSRegularExpression.escapedTemplate(for: sign))
        }
        return fixSpacing(t)
    }

    // MARK: Selbstkorrekturen

    /// Anker-Marker: danach kommt die Korrektur des unmittelbar Gesagten.
    static let correctionMarkers = [
        "nein, ich meine", "nein ich meine", "nein, warte", "nein warte", "ich meine", "ich mein", "ich meinte", "besser gesagt",
        "oder besser", "korrigiere", "korrektur", "sorry, ich meine", "pardon",
        "no, i mean", "no wait", "no, wait", "i mean", "i meant", "sorry, i meant", "or rather", "correction"
    ]
    /// Löschen-Marker: das Vorherige (Satz oder Teilsatz) fällt weg.
    static let scratchMarkers = ["streich das", "vergiss das", "lösch das", "scratch that", "forget that", "delete that"]

    public static func applySelfCorrections(_ text: String) -> String {
        var t = text
        var guardCount = 0
        while guardCount < 8, let next = correctOnce(t) {
            t = next
            guardCount += 1
        }
        return fixSpacing(t)
    }

    private static func correctOnce(_ text: String) -> String? {
        let lower = text.lowercased()
        // 1) "streich das"
        for m in scratchMarkers {
            guard let r = lower.range(of: m) else { continue }
            let lo = text.index(text.startIndex, offsetBy: lower.distance(from: lower.startIndex, to: r.lowerBound))
            let hi = text.index(text.startIndex, offsetBy: lower.distance(from: lower.startIndex, to: r.upperBound))
            let wordStartOK = lo == text.startIndex || !(text[text.index(before: lo)].isLetter)
            let wordEndOK = hi == text.endIndex || !(text[hi].isLetter)
            guard wordStartOK, wordEndOK else { continue }
            var before = String(text[..<lo]).trimmingCharacters(in: .whitespaces)
            let after = String(String(text[hi...]).drop(while: { ".,;:!?… ".contains($0) }))
            let endsSentence = before.last.map { ".!?\n".contains($0) } ?? true
            if !endsSentence {
                // mitten im Satz: alles ab Satzanfang fällt weg, der Teil davor bleibt
                if let cut = before.lastIndex(where: { ".!?\n".contains($0) }) {
                    before = String(before[...cut])
                } else {
                    before = ""
                }
            } else {
                // eigener Satz: vorherigen Satz entfernen
                var b = before
                if let l = b.last, ".!?".contains(l) { b.removeLast() }
                if let cut = b.lastIndex(where: { ".!?\n".contains($0) }) {
                    before = String(b[...cut])
                } else {
                    before = ""
                }
            }
            return (before + " " + after).trimmingCharacters(in: .whitespaces)
        }
        // 2) "…, ich meine …"
        for m in correctionMarkers {
            let body = m.split(separator: " ").map { NSRegularExpression.escapedPattern(for: String($0)) }.joined(separator: "\\s+")
            let pattern = ",\\s*" + body + "(?![\\p{L}])\\s*,?\\s*"
            guard let re = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]),
                  let match = re.firstMatch(in: text, options: [], range: NSRange(text.startIndex..., in: text)),
                  let r = Range(match.range, in: text) else { continue }
            let left = String(text[..<r.lowerBound])
            let right = String(text[r.upperBound...])
            // Korrektur = Rest bis zum nächsten Satz-/Teilsatzende
            let corrEnd = right.firstIndex(where: { ".!?;,\n".contains($0) }) ?? right.endIndex
            let correction = String(right[..<corrEnd]).trimmingCharacters(in: .whitespaces)
            let corrWords = correction.split(separator: " ").map(String.init)
            guard let first = corrWords.first?.lowercased() else { continue }
            // Nur im aktuellen Satz suchen
            let sentenceStart = left.lastIndex(where: { ".!?\n".contains($0) }).map { left.index(after: $0) } ?? left.startIndex
            let head = String(left[..<sentenceStart])
            let sentence = String(left[sentenceStart...])
            var words = sentence.split(separator: " ", omittingEmptySubsequences: true).map(String.init)
            guard !words.isEmpty else { continue }
            // Anker: gleiches Anfangswort in den letzten 6 Wörtern ("um 3, ich meine um 4")
            var cutAt: Int? = nil
            for i in stride(from: words.count - 1, through: max(0, words.count - 6), by: -1) {
                if words[i].lowercased().trimmingCharacters(in: .punctuationCharacters) == first { cutAt = i; break }
            }
            if cutAt == nil && corrWords.count == 1 && words.count >= 2 {
                cutAt = words.count - 1 // "am Montag, ich meine Dienstag" → letztes Wort ersetzen
            }
            // Nicht sicher, was korrigiert wird: Text unverändert lassen (das Sprachmodell kann es besser)
            guard let c = cutAt else { continue }
            words.removeSubrange(c...)
            let kept = (head + (head.isEmpty || head.hasSuffix("\n") ? "" : " ") + words.joined(separator: " ")).trimmingCharacters(in: .whitespaces)
            return (kept.isEmpty ? right : kept + " " + right).trimmingCharacters(in: .whitespaces)
        }
        return nil
    }

    // MARK: Aufzählungen

    static let ordinals: [[String]] = [
        ["erstens", "first", "firstly"], ["zweitens", "second", "secondly"], ["drittens", "third", "thirdly"],
        ["viertens", "fourth", "fourthly"], ["fünftens", "fifth", "fifthly"], ["sechstens", "sixth"], ["siebtens", "seventh"]
    ]

    /// "Ich brauche erstens Milch zweitens Brot drittens Eier" → nummerierte Liste.
    public static func formatLists(_ text: String) -> String {
        let all = ordinals.flatMap { $0 }.joined(separator: "|")
        guard let re = try? NSRegularExpression(pattern: "(?<![\\p{L}])(" + all + ")(?![\\p{L}])[,:]?", options: [.caseInsensitive]) else { return text }
        let matches = re.matches(in: text, options: [], range: NSRange(text.startIndex..., in: text))
        guard matches.count >= 2 else { return text }
        // Reihenfolge prüfen: 1, 2, 3 …
        var ranks: [Int] = []
        for m in matches {
            guard let r = Range(m.range(at: 1), in: text) else { return text }
            let w = text[r].lowercased()
            guard let rank = ordinals.firstIndex(where: { $0.contains(w) }) else { return text }
            ranks.append(rank)
        }
        guard ranks == Array(0..<ranks.count) else { return text }
        guard let firstRange = Range(matches[0].range, in: text) else { return text }
        var intro = String(text[..<firstRange.lowerBound]).trimmingCharacters(in: .whitespaces)
        while let l = intro.last, ",;".contains(l) { intro.removeLast() }
        var items: [String] = []
        for (i, m) in matches.enumerated() {
            guard let r = Range(m.range, in: text) else { return text }
            let end = i + 1 < matches.count ? (Range(matches[i + 1].range, in: text)?.lowerBound ?? text.endIndex) : text.endIndex
            var item = String(text[r.upperBound..<end]).trimmingCharacters(in: .whitespacesAndNewlines)
            while let l = item.last, ",;.".contains(l) { item.removeLast() }
            item = item.trimmingCharacters(in: .whitespaces)
            for conj in [" und", " and", " sowie", " oder", " or", " plus"] where item.lowercased().hasSuffix(conj) {
                item = String(item.dropLast(conj.count)).trimmingCharacters(in: .whitespaces)
            }
            while let l = item.last, ",;.".contains(l) { item.removeLast() }
            if item.isEmpty { return text }
            items.append(capitalizeFirst(item))
        }
        let list = items.enumerated().map { "\($0.offset + 1). \($0.element)" }.joined(separator: "\n")
        if intro.isEmpty { return list }
        if !intro.hasSuffix(":") { intro = intro.trimmingCharacters(in: CharacterSet(charactersIn: ".")) + ":" }
        return intro + "\n" + list
    }

    // MARK: Leerzeichen, Großschreibung, Satzende

    public static func collapseSpaces(_ s: String) -> String {
        regexReplace(s, "[ \\t\\x{00A0}]{2,}", " ")
    }

    public static func fixSpacing(_ s: String) -> String {
        var t = collapseSpaces(s)
        t = regexReplace(t, "[ \\t]+([,.;:!?…)\\]])", "$1")         // kein Leerzeichen vor Satzzeichen
        t = regexReplace(t, "([(\\[])[ \\t]+", "$1")               // keins nach öffnender Klammer
        t = regexReplace(t, ",\\s*,", ",")                         // doppelte Kommas
        t = regexReplace(t, "([,;])(\\p{L})", "$1 $2")             // Leerzeichen nach Komma/Semikolon
        t = regexReplace(t, "([!?])(\\p{Lu})", "$1 $2")            // und nach !/? vor neuem Satz
        t = regexReplace(t, "[ \\t]*\\n[ \\t]*", "\n")             // Zeilen sauber
        t = regexReplace(t, "\\n{3,}", "\n\n")
        t = regexReplace(t, "^[ \\t,;]+", "")                      // kein Komma am Anfang
        return t.trimmingCharacters(in: .whitespaces)
    }

    static let abbreviations: Set<String> = ["z", "b", "bzw", "usw", "ca", "d", "h", "u", "a", "evtl", "ggf", "inkl", "nr", "dr", "etc", "e.g", "i.e",
                                             "vs", "mr", "mrs", "ms", "st", "bspw", "vgl", "sog", "zb", "prof", "mio", "mrd", "jh", "abs", "s"]

    /// Großbuchstabe am Textanfang, nach Zeilenumbruch und nach .!? (außer Abkürzungen und Ordnungszahlen).
    public static func capitalizeSentences(_ s: String) -> String {
        var chars = Array(s)
        var capNext = true
        var i = 0
        while i < chars.count {
            let c = chars[i]
            if capNext && c.isLetter {
                let up = String(c).uppercased()
                if up.count == 1 { chars[i] = Character(up) }
                capNext = false
            } else if c == "\n" {
                capNext = true
            } else if ".!?".contains(c) {
                // Folgt Leerzeichen? Dann ist es ein Satzende – außer nach Abkürzung oder Zahl
                let nextIsSpace = i + 1 < chars.count && (chars[i + 1] == " " || chars[i + 1] == "\n")
                if nextIsSpace {
                    if c == "." {
                        var j = i - 1
                        var token = ""
                        while j >= 0 && (chars[j].isLetter || chars[j].isNumber || chars[j] == ".") { token = String(chars[j]) + token; j -= 1 }
                        let t = token.lowercased()
                        let isAbbrev = abbreviations.contains(t) || (t.count == 1 && t.first!.isLetter)
                        let isOrdinal = !t.isEmpty && t.allSatisfy { $0.isNumber }
                        capNext = !(isAbbrev || isOrdinal)
                    } else {
                        capNext = true
                    }
                }
            } else if !c.isWhitespace && !"\"„“'(«»".contains(c) {
                capNext = false
            }
            i += 1
        }
        return String(chars)
    }

    public static func capitalizeFirst(_ s: String) -> String {
        guard let f = s.first else { return s }
        return String(f).uppercased() + s.dropFirst()
    }

    /// Satzende ergänzen, wenn es wie ein Satz aussieht (mind. 3 Wörter, endet mit Buchstabe/Zahl).
    public static func ensureFinalPunctuation(_ s: String) -> String {
        let t = s.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let last = t.last, last.isLetter || last.isNumber else { return t }
        // Letzte Zeile einer Liste nicht anfassen
        let lastLine = t.split(separator: "\n").last.map(String.init) ?? t
        if lastLine.range(of: "^\\s*(\\d+[.)]|[-•*])\\s", options: .regularExpression) != nil { return t }
        let words = lastLine.split(whereSeparator: { $0.isWhitespace }).count
        return words >= 3 ? t + "." : t
    }

    /// Lockerer Stil: Punkt am Ende einer kurzen Nachricht weg (Fragezeichen/Ausrufezeichen bleiben).
    public static func dropFinalPeriod(_ s: String) -> String {
        var t = s.trimmingCharacters(in: .whitespacesAndNewlines)
        if t.hasSuffix(".") && !t.hasSuffix("..") && !t.contains("\n") {
            let sentences = t.split(whereSeparator: { ".!?".contains($0) }).count
            if sentences <= 2 { t.removeLast() }
        }
        return t
    }

    // MARK: Hilfen

    static func regexReplace(_ s: String, _ pattern: String, _ template: String) -> String {
        guard let re = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]) else { return s }
        return re.stringByReplacingMatches(in: s, options: [], range: NSRange(s.startIndex..., in: s), withTemplate: template)
    }
}
