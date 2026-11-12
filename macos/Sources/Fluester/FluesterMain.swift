import AppKit

/// Flüster: Menüleisten-App ohne Dock-Symbol. Fenster (Einstellungen, Einrichtung) schalten das Dock-Symbol kurz ein.
@main
enum FluesterMain {
    @MainActor
    static func main() {
        // Für Tests: Paket an Ort und Stelle einspielen (wie „Jetzt aktualisieren“, nur ohne Download)
        let args = CommandLine.arguments
        if let i = args.firstIndex(of: "--apply-update"), i + 1 < args.count {
            do {
                try AppUpdater.applyLocal(zip: URL(fileURLWithPath: args[i + 1]))
                exit(0)
            } catch {
                FileHandle.standardError.write(Data("Update: \(error.localizedDescription)\n".utf8))
                exit(1)
            }
        }
        if CommandLine.arguments.contains("--selftest") {
            SelfTest.start()
            return
        }
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory)
        app.run()
        _ = delegate // NSApplication hält den Delegate nur schwach
    }
}
