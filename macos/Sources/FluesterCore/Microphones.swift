import Foundation

/// Gewähltes Mikrofon wiederfinden, auch wenn sich seine UID geändert hat. macOS baut die UID von USB-Mikrofonen ohne
/// Seriennummer aus dem Anschluss (Location-ID): anderer Port oder Hub → neue UID. Reihenfolge: genaue UID, UID ohne
/// Anschluss, gleicher Name. Passen zwei, gilt keins – lieber der Systemstandard als das falsche von zwei gleichen Mikros.
public enum MicrophoneMatch {
    public struct Candidate: Equatable, Sendable {
        public let uid: String
        public let name: String
        public init(uid: String, name: String) { self.uid = uid; self.name = name }
    }

    /// Index des gewählten Mikrofons in `devices`, oder nil (dann gilt der Systemstandard).
    public static func find(_ devices: [Candidate], uid: String, name: String) -> Int? {
        guard !uid.isEmpty else { return nil }
        if let exact = devices.firstIndex(where: { $0.uid == uid }) { return exact }
        let stable = stableUID(uid)
        if stable != uid, let same = only(devices.indices.filter { stableUID(devices[$0].uid) == stable }) { return same }
        let wanted = normalizedName(name)
        guard !wanted.isEmpty else { return nil }
        return only(devices.indices.filter { normalizedName(devices[$0].name) == wanted })
    }

    private static func only(_ hits: [Int]) -> Int? { hits.count == 1 ? hits[0] : nil }

    /// UID ohne Anschluss: „AppleUSBAudioEngine:Hersteller:Produkt:14200000:1“ → „AppleUSBAudioEngine:Hersteller:Produkt:1“.
    /// Das vorletzte Feld ist die Seriennummer oder, wenn das Gerät keine hat, die Location-ID des USB-Ports.
    public static func stableUID(_ uid: String) -> String {
        var parts = uid.split(separator: ":", omittingEmptySubsequences: false)
        guard parts.first == "AppleUSBAudioEngine", parts.count >= 5 else { return uid }
        parts.remove(at: parts.count - 2)
        return parts.joined(separator: ":")
    }

    /// Name zum Vergleichen: ohne Groß-/Kleinschreibung und doppelte Leerzeichen.
    public static func normalizedName(_ name: String) -> String {
        name.split(whereSeparator: \.isWhitespace).joined(separator: " ").lowercased()
    }
}
