import Foundation
import FluesterCore
#if canImport(FoundationModels)
import FoundationModels
#endif

/// Ein Sprachmodell für Bereinigung und Command Mode.
protocol Rewriter {
    var name: String { get }
    func complete(system: String, user: String) async throws -> String
}

enum Rewriters {
    /// Bereinigungs-Modell laut Einstellungen (nil = nur Regeln).
    static func cleanup(_ s: Settings) -> Rewriter? {
        switch s.cleanup {
        case .rules: return nil
        case .appleIntelligence: return appleIntelligence()
        case .llm: return CloudRewriter(config: s.cloudChat, key: Keychain.apiKey(for: s.cloudChat.providerID))
        }
    }

    /// Für Command Mode: das eingestellte Modell, sonst Apple Intelligence, sonst ein konfiguriertes Cloud-Modell.
    static func command(_ s: Settings) -> Rewriter? {
        if let r = cleanup(s) { return r }
        if let a = appleIntelligence() { return a }
        let key = Keychain.apiKey(for: s.cloudChat.providerID)
        if !(key ?? "").isEmpty || !ProviderPreset.byID(s.cloudChat.providerID).needsKey {
            return CloudRewriter(config: s.cloudChat, key: key)
        }
        return nil
    }

    static func appleIntelligence() -> Rewriter? {
        #if canImport(FoundationModels)
        if #available(macOS 26.0, *), AppleIntelligenceRewriter.isAvailable { return AppleIntelligenceRewriter() }
        #endif
        return nil
    }

    /// Klartext, warum Apple Intelligence nicht geht (nil = verfügbar).
    static func appleIntelligenceProblem() -> String? {
        #if canImport(FoundationModels)
        if #available(macOS 26.0, *) { return AppleIntelligenceRewriter.problem }
        #endif
        return "Braucht macOS 26 (Tahoe) und einen Mac mit Apple Silicon"
    }
}

#if canImport(FoundationModels)
@available(macOS 26.0, *)
final class AppleIntelligenceRewriter: Rewriter {
    let name = "Apple Intelligence"

    static var isAvailable: Bool { SystemLanguageModel.default.isAvailable }

    static var problem: String? {
        switch SystemLanguageModel.default.availability {
        case .available:
            return nil
        case .unavailable(let reason):
            let r = String(describing: reason)
            if r.contains("deviceNotEligible") { return "Dieser Mac unterstützt Apple Intelligence nicht" }
            if r.contains("appleIntelligenceNotEnabled") { return "Apple Intelligence ist aus – Systemeinstellungen › Apple Intelligence & Siri" }
            if r.contains("modelNotReady") { return "Apple Intelligence lädt noch das Modell – später erneut versuchen" }
            return "Apple Intelligence nicht verfügbar (\(r))"
        @unknown default:
            return "Apple Intelligence nicht verfügbar"
        }
    }

    func complete(system: String, user: String) async throws -> String {
        let session = LanguageModelSession(instructions: system)
        let response = try await session.respond(to: user)
        return response.content
    }
}
#endif

/// OpenAI-kompatibles Chat-Modell (Groq, OpenAI, Ollama, LM Studio …).
final class CloudRewriter: Rewriter {
    let config: APIConfig
    let key: String?
    var name: String { ProviderPreset.byID(config.providerID).name + " · " + config.model }

    init(config: APIConfig, key: String?) {
        self.config = config
        self.key = key
    }

    func complete(system: String, user: String) async throws -> String {
        let preset = ProviderPreset.byID(config.providerID)
        if preset.needsKey && (key ?? "").isEmpty {
            throw FluesterError("API-Schlüssel für \(preset.name) fehlt – Einstellungen › KI")
        }
        let client = OpenAICompatibleClient(baseURL: config.baseURL, apiKey: key, timeout: 30)
        return try await client.chat(model: config.model, system: system, user: user)
    }
}
