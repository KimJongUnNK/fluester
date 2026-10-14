import AppKit
import AVFoundation
import ApplicationServices
import ServiceManagement
import Security
import Speech
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

/// Wartet höchstens `seconds` auf eine Operation. Kehrt auch dann zurück, wenn die Operation
/// Abbrechen ignoriert (z. B. ein Callback, der nie kommt) – eine Task-Group würde darauf warten.
func withTimeout<T: Sendable>(_ seconds: Double, _ operation: @escaping @Sendable () async throws -> T) async throws -> T {
    let gate = OnceGate<T>()
    return try await withTaskCancellationHandler {
        try await withCheckedThrowingContinuation { (cont: CheckedContinuation<T, Error>) in
            gate.arm(cont)
            let work = Task {
                do { gate.resume(.success(try await operation())) } catch { gate.resume(.failure(error)) }
            }
            let timer = Task {
                try? await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
                gate.resume(.failure(FluesterError("Zeitüberschreitung nach \(Int(seconds)) s")))
            }
            gate.attach(work, timer)
        }
    } onCancel: {
        gate.resume(.failure(CancellationError()))
    }
}

/// Setzt eine Continuation genau einmal fort und räumt die beteiligten Tasks ab.
private final class OnceGate<T: Sendable>: @unchecked Sendable {
    private let lock = NSLock()
    private var cont: CheckedContinuation<T, Error>?
    private var done = false
    private var pending: Result<T, Error>?
    private var tasks: [Task<Void, Never>] = []

    func arm(_ c: CheckedContinuation<T, Error>) {
        lock.lock()
        if let r = pending {
            pending = nil
            lock.unlock()
            c.resume(with: r)
            return
        }
        cont = c
        lock.unlock()
    }

    func attach(_ work: Task<Void, Never>, _ timer: Task<Void, Never>) {
        lock.lock()
        if done {
            lock.unlock()
            work.cancel()
            timer.cancel()
            return
        }
        tasks = [work, timer]
        lock.unlock()
    }

    func resume(_ result: Result<T, Error>) {
        lock.lock()
        guard !done else { lock.unlock(); return }
        done = true
        let c = cont
        cont = nil
        if c == nil { pending = result }   // Abbruch kam vor arm()
        let running = tasks
        tasks = []
        lock.unlock()
        c?.resume(with: result)
        running.forEach { $0.cancel() }
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

    static var speechRecognitionGranted: Bool { SFSpeechRecognizer.authorizationStatus() == .authorized }
    static var speechRecognitionUndetermined: Bool { SFSpeechRecognizer.authorizationStatus() == .notDetermined }
    static func requestSpeechRecognition() { SFSpeechRecognizer.requestAuthorization { _ in } }

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
