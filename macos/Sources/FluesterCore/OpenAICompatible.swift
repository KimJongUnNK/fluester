import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// Fehler mit verständlicher Meldung für die Leiste/den Dialog.
public struct FluesterError: LocalizedError, Equatable, Sendable {
    public let message: String
    public init(_ message: String) { self.message = message }
    public var errorDescription: String? { message }
}

/// multipart/form-data für Datei-Uploads.
public struct MultipartForm {
    public let boundary: String
    private(set) public var body = Data()

    public init(boundary: String = "fluester-\(UUID().uuidString)") {
        self.boundary = boundary
    }

    public var contentType: String { "multipart/form-data; boundary=\(boundary)" }

    public mutating func add(_ name: String, _ value: String) {
        body.append(Data("--\(boundary)\r\nContent-Disposition: form-data; name=\"\(name)\"\r\n\r\n\(value)\r\n".utf8))
    }

    public mutating func addFile(_ name: String, filename: String, mime: String, data: Data) {
        body.append(Data("--\(boundary)\r\nContent-Disposition: form-data; name=\"\(name)\"; filename=\"\(filename)\"\r\nContent-Type: \(mime)\r\n\r\n".utf8))
        body.append(data)
        body.append(Data("\r\n".utf8))
    }

    public func finished() -> Data {
        var d = body
        d.append(Data("--\(boundary)--\r\n".utf8))
        return d
    }
}

/// Minimaler Client für OpenAI-kompatible Endpunkte (Groq, OpenAI, Ollama, LM Studio, whisper.cpp-Server …).
public struct OpenAICompatibleClient: Sendable {
    public var baseURL: String
    public var apiKey: String?
    public var timeout: TimeInterval

    public init(baseURL: String, apiKey: String?, timeout: TimeInterval = 60) {
        self.baseURL = baseURL
        self.apiKey = apiKey
        self.timeout = timeout
    }

    func url(_ path: String) throws -> URL {
        var b = baseURL.trimmingCharacters(in: .whitespacesAndNewlines)
        while b.hasSuffix("/") { b.removeLast() }
        guard !b.isEmpty, let u = URL(string: b + path) else { throw FluesterError("Ungültige Server-Adresse: \(baseURL)") }
        return u
    }

    /// Anthropic-Schnittstelle (Claude) – braucht zusätzlich x-api-key und eine Versionsangabe.
    public var isAnthropic: Bool {
        guard let host = URL(string: baseURL.trimmingCharacters(in: .whitespacesAndNewlines))?.host?.lowercased() else { return false }
        return host.hasSuffix("anthropic.com")
    }

    /// Neuere Modelle (Claude, GPT-5, o-Reihe) lehnen eine eigene Temperatur ab.
    public static func acceptsTemperature(_ model: String) -> Bool {
        var m = model.trimmingCharacters(in: .whitespaces).lowercased()
        if let slash = m.lastIndex(of: "/") { m = String(m[m.index(after: slash)...]) }
        return !["claude", "gpt-5", "o1", "o3", "o4"].contains { m.hasPrefix($0) }
    }

    func authorize(_ req: inout URLRequest) {
        guard let k = apiKey, !k.isEmpty else { return }
        req.setValue("Bearer \(k)", forHTTPHeaderField: "Authorization")
        if isAnthropic {
            req.setValue(k, forHTTPHeaderField: "x-api-key")
            req.setValue("2023-06-01", forHTTPHeaderField: "anthropic-version")
        }
    }

    // MARK: Spracherkennung

    public func transcriptionRequest(wav: Data, model: String, language: String?, prompt: String?) throws -> URLRequest {
        var req = URLRequest(url: try url("/audio/transcriptions"), timeoutInterval: timeout)
        req.httpMethod = "POST"
        authorize(&req)
        var form = MultipartForm()
        form.addFile("file", filename: "diktat.wav", mime: "audio/wav", data: wav)
        form.add("model", model)
        form.add("response_format", "json")
        form.add("temperature", "0")
        if let l = language, !l.isEmpty, l != "auto" { form.add("language", String(l.prefix(2))) }
        if let p = prompt, !p.isEmpty { form.add("prompt", String(p.prefix(800))) }
        req.setValue(form.contentType, forHTTPHeaderField: "Content-Type")
        req.httpBody = form.finished()
        return req
    }

    public func transcribe(wav: Data, model: String, language: String?, prompt: String?) async throws -> String {
        let req = try transcriptionRequest(wav: wav, model: model, language: language, prompt: prompt)
        let data = try await send(req)
        return try Self.parseTranscription(data)
    }

    public static func parseTranscription(_ data: Data) throws -> String {
        if let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any], let text = obj["text"] as? String {
            return text.trimmingCharacters(in: .whitespacesAndNewlines)
        }
        if let s = String(data: data, encoding: .utf8), !s.hasPrefix("{") { return s.trimmingCharacters(in: .whitespacesAndNewlines) }
        throw FluesterError("Unerwartete Antwort der Spracherkennung")
    }

    // MARK: Chat

    public func chatRequest(model: String, system: String, user: String, temperature: Double? = 0.2) throws -> URLRequest {
        var req = URLRequest(url: try url("/chat/completions"), timeoutInterval: timeout)
        req.httpMethod = "POST"
        authorize(&req)
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        var body: [String: Any] = [
            "model": model,
            "messages": [["role": "system", "content": system], ["role": "user", "content": user]]
        ]
        if let t = temperature, Self.acceptsTemperature(model) { body["temperature"] = t }
        // Claude verlangt eine Obergrenze für die Antwortlänge
        if isAnthropic { body["max_tokens"] = 8192 }
        req.httpBody = try JSONSerialization.data(withJSONObject: body)
        return req
    }

    public func chat(model: String, system: String, user: String) async throws -> String {
        do {
            return try Self.parseChat(try await send(try chatRequest(model: model, system: system, user: user)))
        } catch let e as FluesterError where e.message.lowercased().contains("temperature") {
            // Modell kennt keine eigene Temperatur – ohne nochmal
            return try Self.parseChat(try await send(try chatRequest(model: model, system: system, user: user, temperature: nil)))
        }
    }

    // MARK: Modelle

    /// Modelle, die der Anbieter anbietet (GET /models), sortiert.
    public func listModels() async throws -> [String] {
        var req = URLRequest(url: try url("/models"), timeoutInterval: timeout)
        authorize(&req)
        return try Self.parseModels(try await send(req))
    }

    public static func parseModels(_ data: Data) throws -> [String] {
        guard let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let arr = (obj["data"] ?? obj["models"]) as? [Any] else {
            throw FluesterError("Unerwartete Antwort beim Laden der Modelle")
        }
        var seen = Set<String>()
        var out: [String] = []
        for item in arr {
            guard let m = item as? [String: Any], let raw = (m["id"] ?? m["name"] ?? m["model"]) as? String else { continue }
            var id = raw.trimmingCharacters(in: .whitespaces)
            if id.hasPrefix("models/") { id.removeFirst(7) }
            if !id.isEmpty, seen.insert(id).inserted { out.append(id) }
        }
        return out.sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
    }

    /// Für die Erkennung nur Sprach-Modelle, wenn erkennbar.
    public static func speechModels(_ all: [String]) -> [String] {
        let speech = all.filter { let l = $0.lowercased(); return l.contains("whisper") || l.contains("transcribe") || l.contains("voxtral") }
        return speech.isEmpty ? all : speech
    }

    public static func parseChat(_ data: Data) throws -> String {
        guard let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let choices = obj["choices"] as? [[String: Any]], let first = choices.first,
              let msg = first["message"] as? [String: Any], let content = msg["content"] as? String else {
            throw FluesterError("Unerwartete Antwort des Sprachmodells")
        }
        return content
    }

    // MARK: Transport

    func send(_ req: URLRequest) async throws -> Data {
        let (data, resp) = try await URLSession.shared.data(for: req)
        guard let http = resp as? HTTPURLResponse else { throw FluesterError("Keine Antwort vom Server") }
        guard (200..<300).contains(http.statusCode) else {
            throw FluesterError(Self.describe(status: http.statusCode, body: data))
        }
        return data
    }

    public static func describe(status: Int, body: Data) -> String {
        var detail = ""
        if let obj = try? JSONSerialization.jsonObject(with: body) as? [String: Any] {
            if let e = obj["error"] as? [String: Any], let m = e["message"] as? String { detail = m }
            else if let m = obj["error"] as? String { detail = m }
            else if let m = obj["message"] as? String { detail = m }
        }
        switch status {
        case 401, 403: return "API-Schlüssel ungültig oder fehlt" + (detail.isEmpty ? "" : " (\(detail))")
        case 404: return "Modell oder Adresse nicht gefunden" + (detail.isEmpty ? "" : " (\(detail))")
        case 413: return "Aufnahme zu groß für den Server"
        case 429: return "Limit erreicht – kurz warten oder anderes Modell wählen" + (detail.isEmpty ? "" : " (\(detail))")
        default: return "Server-Fehler \(status)" + (detail.isEmpty ? "" : ": \(detail)")
        }
    }
}
