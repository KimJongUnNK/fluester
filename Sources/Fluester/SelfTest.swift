import AppKit
import AVFoundation
import FluesterCore

/// `Fluester --selftest`: prüft ohne Mikrofon und ohne Klicks, ob Erkennung, Bereinigung und Oberfläche wirklich laufen.
/// Sprache kommt von `say`. Für die CI und zur Fehlersuche auf dem eigenen Mac.
enum SelfTest {
    @MainActor
    static func start() {
        let app = NSApplication.shared
        app.setActivationPolicy(.prohibited)
        Task { @MainActor in
            let ok = await runAll()
            log(ok ? "\n✅ SELBSTTEST OK" : "\n❌ SELBSTTEST FEHLGESCHLAGEN")
            exit(ok ? 0 : 1)
        }
        app.run()
    }

    static func log(_ s: String) {
        print(s)
        fflush(stdout)
    }

    @MainActor
    static func runAll() async -> Bool {
        var ok = true
        let env = ProcessInfo.processInfo.environment
        log("Flüster-Selbsttest · macOS \(ProcessInfo.processInfo.operatingSystemVersionString)")

        // 1) Bereinigung mit Regeln über den echten App-Pfad
        var rules = Settings()
        rules.cleanup = .rules
        let cleaned = await DictationController.cleanText("äh also wir treffen uns um 3, ich meine um 4 Uhr", style: .formal, appName: "Test", settings: rules)
        log("Regeln: „\(cleaned.text)“")
        if cleaned.text != "Also wir treffen uns um 4 Uhr." { ok = false; log("  ✗ unerwartet") } else { log("  ✓") }

        // 2) Oberfläche: Leiste, Wellenform
        let bar = FlowBarController()
        bar.set(.listening(handsFree: false, command: false))
        for i in 0..<30 { bar.model.push(Float(i % 10) / 10) }
        bar.set(.processing(command: false))
        bar.set(.done("5 Wörter"))
        bar.idle()
        log("Flow-Leiste: ✓")

        // 3) Geräte und Berechtigungen (in der CI meist leer/aus)
        log("Mikrofone: \(AudioDevices.inputs().map(\.name))")
        log("Bedienungshilfen: \(Permissions.accessibility ? "erlaubt" : "nicht erlaubt")")
        log("Apple Intelligence: \(Rewriters.appleIntelligenceProblem() ?? "verfügbar")")

        // 4) Sprache erzeugen
        let sentence = "Hello, this is a test of the dictation app."
        guard let samples = speech(sentence) else {
            log("✗ say konnte keine Audiodatei erzeugen")
            return false
        }
        log(String(format: "Audio: %.1f s, Sprache erkannt: %@", Double(samples.count) / AudioRecorder.sampleRate,
                   AudioUtil.hasSpeech(samples, sampleRate: 16000) ? "ja" : "nein"))
        if !AudioUtil.hasSpeech(samples, sampleRate: 16000) { ok = false }
        let trimmed = AudioUtil.trimSilence(samples, sampleRate: 16000)

        // 5) whisper.cpp (Pflicht, wenn in der CI vorbereitet)
        if let id = env["FLUESTER_SELFTEST_WHISPER"] {
            let engine = WhisperLocalEngine(model: WhisperModel.byID(id))
            do {
                let start = Date()
                let text = try await withTimeout(180) { try await engine.transcribe(trimmed, sampleRate: 16000, language: "en-US", hints: ["Flüster"]) }
                log(String(format: "Whisper (%@): „%@“ in %.1f s", id, text, Date().timeIntervalSince(start)))
                if text.lowercased().contains("test") { log("  ✓") } else { ok = false; log("  ✗ Text passt nicht") }
                // Zweiter Lauf: Server läuft schon, muss schnell sein
                let s2 = Date()
                _ = try await engine.transcribe(trimmed, sampleRate: 16000, language: "en-US", hints: [])
                log(String(format: "  zweites Diktat: %.2f s", Date().timeIntervalSince(s2)))
            } catch {
                ok = false
                log("✗ Whisper: \(error.localizedDescription)")
            }
            await WhisperServer.shared.stop()
        }

        // 6) Apple SpeechAnalyzer (macOS 26). In der CI-VM ggf. ohne Sprachpaket → nur Hinweis
        if #available(macOS 26.0, *) {
            do {
                let start = Date()
                let text = try await withTimeout(300) { try await AppleSpeechEngine().transcribe(trimmed, sampleRate: 16000, language: "en-US", hints: []) }
                log(String(format: "Apple SpeechAnalyzer: „%@“ in %.1f s", text, Date().timeIntervalSince(start)))
                if text.lowercased().contains("test") { log("  ✓") } else {
                    if env["FLUESTER_SELFTEST_APPLE_REQUIRED"] != nil { ok = false }
                    log("  ⚠︎ Text passt nicht")
                }
            } catch {
                if env["FLUESTER_SELFTEST_APPLE_REQUIRED"] != nil { ok = false }
                log("⚠︎ Apple SpeechAnalyzer: \(error.localizedDescription)")
            }
        }

        // 7) Apple Intelligence, falls vorhanden
        if Rewriters.appleIntelligence() != nil {
            var ai = Settings()
            ai.cleanup = .appleIntelligence
            let r = await DictationController.cleanText("um so I think we should uh meet at five no wait at six", style: .formal, appName: "Mail", settings: ai)
            log("Apple Intelligence: „\(r.text)“ \(r.warning ?? "")")
        }
        return ok
    }

    /// Text mit `say` sprechen lassen und als 16 kHz-Samples laden.
    static func speech(_ text: String) -> [Float]? {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("fluester-selftest.aiff")
        try? FileManager.default.removeItem(at: url)
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/say")
        p.arguments = ["-o", url.path, text]
        do {
            try p.run()
            p.waitUntilExit()
        } catch {
            return nil
        }
        guard p.terminationStatus == 0 else { return nil }
        return try? AudioBuffers.loadFile(url)
    }
}
