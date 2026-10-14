import Foundation
import AVFoundation
import Speech
import FluesterCore

/// Eine Spracherkennung: Samples (16 kHz mono) → Text.
protocol SpeechEngine {
    var name: String { get }
    func transcribe(_ samples: [Float], sampleRate: Double, language: String, hints: [String]) async throws -> String
}

enum SpeechEngines {
    /// Passende Erkennung zu den Einstellungen.
    static func make(_ s: Settings) -> SpeechEngine {
        switch s.engine {
        case .apple:
            if #available(macOS 26.0, *) { return AppleSpeechEngine() }
            return LegacyAppleSpeechEngine()
        case .whisperLocal:
            return WhisperLocalEngine(model: WhisperModel.byID(s.whisperModelID))
        case .cloud:
            return CloudSpeechEngine(config: s.cloudTranscription, key: Keychain.apiKey(for: s.cloudTranscription.providerID))
        }
    }

    static func locale(for language: String) -> Locale {
        language == "auto" ? Locale.current : Locale(identifier: language)
    }
}

// MARK: - Apple SpeechAnalyzer (macOS 26): neu, schnell, auf dem Mac

@available(macOS 26.0, *)
final class AppleSpeechEngine: SpeechEngine {
    let name = "Apple SpeechAnalyzer"

    func transcribe(_ samples: [Float], sampleRate: Double, language: String, hints: [String]) async throws -> String {
        let (transcriber, locale) = try await Self.makeTranscriber(language: language)
        try await Self.ensureAssets(transcriber, locale: locale)
        let format = await SpeechAnalyzer.bestAvailableAudioFormat(compatibleWith: [transcriber])
        let buffer = try AudioBuffers.make(samples: samples, sampleRate: sampleRate, to: format)

        let analyzer = SpeechAnalyzer(modules: [transcriber])
        if !hints.isEmpty {
            let context = AnalysisContext()
            context.contextualStrings[.general] = hints
            try? await analyzer.setContext(context)
        }
        let (stream, continuation) = AsyncStream.makeStream(of: AnalyzerInput.self)
        let collector = Task { () -> String in
            var text = ""
            for try await result in transcriber.results {
                text += String(result.text.characters)
            }
            return text
        }
        continuation.yield(AnalyzerInput(buffer: buffer))
        continuation.finish()
        if let last = try await analyzer.analyzeSequence(stream) {
            try await analyzer.finalizeAndFinish(through: last)
        } else {
            await analyzer.cancelAndFinishNow()
        }
        return try await collector.value.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    static func makeTranscriber(language: String) async throws -> (SpeechTranscriber, Locale) {
        let wanted = SpeechEngines.locale(for: language)
        guard let locale = await SpeechTranscriber.supportedLocale(equivalentTo: wanted) else {
            throw FluesterError("Apple-Spracherkennung kann „\(wanted.identifier)“ nicht – andere Sprache oder Whisper wählen")
        }
        return (SpeechTranscriber(locale: locale, transcriptionOptions: [], reportingOptions: [], attributeOptions: []), locale)
    }

    /// Sprachpaket reservieren und laden, falls es fehlt (einmalig, ein paar hundert MB).
    static func ensureAssets(_ transcriber: SpeechTranscriber, locale: Locale) async throws {
        // Ohne Reservierung verweigert der Asset-Katalog den Download ("not subscribed")
        let reserved = await AssetInventory.reservedLocales
        if !reserved.contains(where: { $0.identifier(.bcp47) == locale.identifier(.bcp47) }) {
            let limit = await AssetInventory.maximumReservedLocales
            if reserved.count >= limit, let oldest = reserved.first {
                _ = await AssetInventory.release(reservedLocale: oldest)
            }
            _ = try await AssetInventory.reserve(locale: locale)
        }
        if let request = try await AssetInventory.assetInstallationRequest(supporting: [transcriber]) {
            try await request.downloadAndInstall()
        }
    }

    /// Für die Einstellungen: Sprachpaket vorab laden.
    static func prepare(language: String) async throws {
        let (t, locale) = try await makeTranscriber(language: language)
        try await ensureAssets(t, locale: locale)
    }
}

// MARK: - SFSpeechRecognizer (macOS 14/15): auf dem Gerät, einfacher

final class LegacyAppleSpeechEngine: SpeechEngine {
    let name = "Apple Spracherkennung"

    func transcribe(_ samples: [Float], sampleRate: Double, language: String, hints: [String]) async throws -> String {
        try await Self.authorize()
        let locale = SpeechEngines.locale(for: language)
        guard let recognizer = SFSpeechRecognizer(locale: locale) ?? SFSpeechRecognizer(), recognizer.isAvailable else {
            throw FluesterError("Apple-Spracherkennung für „\(locale.identifier)“ nicht verfügbar")
        }
        let request = SFSpeechAudioBufferRecognitionRequest()
        request.shouldReportPartialResults = false
        if recognizer.supportsOnDeviceRecognition { request.requiresOnDeviceRecognition = true }
        request.addsPunctuation = true
        request.contextualStrings = hints
        request.append(try AudioBuffers.make(samples: samples, sampleRate: sampleRate, to: nil))
        request.endAudio()
        let done = Flag()
        return try await withCheckedThrowingContinuation { (cont: CheckedContinuation<String, Error>) in
            _ = recognizer.recognitionTask(with: request) { result, error in
                if done.value { return }
                if let error = error {
                    done.value = true
                    let ns = error as NSError
                    // "No speech detected" ist kein Fehler, sondern Stille
                    if ns.code == 1110 || ns.code == 203 { cont.resume(returning: "") } else { cont.resume(throwing: error) }
                    return
                }
                if let result = result, result.isFinal {
                    done.value = true
                    cont.resume(returning: result.bestTranscription.formattedString)
                }
            }
        }
    }

    static func authorize() async throws {
        let status = SFSpeechRecognizer.authorizationStatus()
        if status == .authorized { return }
        if status == .denied || status == .restricted {
            throw FluesterError("Spracherkennung nicht erlaubt – Systemeinstellungen › Datenschutz › Spracherkennung")
        }
        let ok = await withCheckedContinuation { (c: CheckedContinuation<Bool, Never>) in
            SFSpeechRecognizer.requestAuthorization { c.resume(returning: $0 == .authorized) }
        }
        if !ok { throw FluesterError("Spracherkennung nicht erlaubt") }
    }
}

// MARK: - Cloud (Groq, OpenAI …)

final class CloudSpeechEngine: SpeechEngine {
    let config: APIConfig
    let key: String?
    var name: String { ProviderPreset.byID(config.providerID).name }

    init(config: APIConfig, key: String?) {
        self.config = config
        self.key = key
    }

    func transcribe(_ samples: [Float], sampleRate: Double, language: String, hints: [String]) async throws -> String {
        let preset = ProviderPreset.byID(config.providerID)
        if preset.needsKey && (key ?? "").isEmpty {
            throw FluesterError("API-Schlüssel für \(preset.name) fehlt – Einstellungen › Erkennung")
        }
        let wav = AudioUtil.wav(samples: samples, sampleRate: Int(sampleRate))
        let client = OpenAICompatibleClient(baseURL: config.baseURL, apiKey: key, timeout: 45)
        return try await client.transcribe(wav: wav, model: config.model, language: language, prompt: hints.joined(separator: ", "))
    }
}

// MARK: - whisper.cpp lokal (whisper-server aus Homebrew)

final class WhisperLocalEngine: SpeechEngine {
    let model: WhisperModel
    var name: String { "Whisper " + model.name }

    init(model: WhisperModel) { self.model = model }

    func transcribe(_ samples: [Float], sampleRate: Double, language: String, hints: [String]) async throws -> String {
        let port = try await WhisperServer.shared.ensureRunning(model: model, language: language)
        var form = MultipartForm()
        form.addFile("file", filename: "diktat.wav", mime: "audio/wav", data: AudioUtil.wav(samples: samples, sampleRate: Int(sampleRate)))
        form.add("temperature", "0.0")
        form.add("temperature_inc", "0.2")
        form.add("response_format", "json")
        if !hints.isEmpty { form.add("prompt", hints.joined(separator: ", ")) }
        var req = URLRequest(url: URL(string: "http://127.0.0.1:\(port)/inference")!, timeoutInterval: 120)
        req.httpMethod = "POST"
        req.setValue(form.contentType, forHTTPHeaderField: "Content-Type")
        req.httpBody = form.finished()
        let (data, resp) = try await URLSession.shared.data(for: req)
        guard (resp as? HTTPURLResponse)?.statusCode == 200 else { throw FluesterError("whisper-server antwortet nicht richtig") }
        return try OpenAICompatibleClient.parseTranscription(data)
    }
}

/// Startet whisper-server einmal mit dem Modell im Speicher, damit jedes Diktat schnell ist.
actor WhisperServer {
    static let shared = WhisperServer()
    /// Für das Beenden der App ohne await
    nonisolated(unsafe) static var runningPID: Int32 = 0

    private var process: Process?
    private var key = ""
    let port = 18000 + Int(getpid() % 900)

    static func binaryPath() -> String? {
        let candidates = ["/opt/homebrew/bin/whisper-server", "/usr/local/bin/whisper-server",
                          NSHomeDirectory() + "/.local/bin/whisper-server", "/opt/local/bin/whisper-server"]
        return candidates.first { FileManager.default.isExecutableFile(atPath: $0) }
    }

    static func modelURL(_ m: WhisperModel) -> URL { AppPaths.models.appendingPathComponent(m.file) }
    static func isInstalled(_ m: WhisperModel) -> Bool { FileManager.default.fileExists(atPath: modelURL(m).path) }

    static var logURL: URL { AppPaths.support.appendingPathComponent("whisper-server.log") }

    func ensureRunning(model: WhisperModel, language: String) async throws -> Int {
        let lang = language == "auto" ? "auto" : String(language.prefix(2))
        let wanted = model.file + "|" + lang
        if let p = process, p.isRunning, wanted == key { return port }
        stop()
        guard let bin = Self.binaryPath() else {
            throw FluesterError("whisper.cpp fehlt – im Terminal: brew install whisper-cpp")
        }
        let modelPath = Self.modelURL(model).path
        guard FileManager.default.fileExists(atPath: modelPath) else {
            throw FluesterError("Whisper-Modell fehlt – Einstellungen › Erkennung › Modell laden")
        }
        // Erst mit GPU (Metal), falls der Server sofort abstürzt ohne
        for useGPU in [true, false] {
            let p = try launch(bin: bin, modelPath: modelPath, lang: lang, gpu: useGPU)
            process = p
            key = wanted
            Self.runningPID = p.processIdentifier
            // Modell laden dauert je nach Größe ein paar Sekunden
            for _ in 0..<240 {
                if await isUp() { return port }
                if !p.isRunning { break }
                try await Task.sleep(nanoseconds: 150_000_000)
            }
            let crashed = !p.isRunning
            stop()
            if !crashed { break }   // läuft, antwortet aber nicht – ohne GPU hilft das nicht
        }
        throw FluesterError("whisper-server startet nicht: " + Self.logTail())
    }

    private func launch(bin: String, modelPath: String, lang: String, gpu: Bool) throws -> Process {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: bin)
        let threads = max(4, min(8, ProcessInfo.processInfo.activeProcessorCount - 2))
        var args = ["-m", modelPath, "-l", lang, "--host", "127.0.0.1", "--port", String(port), "-t", String(threads), "-nt"]
        if !gpu { args.append("-ng") }
        p.arguments = args
        FileManager.default.createFile(atPath: Self.logURL.path, contents: nil)
        let log = try? FileHandle(forWritingTo: Self.logURL)
        p.standardOutput = log ?? FileHandle.nullDevice
        p.standardError = log ?? FileHandle.nullDevice
        try p.run()
        return p
    }

    /// Letzte Zeilen des Server-Logs für Fehlermeldungen.
    static func logTail(_ lines: Int = 4) -> String {
        guard let text = try? String(contentsOf: logURL, encoding: .utf8) else { return "kein Log" }
        let tail = text.split(separator: "\n").suffix(lines).joined(separator: " · ")
        return tail.isEmpty ? "kein Log" : String(tail.suffix(400))
    }

    private func isUp() async -> Bool {
        var r = URLRequest(url: URL(string: "http://127.0.0.1:\(port)/")!)
        r.timeoutInterval = 0.4
        do {
            _ = try await URLSession.shared.data(for: r)
            return true
        } catch {
            return false
        }
    }

    func stop() {
        if let p = process, p.isRunning { p.terminate() }
        process = nil
        key = ""
        Self.runningPID = 0
    }

    /// Beim Beenden der App: synchron, ohne Actor.
    static func killNow() {
        if runningPID > 0 { kill(runningPID, SIGTERM) }
    }
}

/// Lädt Whisper-Modelle mit Fortschritt.
final class ModelDownloader: NSObject, ObservableObject, URLSessionDownloadDelegate {
    static let shared = ModelDownloader()

    @Published var progress: Double = 0
    @Published var activeID: String?
    @Published var error: String?
    @Published var revision = 0

    private var session: URLSession?
    private var current: WhisperModel?

    func download(_ model: WhisperModel) {
        guard activeID == nil else { return }
        error = nil
        progress = 0
        activeID = model.id
        current = model
        let s = URLSession(configuration: .default, delegate: self, delegateQueue: nil)
        session = s
        s.downloadTask(with: model.url).resume()
    }

    func cancel() {
        session?.invalidateAndCancel()
        session = nil
        activeID = nil
    }

    func delete(_ model: WhisperModel) {
        try? FileManager.default.removeItem(at: WhisperServer.modelURL(model))
        revision += 1
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didWriteData bytesWritten: Int64,
                    totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) {
        guard totalBytesExpectedToWrite > 0 else { return }
        let p = Double(totalBytesWritten) / Double(totalBytesExpectedToWrite)
        DispatchQueue.main.async { self.progress = p }
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        guard let model = current else { return }
        let status = (downloadTask.response as? HTTPURLResponse)?.statusCode ?? 200
        if status != 200 {
            DispatchQueue.main.async { self.error = "Download fehlgeschlagen (HTTP \(status))" }
            return
        }
        let dest = WhisperServer.modelURL(model)
        try? FileManager.default.removeItem(at: dest)
        do {
            try FileManager.default.moveItem(at: location, to: dest)
        } catch {
            let msg = error.localizedDescription
            DispatchQueue.main.async { self.error = msg }
        }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        let msg = error?.localizedDescription
        DispatchQueue.main.async {
            if let msg = msg, (error as NSError?)?.code != NSURLErrorCancelled { self.error = msg }
            self.activeID = nil
            self.current = nil
            self.revision += 1
            self.session?.finishTasksAndInvalidate()
            self.session = nil
        }
    }
}
