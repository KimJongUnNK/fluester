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

    // MARK: Spracherkennung

    public func transcriptionRequest(wav: Data, model: String, language: String?, prompt: String?) throws -> URLRequest {
        var req = URLRequest(url: try url("/audio/transcriptions"), timeoutInterval: timeout)
        req.httpMethod = "POST"
        if let k = apiKey, !k.isEmpty { req.setValue("Bearer \(k)", forHTTPHeaderField: "Authorization") }
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

    public func chatRequest(model: String, system: String, user: String, temperature: Double = 0.2) throws -> URLRequest {
        var req = URLRequest(url: try url("/chat/completions"), timeoutInterval: timeout)
        req.httpMethod = "POST"
        if let k = apiKey, !k.isEmpty { req.setValue("Bearer \(k)", forHTTPHeaderField: "Authorization") }
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        let body: [String: Any] = [
            "model": model,
            "temperature": temperature,
            "messages": [["role": "system", "content": system], ["role": "user", "content": user]]
        ]
        req.httpBody = try JSONSerialization.data(withJSONObject: body)
        return req
    }

    public func chat(model: String, system: String, user: String) async throws -> String {
        let data = try await send(try chatRequest(model: model, system: system, user: user))
        return try Self.parseChat(data)
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
