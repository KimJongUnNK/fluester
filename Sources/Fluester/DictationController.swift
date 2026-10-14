import AppKit
import FluesterCore

/// Ablauf: Taste → Aufnahme → Erkennung → Bereinigung → Einfügen → Verlauf.
@MainActor
final class DictationController {
    enum State: Equatable { case idle, recording, processing }

    let store: SettingsStore
    let history: HistoryStore
    let bar: FlowBarController

    private let recorder = AudioRecorder()
    private let hotkey = HotkeyMonitor()
    private let gestures = HotkeyGestures()

    private(set) var state: State = .idle { didSet { onStateChange?() } }
    private(set) var lastError: String?
    var onStateChange: (() -> Void)?
    var onNeedsPermissions: (() -> Void)?

    private var isCommand = false
    private var target = TargetApp(name: "", bundleID: "", pid: 0, windowTitle: nil)
    private var selection: String?
    private var ticker: Timer?
    private var work: Task<Void, Never>?
    private var hotkeyRetry: Timer?

    /// Maximale Länge eines Diktats
    private let maxSeconds: Double = 600

    init(store: SettingsStore, history: HistoryStore, bar: FlowBarController) {
        self.store = store
        self.history = history
        self.bar = bar
    }

    var hotkeyActive: Bool { hotkey.isRunning }

    // MARK: Start

    func start() {
        recorder.onLevel = { [weak self] level in
            MainActor.assumeIsolated { self?.bar.model.push(level) }
        }
        hotkey.onPress = { [weak self] command in
            MainActor.assumeIsolated { self?.keyDown(command: command) }
        }
        hotkey.onRelease = { [weak self] in
            MainActor.assumeIsolated { self?.keyUp() }
        }
        hotkey.onCommandModifier = { [weak self] in
            MainActor.assumeIsolated { self?.upgradeToCommand() }
        }
        hotkey.onOtherKey = { [weak self] in
            MainActor.assumeIsolated { self?.gestures.otherKeyPressed() }
        }
        hotkey.onEscape = { [weak self] in
            MainActor.assumeIsolated { self?.escape() ?? false }
        }
        applySettings()
        startHotkey()
    }

    func applySettings() {
        let s = store.s
        hotkey.choice = s.hotkey
        gestures.doubleTapEnabled = s.handsFreeDoubleTap
        bar.showWhenIdle = s.showIdleBar
        if hotkey.isRunning { hotkey.start() }
        prewarm()
    }

    /// Hotkey braucht die Bedienungshilfen-Freigabe; bis dahin regelmäßig erneut versuchen.
    func startHotkey() {
        hotkeyRetry?.invalidate()
        if hotkey.start() {
            onStateChange?()
            return
        }
        hotkeyRetry = Timer.scheduledTimer(withTimeInterval: 1.5, repeats: true) { [weak self] t in
            MainActor.assumeIsolated {
                guard let self = self else { t.invalidate(); return }
                if Permissions.accessibility, self.hotkey.start() {
                    t.invalidate()
                    self.onStateChange?()
                }
            }
        }
    }

    /// Modelle vorab laden, damit das erste Diktat schnell ist.
    func prewarm() {
        let s = store.s
        if s.engine == .whisperLocal, WhisperServer.binaryPath() != nil, WhisperServer.isInstalled(WhisperModel.byID(s.whisperModelID)) {
            Task.detached { _ = try? await WhisperServer.shared.ensureRunning(model: WhisperModel.byID(s.whisperModelID), language: s.language) }
        } else {
            Task.detached { await WhisperServer.shared.stop() }
        }
        if s.engine == .apple {
            if #available(macOS 26.0, *) {
                Task.detached { try? await AppleSpeechEngine.prepare(language: s.language) }
            }
        }
    }

    // MARK: Taste

    private func keyDown(command: Bool) {
        let now = ProcessInfo.processInfo.systemUptime
        handle(gestures.press(at: now, command: command && store.s.commandModeEnabled))
    }

    private func keyUp() {
        handle(gestures.release(at: ProcessInfo.processInfo.systemUptime))
    }

    private func upgradeToCommand() {
        guard state == .recording, !isCommand, store.s.commandModeEnabled, recorder.elapsed < 1.5 else { return }
        isCommand = true
        selection = TextInserter.selectedTextAX()
        bar.set(.listening(handsFree: gestures.state == .handsFree, command: true))
    }

    private func handle(_ action: HotkeyGestures.Action) {
        switch action {
        case .startHold(let command):
            beginRecording(command: command)
        case .stopAndProcess:
            finishRecording()
        case .cancel:
            cancel(silent: true)
        case .switchToHandsFree:
            bar.set(.listening(handsFree: true, command: isCommand))
        case .none:
            break
        }
    }

    /// Esc: Aufnahme oder Verarbeitung abbrechen.
    private func escape() -> Bool {
        switch state {
        case .recording:
            cancel(silent: false)
            return true
        case .processing:
            work?.cancel()
            work = nil
            state = .idle
            bar.set(.error("Abgebrochen"), autoHide: 1.2)
            return true
        case .idle:
            return false
        }
    }

    // MARK: Menü-Aktionen

    /// Für Menüleiste/Kurzbefehl: Diktat freihändig starten bzw. beenden.
    func toggleFromMenu() {
        switch state {
        case .idle:
            beginRecording(command: false)
            if state == .recording {
                _ = gestures.press(at: 0, command: false)
                _ = gestures.release(at: 0.05)
                _ = gestures.press(at: 0.1, command: false) // → freihändig
                _ = gestures.release(at: 0.15)
                bar.set(.listening(handsFree: true, command: false))
            }
        case .recording:
            gestures.reset()
            finishRecording()
        case .processing:
            break
        }
    }

    func pasteLast() {
        guard let last = history.last else { return }
        TextInserter.insert(last.text, method: store.s.insertMethod, smartSpacing: store.s.smartSpacing)
    }

    // MARK: Aufnahme

    private func beginRecording(command: Bool) {
        if state == .processing {
            Sounds.error()
            gestures.reset()
            return
        }
        if state == .recording { _ = recorder.stop() }
        guard Permissions.microphoneGranted else {
            gestures.reset()
            if Permissions.microphone == .notDetermined {
                Permissions.requestMicrophone { _ in }
            } else {
                bar.set(.error("Kein Mikrofon-Zugriff – Einstellungen › Berechtigungen"), autoHide: 3)
                onNeedsPermissions?()
            }
            return
        }
        isCommand = command
        target = TargetApp.current()
        selection = command ? TextInserter.selectedTextAX() : nil
        do {
            try recorder.start(deviceUID: store.s.microphoneUID)
        } catch {
            gestures.reset()
            fail(error.localizedDescription)
            return
        }
        Sounds.start()
        state = .recording
        bar.model.seconds = 0
        bar.set(.listening(handsFree: false, command: command))
        ticker?.invalidate()
        ticker = Timer.scheduledTimer(withTimeInterval: 0.05, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
    }

    private func tick() {
        guard state == .recording else { ticker?.invalidate(); return }
        let action = gestures.tick(at: ProcessInfo.processInfo.systemUptime)
        if action != .none { handle(action); return }
        bar.model.seconds = Int(recorder.elapsed)
        if recorder.elapsed > maxSeconds {
            gestures.reset()
            finishRecording()
        }
    }

    private func cancel(silent: Bool) {
        ticker?.invalidate()
        gestures.reset()
        if state == .recording { _ = recorder.stop() }
        state = .idle
        if silent { bar.idle() } else { bar.set(.error("Abgebrochen"), autoHide: 1.0) }
    }

    private func finishRecording() {
        ticker?.invalidate()
        guard state == .recording else { return }
        let (samples, seconds) = recorder.stop()
        Sounds.stop()
        guard seconds >= 0.3, AudioUtil.hasSpeech(samples, sampleRate: Int(AudioRecorder.sampleRate)) else {
            state = .idle
            if seconds >= 0.8 { bar.set(.error("Nichts gehört – Mikrofon prüfen?"), autoHide: 1.8) } else { bar.idle() }
            return
        }
        state = .processing
        bar.set(.processing(command: isCommand))
        let command = isCommand
        let target = self.target
        let selection = self.selection
        work = Task { [weak self] in
            await self?.process(samples: samples, seconds: seconds, command: command, target: target, selection: selection)
        }
    }

    // MARK: Verarbeitung

    private func process(samples: [Float], seconds: Double, command: Bool, target: TargetApp, selection: String?) async {
        let s = store.s
        let engine = SpeechEngines.make(s)
        let hints = Vocabulary.hints(s.dictionary, snippets: s.snippets)
        let category = AppCategorizer.category(bundleID: target.bundleID, windowTitle: target.windowTitle, overrides: s.styles.overrides)
        let style = s.styles.style(for: category)
        let audio = AudioUtil.trimSilence(samples, sampleRate: Int(AudioRecorder.sampleRate))

        do {
            var raw = try await engine.transcribe(audio, sampleRate: AudioRecorder.sampleRate, language: s.language, hints: hints)
            raw = TextPipeline.stripHallucinations(raw)
            if Task.isCancelled { return }
            guard !raw.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                finishWith(error: "Nichts verstanden")
                return
            }

            let output: String
            if command {
                output = try await runCommand(instruction: raw, selection: selection, target: target, settings: s)
            } else {
                let result = await Self.cleanText(raw, style: style, appName: target.name, settings: s)
                output = result.text
                lastError = result.warning
            }
            if Task.isCancelled { return }
            guard !output.isEmpty else {
                finishWith(error: "Nichts verstanden")
                return
            }

            let inField = TextInserter.insert(output, method: s.insertMethod, smartSpacing: s.smartSpacing && !command)
            if s.keepHistory {
                history.add(HistoryItem(raw: raw, text: output, appName: target.name, bundleID: target.bundleID,
                                        seconds: seconds, isCommand: command, engine: engine.name))
            }
            state = .idle
            if inField {
                bar.set(.done(command ? "Fertig" : "\(Stats.wordCount(output)) Wörter"), autoHide: 0.9)
            } else {
                bar.set(.done("In der Zwischenablage – ⌘V"), autoHide: 2.2)
            }
        } catch is CancellationError {
            return
        } catch {
            finishWith(error: error.localizedDescription)
        }
    }

    /// Erkennung → Snippets → Regeln → (Sprachmodell) → Regeln. Liefert Text und ggf. Warnung (KI ausgefallen).
    nonisolated static func cleanText(_ raw: String, style: WritingStyle, appName: String, settings s: Settings) async -> (text: String, warning: String?) {
        let options = CleanupOptions(s, style: style)
        let snip = Vocabulary.expandSnippets(raw, s.snippets)
        if snip.matchedWhole { return (snip.text, nil) }
        let pre = TextPipeline.preprocess(snip.text, options: options)
        var cleaned: String
        var warning: String? = nil
        if let rewriter = Rewriters.cleanup(s), Stats.wordCount(pre) >= 3 {
            let system = Prompts.cleanupSystem(style: style, appName: appName, languageName: s.languageName, dictionary: s.dictionary,
                                               selfCorrections: s.applySelfCorrections, formatLists: s.formatLists)
            do {
                let out = try await withTimeout(12) { try await rewriter.complete(system: system, user: Prompts.cleanupUser(pre)) }
                if let ok = Prompts.sanitize(out, input: pre) {
                    cleaned = ok
                } else {
                    cleaned = TextPipeline.ruleBased(pre, options: options, dictionary: s.dictionary)
                    warning = "KI-Antwort verworfen – Regeln verwendet"
                }
            } catch {
                warning = "KI-Bereinigung: \(error.localizedDescription) – Regeln verwendet"
                cleaned = TextPipeline.ruleBased(pre, options: options, dictionary: s.dictionary)
            }
        } else {
            cleaned = TextPipeline.ruleBased(pre, options: options, dictionary: s.dictionary)
        }
        return (TextPipeline.finalize(cleaned, options: options, dictionary: s.dictionary), warning)
    }

    /// Command Mode: Markierung + gesprochene Anweisung → neuer Text.
    private func runCommand(instruction: String, selection: String?, target: TargetApp, settings s: Settings) async throws -> String {
        guard let rewriter = Rewriters.command(s) else {
            throw FluesterError("Command Mode braucht Apple Intelligence oder ein Sprachmodell (Einstellungen › KI)")
        }
        var sel = selection
        if sel == nil { sel = await TextInserter.copySelection() }
        let system = Prompts.commandSystem(appName: target.name, languageName: s.languageName)
        let out = try await withTimeout(30) {
            try await rewriter.complete(system: system, user: Prompts.commandUser(instruction: instruction, selection: sel))
        }
        guard let clean = Prompts.sanitize(out, input: instruction, isCommand: true) else {
            throw FluesterError("Das Modell hat nichts Brauchbares geliefert")
        }
        return clean
    }

    private func finishWith(error message: String) {
        state = .idle
        lastError = message
        Sounds.error()
        bar.set(.error(message), autoHide: 3.5)
    }

    private func fail(_ message: String) {
        state = .idle
        lastError = message
        Sounds.error()
        bar.set(.error(message), autoHide: 3.5)
    }
}
