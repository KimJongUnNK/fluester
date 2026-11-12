import AppKit
import CryptoKit
import FluesterCore

/// Aktualisieren aus der App: neue Version laden und prüfen (Prüfsumme, Signatur), Flüster beenden, die App genau dort
/// ersetzen, wo sie liegt (keine zweite Kopie), und neu öffnen. Das Ersetzen macht ein kleines Skript, das wartet,
/// bis Flüster beendet ist.
@MainActor
final class AppUpdater: ObservableObject {
    static let shared = AppUpdater()

    enum Phase: Equatable {
        case idle, checking, upToDate, available(String), downloading, installing, failed(String)
    }

    @Published private(set) var phase: Phase = .idle

    nonisolated static let asset = "Fluester-macOS.zip"
    static var currentVersion: String { Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0" }

    func check() {
        phase = .checking
        Task {
            do {
                let latest = try await Self.latest()
                phase = Updates.isNewer(latest, than: Self.currentVersion) ? .available(latest) : .upToDate
            } catch {
                phase = .failed("Konnte nicht prüfen: " + error.localizedDescription)
            }
        }
    }

    func install(_ version: String) {
        phase = .downloading
        Task {
            do {
                let (app, work) = try await Self.download(version)
                phase = .installing
                try Self.startReplace(with: app, work: work)
                try? await Task.sleep(nanoseconds: 300_000_000)
                NSApp.terminate(nil)
            } catch {
                phase = .failed("Aktualisieren fehlgeschlagen: " + error.localizedDescription)
            }
        }
    }

    // MARK: Laden und prüfen

    nonisolated static func latest() async throws -> String {
        var req = URLRequest(url: Updates.latestAPI, timeoutInterval: 10)
        req.setValue("Fluester-Updater", forHTTPHeaderField: "User-Agent")
        let (data, _) = try await URLSession.shared.data(for: req)
        guard let v = Updates.parseLatest(data) else { throw FluesterError("Keine Version auf GitHub gefunden") }
        return v
    }

    /// Lädt ZIP und Prüfsummen, vergleicht SHA-256, entpackt und prüft die Signatur. Liefert die neue App und den Arbeitsordner.
    nonisolated static func download(_ version: String) async throws -> (app: URL, work: URL) {
        let fm = FileManager.default
        let work = fm.temporaryDirectory.appendingPathComponent("fluester-update-\(UUID().uuidString)", isDirectory: true)
        try fm.createDirectory(at: work, withIntermediateDirectories: true)

        let (sumsData, sumsResp) = try await URLSession.shared.data(from: Updates.assetURL(version: version, asset: "SHA256SUMS.txt"))
        guard (sumsResp as? HTTPURLResponse)?.statusCode == 200, let sums = String(data: sumsData, encoding: .utf8),
              let expected = Updates.checksum(in: sums, for: asset) else {
            throw FluesterError("Keine Prüfsumme für \(asset) im Release")
        }
        let (tmp, resp) = try await URLSession.shared.download(from: Updates.assetURL(version: version, asset: asset))
        guard (resp as? HTTPURLResponse)?.statusCode == 200 else { throw FluesterError("Download fehlgeschlagen") }
        let zip = work.appendingPathComponent(asset)
        try fm.moveItem(at: tmp, to: zip)
        let actual = SHA256.hash(data: try Data(contentsOf: zip)).map { String(format: "%02x", $0) }.joined()
        guard actual == expected else { throw FluesterError("Download beschädigt (Prüfsumme passt nicht) – bitte erneut versuchen") }

        let out = work.appendingPathComponent("neu", isDirectory: true)
        try run("/usr/bin/ditto", ["-x", "-k", zip.path, out.path])
        guard let app = try fm.contentsOfDirectory(at: out, includingPropertiesForKeys: nil).first(where: { $0.pathExtension == "app" }) else {
            throw FluesterError("Im Paket ist keine App")
        }
        try run("/usr/bin/codesign", ["--verify", app.path])
        return (app, work)
    }

    /// Für Tests: lokales Paket entpacken, Signatur prüfen und das Ersetzen starten (ohne Download und Prüfsumme).
    static func applyLocal(zip: URL) throws {
        let fm = FileManager.default
        let work = fm.temporaryDirectory.appendingPathComponent("fluester-update-\(UUID().uuidString)", isDirectory: true)
        let out = work.appendingPathComponent("neu", isDirectory: true)
        try fm.createDirectory(at: work, withIntermediateDirectories: true)
        try run("/usr/bin/ditto", ["-x", "-k", zip.path, out.path])
        guard let app = try fm.contentsOfDirectory(at: out, includingPropertiesForKeys: nil).first(where: { $0.pathExtension == "app" }) else {
            throw FluesterError("Im Paket ist keine App")
        }
        try run("/usr/bin/codesign", ["--verify", app.path])
        try startReplace(with: app, work: work)
    }

    nonisolated private static func run(_ exe: String, _ args: [String]) throws {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: exe)
        p.arguments = args
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        try p.run()
        p.waitUntilExit()
        guard p.terminationStatus == 0 else { throw FluesterError("\((exe as NSString).lastPathComponent) fehlgeschlagen (\(p.terminationStatus))") }
    }

    // MARK: Ersetzen

    /// Wohin die neue Version kommt: an die Stelle dieser App. Läuft sie schreibgeschützt aus „Downloads“
    /// (App Translocation), nach /Applications.
    static var destination: URL {
        let here = Bundle.main.bundleURL
        return here.path.contains("/AppTranslocation/") ? URL(fileURLWithPath: "/Applications/Flüster.app") : here
    }

    /// Startet das Skript, das auf das Ende von Flüster wartet, die App ersetzt (alte Fassung bleibt bei einem Fehler)
    /// und sie neu öffnet. Ohne Schreibrecht im Ordner fragt macOS nach dem Admin-Passwort.
    static func startReplace(with newApp: URL, work: URL) throws {
        let dest = destination
        let admin = !FileManager.default.isWritableFile(atPath: dest.deletingLastPathComponent().path)
        let replace = work.appendingPathComponent("ersetzen.sh")
        try """
            #!/bin/bash
            # $1 = Ziel, $2 = neue App. Alte App erst beiseite, bei Fehler zurück.
            DEST="$1"; NEW="$2"
            rm -rf "$DEST.alt"
            if [ -e "$DEST" ]; then mv "$DEST" "$DEST.alt" || exit 1; fi
            if ditto "$NEW" "$DEST"; then rm -rf "$DEST.alt"; exit 0; fi
            rm -rf "$DEST"; [ -e "$DEST.alt" ] && mv "$DEST.alt" "$DEST"
            exit 1
            """.write(to: replace, atomically: true, encoding: .utf8)
        let main = work.appendingPathComponent("update.sh")
        try """
            #!/bin/bash
            PID="$1"; DEST="$2"; NEW="$3"; ID="$4"; LOG="$5"; ADMIN="$6"; WORK="$7"
            log() { echo "$(date '+%Y-%m-%d %H:%M:%S') Update: $*" >> "$LOG"; }
            # Warten, bis Flüster beendet ist
            for _ in $(seq 150); do kill -0 "$PID" 2>/dev/null || break; sleep 0.2; done
            kill -0 "$PID" 2>/dev/null && { kill "$PID"; sleep 1; }
            ersetzen() {
              if [ "$ADMIN" = 1 ]; then
                osascript -e 'on run argv' \\
                  -e 'do shell script "/bin/bash " & quoted form of (item 1 of argv) & " " & quoted form of (item 2 of argv) & " " & quoted form of (item 3 of argv) with administrator privileges' \\
                  -e 'end run' "$WORK/ersetzen.sh" "$DEST" "$NEW"
              else
                /bin/bash "$WORK/ersetzen.sh" "$DEST" "$NEW"
              fi
            }
            if ersetzen; then
              log "fertig: $DEST"
              xattr -dr com.apple.quarantine "$DEST" 2>/dev/null
              # Neue Signatur – die alte Bedienungshilfen-Freigabe passt nicht mehr, macOS soll sauber neu fragen
              tccutil reset Accessibility "$ID" >/dev/null 2>&1
            else
              log "fehlgeschlagen – alte Version bleibt"
            fi
            open "$DEST"
            rm -rf "$WORK"
            """.write(to: main, atomically: true, encoding: .utf8)

        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/bash")
        p.arguments = [main.path, String(getpid()), dest.path, newApp.path, Bundle.main.bundleIdentifier ?? "app.fluester.mac",
                       AppPaths.support.appendingPathComponent("update.log").path, admin ? "1" : "0", work.path]
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        try p.run()
        NSLog("Flüster: Update startet – ersetze %@%@", dest.path, admin ? " (mit Admin-Passwort)" : "")
    }
}
