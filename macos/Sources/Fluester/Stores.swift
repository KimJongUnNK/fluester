import Foundation
import Combine
import FluesterCore

/// Einstellungen als ObservableObject für SwiftUI, gespeichert als JSON in den UserDefaults.
final class SettingsStore: ObservableObject {
    static let shared = SettingsStore()
    private static let key = "fluester.settings.v1"

    @Published var s: Settings {
        didSet { if s != oldValue { save() } }
    }

    private init() {
        if let data = UserDefaults.standard.data(forKey: Self.key),
           let decoded = try? JSONDecoder().decode(Settings.self, from: data) {
            s = decoded
        } else {
            s = Settings()
        }
    }

    func save() {
        if let data = try? JSONEncoder().encode(s) { UserDefaults.standard.set(data, forKey: Self.key) }
    }

    /// Schlüssel für die aktuelle Erkennung/Bereinigung
    var transcriptionKey: String? { Keychain.apiKey(for: s.cloudTranscription.providerID) }
    var chatKey: String? { Keychain.apiKey(for: s.cloudChat.providerID) }
}

/// Verlauf der Diktate (JSON-Datei, lokal).
final class HistoryStore: ObservableObject {
    @Published private(set) var items: [HistoryItem] = []
    private let url = AppPaths.support.appendingPathComponent("history.json")
    private let maxItems = 2000

    init() { load() }

    func add(_ item: HistoryItem) {
        items.insert(item, at: 0)
        if items.count > maxItems { items.removeLast(items.count - maxItems) }
        save()
    }

    func delete(_ id: UUID) {
        items.removeAll { $0.id == id }
        save()
    }

    func clear() {
        items = []
        save()
    }

    var summary: StatsSummary { Stats.summary(items) }
    var last: HistoryItem? { items.first }

    private func load() {
        guard let data = try? Data(contentsOf: url) else { return }
        let dec = JSONDecoder()
        dec.dateDecodingStrategy = .iso8601
        items = (try? dec.decode([HistoryItem].self, from: data)) ?? []
    }

    private func save() {
        let enc = JSONEncoder()
        enc.dateEncodingStrategy = .iso8601
        guard let data = try? enc.encode(items) else { return }
        try? data.write(to: url, options: [.atomic])
    }
}
