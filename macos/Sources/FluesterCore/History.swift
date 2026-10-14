import Foundation

/// Ein Diktat im Verlauf.
public struct HistoryItem: Codable, Identifiable, Equatable, Sendable {
    public var id: UUID
    public var date: Date
    public var raw: String
    public var text: String
    public var appName: String
    public var bundleID: String
    public var seconds: Double
    public var isCommand: Bool
    public var engine: String

    public init(id: UUID = UUID(), date: Date = Date(), raw: String, text: String, appName: String, bundleID: String,
                seconds: Double, isCommand: Bool = false, engine: String = "") {
        self.id = id
        self.date = date
        self.raw = raw
        self.text = text
        self.appName = appName
        self.bundleID = bundleID
        self.seconds = seconds
        self.isCommand = isCommand
        self.engine = engine
    }

    public var words: Int { Stats.wordCount(text) }
}

public struct StatsSummary: Equatable, Sendable {
    public var totalWords: Int
    public var weekWords: Int
    public var todayWords: Int
    public var dictations: Int
    /// Wörter pro Minute beim Sprechen
    public var wpm: Int
    /// Gesparte Minuten gegenüber Tippen mit 40 WPM
    public var minutesSaved: Int
    public var streakDays: Int
}

public enum Stats {
    public static func wordCount(_ s: String) -> Int {
        s.split(whereSeparator: { $0.isWhitespace || $0.isNewline }).filter { $0.contains(where: { $0.isLetter || $0.isNumber }) }.count
    }

    public static func summary(_ items: [HistoryItem], now: Date = Date(), calendar: Calendar = .current) -> StatsSummary {
        let dictations = items.filter { !$0.isCommand }
        let total = dictations.reduce(0) { $0 + $1.words }
        let startOfDay = calendar.startOfDay(for: now)
        let weekAgo = calendar.date(byAdding: .day, value: -7, to: now) ?? now
        let today = dictations.filter { $0.date >= startOfDay }.reduce(0) { $0 + $1.words }
        let week = dictations.filter { $0.date >= weekAgo }.reduce(0) { $0 + $1.words }
        let secs = dictations.reduce(0.0) { $0 + $1.seconds }
        let wpm = secs > 5 ? Int((Double(total) / (secs / 60)).rounded()) : 0
        let typingMinutes = Double(total) / 40
        let saved = max(0, Int((typingMinutes - secs / 60).rounded()))
        // Tage in Folge mit mindestens einem Diktat
        let days = Set(dictations.map { calendar.startOfDay(for: $0.date) })
        var streak = 0
        var d = startOfDay
        while days.contains(d) {
            streak += 1
            guard let prev = calendar.date(byAdding: .day, value: -1, to: d) else { break }
            d = prev
        }
        return StatsSummary(totalWords: total, weekWords: week, todayWords: today, dictations: dictations.count, wpm: wpm, minutesSaved: saved, streakDays: streak)
    }
}
