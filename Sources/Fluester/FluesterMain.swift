import AppKit

/// Flüster: Menüleisten-App ohne Dock-Symbol. Fenster (Einstellungen, Einrichtung) schalten das Dock-Symbol kurz ein.
@main
enum FluesterMain {
    @MainActor
    static func main() {
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
