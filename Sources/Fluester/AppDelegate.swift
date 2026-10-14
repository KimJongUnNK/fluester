import AppKit
import SwiftUI
import Combine
import FluesterCore

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate, NSWindowDelegate {
    let store = SettingsStore.shared
    let history = HistoryStore()
    private(set) var bar: FlowBarController!
    private(set) var controller: DictationController!

    private var statusItem: NSStatusItem!
    private var settingsWindow: NSWindow?
    private var onboardingWindow: NSWindow?
    private var cancellables = Set<AnyCancellable>()
    let settingsTab = SettingsTabState()

    func applicationDidFinishLaunching(_ notification: Notification) {
        bar = FlowBarController()
        controller = DictationController(store: store, history: history, bar: bar)
        setupStatusItem()
        controller.onStateChange = { [weak self] in self?.refreshStatusIcon() }
        controller.onNeedsPermissions = { [weak self] in self?.showSettings(tab: .permissions) }
        controller.start()

        // Einstellungen live übernehmen (gebündelt, damit Tippen in Feldern nicht ständig neu startet)
        store.$s
            .dropFirst()
            .debounce(for: .milliseconds(400), scheduler: RunLoop.main)
            .removeDuplicates { $0.hotkey == $1.hotkey && $0.engine == $1.engine && $0.whisperModelID == $1.whisperModelID
                && $0.language == $1.language && $0.handsFreeDoubleTap == $1.handsFreeDoubleTap && $0.showIdleBar == $1.showIdleBar }
            .sink { [weak self] _ in
                MainActor.assumeIsolated { self?.controller.applySettings() }
            }
            .store(in: &cancellables)

        if !store.s.onboardingDone || !Permissions.accessibility || !Permissions.microphoneGranted {
            showOnboarding()
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        WhisperServer.killNow()
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showSettings(tab: .general)
        return true
    }

    // MARK: Menüleiste

    private func setupStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        refreshStatusIcon()
        let menu = NSMenu()
        menu.delegate = self
        statusItem.menu = menu
    }

    func refreshStatusIcon() {
        guard let button = statusItem?.button else { return }
        let name: String
        switch controller?.state ?? .idle {
        case .idle: name = controller?.hotkeyActive == false ? "waveform.badge.exclamationmark" : "waveform"
        case .recording: name = "waveform.circle.fill"
        case .processing: name = "ellipsis.circle"
        }
        let img = NSImage(systemSymbolName: name, accessibilityDescription: "Flüster")
            ?? NSImage(systemSymbolName: "waveform", accessibilityDescription: "Flüster")
        img?.isTemplate = true
        button.image = img
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let s = store.s
        let stats = history.summary

        let head = NSMenuItem(title: statusLine(), action: nil, keyEquivalent: "")
        head.isEnabled = false
        menu.addItem(head)
        if let err = controller.lastError {
            let e = NSMenuItem(title: "⚠︎ " + err, action: nil, keyEquivalent: "")
            e.isEnabled = false
            menu.addItem(e)
        }
        menu.addItem(.separator())

        let toggleTitle = controller.state == .recording ? "Diktat beenden" : "Freihändig diktieren"
        menu.addItem(item(toggleTitle, #selector(toggleDictation), key: ""))
        let pasteLast = item("Letztes Diktat erneut einfügen", #selector(pasteLastAction), key: "")
        pasteLast.isEnabled = history.last != nil
        menu.addItem(pasteLast)

        // Verlauf
        let histItem = NSMenuItem(title: "Verlauf", action: nil, keyEquivalent: "")
        let sub = NSMenu()
        if history.items.isEmpty {
            let none = NSMenuItem(title: "Noch keine Diktate", action: nil, keyEquivalent: "")
            none.isEnabled = false
            sub.addItem(none)
        }
        for h in history.items.prefix(12) {
            let preview = h.text.replacingOccurrences(of: "\n", with: " ")
            let title = preview.count > 60 ? String(preview.prefix(60)) + "…" : preview
            let mi = NSMenuItem(title: title, action: #selector(copyHistoryItem(_:)), keyEquivalent: "")
            mi.target = self
            mi.representedObject = h.text
            mi.toolTip = "Klicken zum Kopieren · " + (h.appName.isEmpty ? "" : h.appName + " · ") + h.date.formatted(date: .abbreviated, time: .shortened)
            sub.addItem(mi)
        }
        sub.addItem(.separator())
        sub.addItem(item("Ganzen Verlauf zeigen …", #selector(openHistory), key: ""))
        histItem.submenu = sub
        menu.addItem(histItem)

        let statsItem = NSMenuItem(title: "\(stats.weekWords) Wörter diese Woche · \(stats.wpm) WPM · \(stats.minutesSaved) min gespart",
                                   action: nil, keyEquivalent: "")
        statsItem.isEnabled = false
        menu.addItem(statsItem)
        menu.addItem(.separator())

        // Schnellwahl Erkennung
        let engineItem = NSMenuItem(title: "Erkennung: " + s.engine.label, action: nil, keyEquivalent: "")
        let engineMenu = NSMenu()
        for e in EngineChoice.allCases {
            let mi = NSMenuItem(title: e.label, action: #selector(pickEngine(_:)), keyEquivalent: "")
            mi.target = self
            mi.representedObject = e.rawValue
            mi.state = e == s.engine ? .on : .off
            engineMenu.addItem(mi)
        }
        engineItem.submenu = engineMenu
        menu.addItem(engineItem)
        let cleanupItem = NSMenuItem(title: "KI: " + s.cleanup.label, action: nil, keyEquivalent: "")
        let cleanupMenu = NSMenu()
        for c in CleanupChoice.allCases {
            let mi = NSMenuItem(title: c.label, action: #selector(pickCleanup(_:)), keyEquivalent: "")
            mi.target = self
            mi.representedObject = c.rawValue
            mi.state = c == s.cleanup ? .on : .off
            cleanupMenu.addItem(mi)
        }
        cleanupItem.submenu = cleanupMenu
        menu.addItem(cleanupItem)
        menu.addItem(.separator())

        menu.addItem(item("Einstellungen …", #selector(openSettings), key: ","))
        menu.addItem(item("Einrichtung & Berechtigungen …", #selector(openOnboarding), key: ""))
        menu.addItem(.separator())
        menu.addItem(item("Flüster beenden", #selector(quit), key: "q"))
    }

    private func statusLine() -> String {
        if !controller.hotkeyActive { return "Bedienungshilfen fehlen – Taste inaktiv" }
        if !Permissions.microphoneGranted { return "Mikrofon nicht freigegeben" }
        switch controller.state {
        case .idle: return "Bereit – \(store.s.hotkey.label) halten zum Diktieren"
        case .recording: return "Hört zu …"
        case .processing: return "Schreibt …"
        }
    }

    private func item(_ title: String, _ action: Selector, key: String) -> NSMenuItem {
        let mi = NSMenuItem(title: title, action: action, keyEquivalent: key)
        mi.target = self
        return mi
    }

    @objc private func toggleDictation() { controller.toggleFromMenu() }
    @objc private func pasteLastAction() {
        // Menü schließt sich, Fokus geht zurück an die App – kurz warten
        onMain(after: 0.25) { [weak self] in self?.controller.pasteLast() }
    }
    @objc private func copyHistoryItem(_ sender: NSMenuItem) {
        guard let text = sender.representedObject as? String else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(text, forType: .string)
    }
    @objc private func pickEngine(_ sender: NSMenuItem) {
        if let raw = sender.representedObject as? String, let e = EngineChoice(rawValue: raw) { store.s.engine = e }
    }
    @objc private func pickCleanup(_ sender: NSMenuItem) {
        if let raw = sender.representedObject as? String, let c = CleanupChoice(rawValue: raw) { store.s.cleanup = c }
    }
    @objc private func openSettings() { showSettings(tab: .general) }
    @objc private func openHistory() { showSettings(tab: .history) }
    @objc private func openOnboarding() { showOnboarding() }
    @objc private func quit() { NSApp.terminate(nil) }

    // MARK: Fenster

    func showSettings(tab: SettingsTab) {
        settingsTab.tab = tab
        if settingsWindow == nil {
            let view = SettingsView(store: store, history: history, tabState: settingsTab, delegate: self)
            let host = NSHostingController(rootView: view)
            let w = NSWindow(contentViewController: host)
            w.title = "Flüster"
            w.styleMask = [.titled, .closable, .miniaturizable, .resizable]
            w.setContentSize(NSSize(width: 760, height: 560))
            w.isReleasedWhenClosed = false
            w.delegate = self
            w.center()
            settingsWindow = w
        }
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
        settingsWindow?.makeKeyAndOrderFront(nil)
    }

    func showOnboarding() {
        if onboardingWindow == nil {
            let view = OnboardingView(store: store, delegate: self)
            let host = NSHostingController(rootView: view)
            let w = NSWindow(contentViewController: host)
            w.title = "Willkommen bei Flüster"
            w.styleMask = [.titled, .closable, .fullSizeContentView]
            w.titlebarAppearsTransparent = true
            w.isReleasedWhenClosed = false
            w.delegate = self
            w.center()
            onboardingWindow = w
        }
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
        onboardingWindow?.makeKeyAndOrderFront(nil)
    }

    func closeOnboarding() {
        onboardingWindow?.close()
    }

    func windowWillClose(_ notification: Notification) {
        // Ohne offene Fenster wieder reine Menüleisten-App (kein Dock-Symbol)
        onMain(after: 0.1) { [weak self] in
            guard let self = self else { return }
            let visible = [self.settingsWindow, self.onboardingWindow].contains { $0?.isVisible == true }
            if !visible { NSApp.setActivationPolicy(.accessory) }
            self.controller.startHotkey()
        }
    }

    // Für die Einstellungen
    func restartHotkey() { controller.startHotkey() }
    var hotkeyActive: Bool { controller.hotkeyActive }
}
