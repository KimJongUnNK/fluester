import AppKit
import AVFoundation
import ApplicationServices
import ServiceManagement
import Security
import FluesterCore

/// Ordner in ~/Library/Application Support/Fluester
enum AppPaths {
    static var support: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? URL(fileURLWithPath: NSHomeDirectory()).appendingPathComponent("Library/Application Support")
        let dir = base.appendingPathComponent("Fluester", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }
    static var models: URL {
        let dir = support.appendingPathComponent("models", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }
}

/// Arbeit auf dem Main-Thread, optional verzögert.
func onMain(after seconds: Double = 0, _ work: @escaping @MainActor () -> Void) {
    DispatchQueue.main.asyncAfter(deadline: .now() + seconds) {
        MainActor.assumeIsolated { work() }
    }
}

/// Wartet höchstens `seconds` auf eine Operation.
func withTimeout<T: Sendable>(_ seconds: Double, _ operation: @escaping @Sendable () async throws -> T) async throws -> T {
    try await withThrowingTaskGroup(of: T.self) { group in
        group.addTask { try await operation() }
        group.addTask {
            try await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
            throw FluesterError("Zeitüberschreitung nach \(Int(seconds)) s")
        }
        guard let first = try await group.next() else { throw FluesterError("Keine Antwort") }
        group.cancelAll()
        return first
    }
}

// MARK: - Schlüsselbund für API-Schlüssel

enum Keychain {
    static let service = "app.fluester.api-keys"

    static func get(_ account: String) -> String? {
        let q: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne
        ]
        var out: AnyObject?
        guard SecItemCopyMatching(q as CFDictionary, &out) == errSecSuccess, let data = out as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    static func set(_ account: String, _ value: String?) {
        let base: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account
        ]
        SecItemDelete(base as CFDictionary)
        guard let v = value?.trimmingCharacters(in: .whitespacesAndNewlines), !v.isEmpty else { return }
        var add = base
        add[kSecValueData as String] = Data(v.utf8)
        add[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
        SecItemAdd(add as CFDictionary, nil)
    }

    /// Schlüssel je Anbieter (groq, openai, custom …)
    static func apiKey(for providerID: String) -> String? { get("key." + providerID) }
    static func setAPIKey(_ key: String?, for providerID: String) { set("key." + providerID, key) }
}

// MARK: - Berechtigungen

enum Permissions {
    static var microphone: AVAuthorizationStatus { AVCaptureDevice.authorizationStatus(for: .audio) }
    static var microphoneGranted: Bool { microphone == .authorized }

    static func requestMicrophone(_ done: @escaping (Bool) -> Void) {
        AVCaptureDevice.requestAccess(for: .audio) { ok in
            DispatchQueue.main.async { done(ok) }
        }
    }

    static var accessibility: Bool { AXIsProcessTrusted() }

    static func promptAccessibility() {
        let key = kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String
        _ = AXIsProcessTrustedWithOptions([key: true] as CFDictionary)
    }

    static func openPrivacy(_ anchor: String) {
        let urls = [
            "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?\(anchor)",
            "x-apple.systempreferences:com.apple.preference.security?\(anchor)"
        ]
        for s in urls {
            if let u = URL(string: s), NSWorkspace.shared.open(u) { return }
        }
    }

    static func openKeyboardSettings() {
        for s in ["x-apple.systempreferences:com.apple.Keyboard-Settings.extension", "x-apple.systempreferences:com.apple.preference.keyboard"] {
            if let u = URL(string: s), NSWorkspace.shared.open(u) { return }
        }
    }
}

// MARK: - Töne

enum Sounds {
    static func play(_ name: String, volume: Float = 0.25) {
        guard SettingsStore.shared.s.sounds, let sound = NSSound(named: NSSound.Name(name)) else { return }
        sound.stop()
        sound.volume = volume
        sound.play()
    }
    static func start() { play("Pop", volume: 0.18) }
    static func stop() { play("Tink", volume: 0.15) }
    static func error() { play("Basso", volume: 0.2) }
}

// MARK: - Bei Anmeldung starten

enum LoginItem {
    static var isEnabled: Bool { SMAppService.mainApp.status == .enabled }

    @discardableResult
    static func set(_ on: Bool) -> String? {
        do {
            if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            return nil
        } catch {
            return error.localizedDescription
        }
    }
}

// MARK: - Ziel-App (wohin der Text geht)

struct TargetApp {
    var name: String
    var bundleID: String
    var pid: pid_t
    var windowTitle: String?

    static func current() -> TargetApp {
        let app = NSWorkspace.shared.frontmostApplication
        let pid = app?.processIdentifier ?? 0
        var title: String? = nil
        if pid > 0 {
            let axApp = AXUIElementCreateApplication(pid)
            var win: CFTypeRef?
            if AXUIElementCopyAttributeValue(axApp, kAXFocusedWindowAttribute as CFString, &win) == .success, let w = win {
                var t: CFTypeRef?
                // swiftlint:disable:next force_cast
                if AXUIElementCopyAttributeValue(w as! AXUIElement, kAXTitleAttribute as CFString, &t) == .success { title = t as? String }
            }
        }
        return TargetApp(name: app?.localizedName ?? "", bundleID: app?.bundleIdentifier ?? "", pid: pid, windowTitle: title)
    }
}
