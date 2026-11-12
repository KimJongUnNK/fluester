import Foundation

/// Update-Suche ohne AppKit: Version aus GitHub lesen, Versionen vergleichen, Prüfsumme aus SHA256SUMS.txt holen.
public enum Updates {
    public static let repo = "KimJongUnNK/fluester"
    public static let latestAPI = URL(string: "https://api.github.com/repos/\(repo)/releases/latest")!

    public static func assetURL(version: String, asset: String) -> URL {
        URL(string: "https://github.com/\(repo)/releases/download/v\(version)/\(asset)")!
    }

    /// Version aus der Antwort von /releases/latest („v1.3.2“ → „1.3.2“), sonst nil.
    public static func parseLatest(_ data: Data) -> String? {
        guard let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let tag = obj["tag_name"] as? String else { return nil }
        let v = tag.trimmingCharacters(in: .whitespaces).trimmingCharacters(in: CharacterSet(charactersIn: "vV"))
        return parts(v) != nil ? v : nil
    }

    /// Ist `latest` neuer als `current`? Unlesbare Versionen zählen nie als neuer.
    public static func isNewer(_ latest: String?, than current: String?) -> Bool {
        guard let l = latest.flatMap(parts), let c = current.flatMap(parts) else { return false }
        return c.lexicographicallyPrecedes(l)
    }

    /// „1.3“ → [1, 3, 0]; nil, wenn keine Version.
    static func parts(_ s: String) -> [Int]? {
        let t = s.trimmingCharacters(in: CharacterSet(charactersIn: "vV "))
        let comps = t.split(separator: ".", omittingEmptySubsequences: false)
        guard (2...4).contains(comps.count) else { return nil }
        var nums: [Int] = []
        for c in comps {
            guard let n = Int(c), n >= 0 else { return nil }
            nums.append(n)
        }
        while nums.count < 3 { nums.append(0) }
        return Array(nums.prefix(3))
    }

    /// Prüfsumme einer Datei aus SHA256SUMS.txt („hash  Dateiname“ bzw. „hash *Dateiname“).
    public static func checksum(in sums: String, for asset: String) -> String? {
        for raw in sums.split(whereSeparator: \.isNewline) {
            let line = raw.trimmingCharacters(in: .whitespaces)
            guard let sp = line.firstIndex(of: " "), line.distance(from: line.startIndex, to: sp) == 64 else { continue }
            var name = line[sp...].trimmingCharacters(in: .whitespaces)
            if name.hasPrefix("*") { name.removeFirst() }
            if name == asset { return String(line[..<sp]).lowercased() }
        }
        return nil
    }
}
