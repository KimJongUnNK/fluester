import Foundation

/// Schreibstil je nach App, wie bei Wispr Flow.
public enum WritingStyle: String, Codable, CaseIterable, Identifiable, Sendable {
    case formal, casual, veryCasual, excited, raw
    public var id: String { rawValue }
    public var label: String {
        switch self {
        case .formal: return "Formell"
        case .casual: return "Locker"
        case .veryCasual: return "Sehr locker"
        case .excited: return "Begeistert"
        case .raw: return "Wörtlich"
        }
    }
    public var example: String {
        switch self {
        case .formal: return "Hallo Lisa, passt dir Donnerstag um 15 Uhr? Liebe Grüße"
        case .casual: return "Hallo Lisa, passt dir Donnerstag um 15 Uhr"
        case .veryCasual: return "hallo lisa passt dir donnerstag um 15 uhr"
        case .excited: return "Hallo Lisa! Passt dir Donnerstag um 15 Uhr?"
        case .raw: return "Genau wie gesprochen, nur Füllwörter weg"
        }
    }
    /// Anweisung an das Sprachmodell.
    public var prompt: String {
        switch self {
        case .formal: return "Stil: formell und korrekt. Vollständige Sätze, korrekte Groß-/Kleinschreibung und Zeichensetzung."
        case .casual: return "Stil: locker. Normale Groß-/Kleinschreibung, sparsame Zeichensetzung, kein Punkt am Ende einer kurzen Nachricht."
        case .veryCasual: return "Stil: sehr locker wie im Chat. Alles kleingeschrieben, kaum Satzzeichen, kein Punkt am Ende."
        case .excited: return "Stil: begeistert und freundlich. Ausrufezeichen wo passend, aber nicht übertreiben."
        case .raw: return "Stil: wörtlich. Formuliere nichts um, korrigiere nur Zeichensetzung und entferne Füllwörter."
        }
    }
}

/// App-Kategorien, für die je ein Stil gilt.
public enum AppCategory: String, Codable, CaseIterable, Identifiable, Sendable {
    case messages, email, work, code, other
    public var id: String { rawValue }
    public var label: String {
        switch self {
        case .messages: return "Private Nachrichten"
        case .email: return "E-Mail"
        case .work: return "Arbeits-Chats"
        case .code: return "Code & Terminal"
        case .other: return "Alle anderen Apps"
        }
    }
    public var appsHint: String {
        switch self {
        case .messages: return "Nachrichten, WhatsApp, Telegram, Signal, Instagram"
        case .email: return "Mail, Outlook, Gmail, Spark, Superhuman"
        case .work: return "Slack, Teams, Discord, Notion, Linear"
        case .code: return "Xcode, VS Code, Cursor, Terminal, iTerm"
        case .other: return "Notizen, Pages, Word, Browser …"
        }
    }
}

public struct StyleSettings: Codable, Equatable, Sendable {
    public var messages: WritingStyle = .casual
    public var email: WritingStyle = .formal
    public var work: WritingStyle = .casual
    public var code: WritingStyle = .raw
    public var other: WritingStyle = .formal
    /// Eigene Zuordnung Bundle-ID → Kategorie (überschreibt die eingebaute)
    public var overrides: [String: AppCategory] = [:]

    public init() {}

    public func style(for category: AppCategory) -> WritingStyle {
        switch category {
        case .messages: return messages
        case .email: return email
        case .work: return work
        case .code: return code
        case .other: return other
        }
    }

    public mutating func set(_ style: WritingStyle, for category: AppCategory) {
        switch category {
        case .messages: messages = style
        case .email: email = style
        case .work: work = style
        case .code: code = style
        case .other: other = style
        }
    }
}

public enum AppCategorizer {
    /// Bekannte Apps nach Bundle-ID (Präfix reicht).
    static let known: [(String, AppCategory)] = [
        ("com.apple.MobileSMS", .messages), ("net.whatsapp.WhatsApp", .messages), ("desktop.WhatsApp", .messages),
        ("ru.keepcoder.Telegram", .messages), ("com.tdesktop.Telegram", .messages), ("org.whispersystems.signal-desktop", .messages),
        ("com.facebook.archon", .messages), ("com.burbn.instagram", .messages), ("com.apple.FaceTime", .messages),
        ("com.apple.mail", .email), ("com.microsoft.Outlook", .email), ("com.readdle.smartemail-Mac", .email), ("com.superhuman.electron", .email),
        ("it.bloop.airmail2", .email), ("com.mimestream.Mimestream", .email), ("com.google.Gmail", .email),
        ("com.tinyspeck.slackmacgap", .work), ("com.microsoft.teams", .work), ("com.microsoft.teams2", .work), ("com.hnc.Discord", .work),
        ("notion.id", .work), ("com.linear", .work), ("com.figma.Desktop", .work), ("com.atlassian.trello", .work),
        ("com.apple.dt.Xcode", .code), ("com.microsoft.VSCode", .code), ("com.todesktop.230313mzl4w4u92", .code), ("com.exafunction.windsurf", .code),
        ("com.apple.Terminal", .code), ("com.googlecode.iterm2", .code), ("dev.warp.Warp-Stable", .code), ("com.jetbrains", .code),
        ("dev.zed.Zed", .code), ("com.sublimetext", .code), ("com.mitchellh.ghostty", .code)
    ]

    /// Webseiten im Browser erkennen wir am Fenstertitel (Gmail, WhatsApp Web …).
    static let titleHints: [(String, AppCategory)] = [
        ("gmail", .email), ("outlook", .email), ("posteingang", .email), ("inbox", .email),
        ("whatsapp", .messages), ("messenger", .messages), ("telegram", .messages), ("instagram", .messages),
        ("slack", .work), ("teams", .work), ("discord", .work), ("notion", .work), ("linear", .work),
        ("github", .code), ("gitlab", .code), ("stack overflow", .code), ("replit", .code)
    ]

    public static func category(bundleID: String?, windowTitle: String? = nil, overrides: [String: AppCategory] = [:]) -> AppCategory {
        guard let id = bundleID, !id.isEmpty else { return .other }
        if let o = overrides[id] { return o }
        if let hit = known.first(where: { id == $0.0 || id.hasPrefix($0.0 + ".") || ($0.0.hasSuffix("jetbrains") && id.hasPrefix($0.0)) || ($0.0 == "com.sublimetext" && id.hasPrefix($0.0)) }) {
            return hit.1
        }
        let browsers = ["com.apple.Safari", "com.google.Chrome", "company.thebrowser.Browser", "org.mozilla.firefox", "com.brave.Browser",
                        "com.microsoft.edgemac", "com.operasoftware.Opera", "app.zen-browser.zen", "com.vivaldi.Vivaldi"]
        if browsers.contains(where: { id.hasPrefix($0) }), let t = windowTitle?.lowercased() {
            if let hit = titleHints.first(where: { t.contains($0.0) }) { return hit.1 }
        }
        return .other
    }
}
