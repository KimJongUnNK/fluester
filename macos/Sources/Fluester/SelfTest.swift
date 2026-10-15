import AppKit
import AVFoundation
import FluesterCore

/// `Fluester --selftest`: prüft ohne Mikrofon und ohne Klicks, ob Erkennung, Bereinigung und Oberfläche wirklich laufen.
/// Sprache kommt aus FLUESTER_SELFTEST_WAV (CI: jfk.wav) oder von `say`. Für die CI und zur Fehlersuche auf dem eigenen Mac.
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
        log("Spracherkennung: \(Permissions.speechRecognitionGranted ? "erlaubt" : Permissions.speechRecognitionUndetermined ? "nicht gefragt" : "nicht erlaubt")")
        log("Apple Intelligence: \(Rewriters.appleIntelligenceProblem() ?? "verfügbar")")

        // 4) Testaudio: eine echte Aufnahme (FLUESTER_SELFTEST_WAV, in der CI jfk.wav) ist verlässlich.
        //    Ohne sie spricht `say` – das liefert auf frisch gestarteten CI-Macs gelegentlich leere Dateien.
        let samples: [Float]
        var expectWord = "test"
        if let wav = env["FLUESTER_SELFTEST_WAV"], let fromFile = try? AudioBuffers.loadFile(URL(fileURLWithPath: wav)) {
            samples = fromFile
            expectWord = (env["FLUESTER_SELFTEST_EXPECT"] ?? "").lowercased()
            log("Testaudio: \(URL(fileURLWithPath: wav).lastPathComponent)")
        } else if let spoken = speech("Hello, this is a test of the dictation app.") {
            samples = spoken
            log("Testaudio: say")
        } else {
            log("✗ Kein Testaudio – say lieferte auch nach drei Versuchen keine Sprache")
            return false
        }
        let expect = expectWord
        let matches: (String) -> Bool = { text in
            expect.isEmpty ? !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty : text.lowercased().contains(expect)
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
                if matches(text) { log("  ✓") } else { ok = false; log("  ✗ Text passt nicht") }
                // Zweiter Lauf: Server läuft schon, muss schnell sein
                let s2 = Date()
                _ = try await engine.transcribe(trimmed, sampleRate: 16000, language: "en-US", hints: [])
                log(String(format: "  zweites Diktat: %.2f s", Date().timeIntervalSince(s2)))
            } catch {
                ok = false
                log("✗ Whisper: \(error.localizedDescription)")
            }
            await WhisperServer.shared.stop()
            for line in whisperBackendLines() { log("  · " + line) }
        }

        // 6) Apple (macOS 26). In der CI-VM ggf. ohne Sprachpaket → nur Hinweise, außer ausdrücklich verlangt
        if #available(macOS 26.0, *) {
            let appleRequired = env["FLUESTER_SELFTEST_APPLE_REQUIRED"] != nil
            log("Apple-Sprachpaket:")
            do {
                try await withTimeout(300) { try await AppleSpeechEngine.prepare(language: "en-US", trace: { log("  · " + $0) }) }
                log("  ✓ bereit")
            } catch {
                log("  ⚠︎ \(error.localizedDescription)")
            }
            do {
                let start = Date()
                let text = try await withTimeout(120) { try await AppleSpeechEngine().transcribe(trimmed, sampleRate: 16000, language: "en-US", hints: []) }
                log(String(format: "Apple SpeechAnalyzer: „%@“ in %.1f s", text, Date().timeIntervalSince(start)))
                if matches(text) { log("  ✓") } else {
                    if appleRequired { ok = false }
                    log("  ⚠︎ Text passt nicht")
                }
            } catch {
                if appleRequired { ok = false }
                log("⚠︎ Apple SpeechAnalyzer: \(error.localizedDescription)")
            }
            // Standard-Engine der App: SpeechAnalyzer oder Rückfall auf SFSpeechRecognizer
            let auto = AppleAutoEngine()
            do {
                let start = Date()
                let text = try await withTimeout(120) { try await auto.transcribe(trimmed, sampleRate: 16000, language: "en-US", hints: []) }
                log(String(format: "Apple (Standard, %@): „%@“ in %.1f s", auto.name, text, Date().timeIntervalSince(start)))
                log(matches(text) ? "  ✓" : "  ⚠︎ Text passt nicht")
            } catch {
                log("⚠︎ Apple (Standard): \(error.localizedDescription)")
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

    /// Aus dem whisper-server-Log: läuft Metal (GPU) oder nur die CPU?
    static func whisperBackendLines() -> [String] {
        guard let text = try? String(contentsOf: WhisperServer.logURL, encoding: .utf8) else { return [] }
        let keys = ["metal", "gpu", "backend", "device"]
        let hits = text.split(separator: "\n").map(String.init).filter { line in
            let l = line.lowercased()
            return keys.contains { l.contains($0) }
        }
        return Array(hits.prefix(6)).map { String($0.prefix(160)) }
    }

    /// Text mit `say` sprechen lassen und als 16 kHz-Samples laden. `say` liefert auf frisch gestarteten Macs
    /// manchmal eine leere Datei – deshalb bis zu drei Versuche, und nur Audio mit hörbarer Sprache zählt.
    static func speech(_ text: String) -> [Float]? {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("fluester-selftest.aiff")
        for attempt in 1...3 {
            try? FileManager.default.removeItem(at: url)
            let p = Process()
            p.executableURL = URL(fileURLWithPath: "/usr/bin/say")
            p.arguments = ["-o", url.path, text]
            if (try? p.run()) != nil {
                p.waitUntilExit()
                if p.terminationStatus == 0, let samples = try? AudioBuffers.loadFile(url),
                   AudioUtil.hasSpeech(samples, sampleRate: 16000) {
                    return samples
                }
            }
            log("  · say lieferte keine Sprache (Versuch \(attempt) von 3)")
            Thread.sleep(forTimeInterval: 2)
        }
        return nil
    }
}
