import Foundation

/// Welche Taste das Diktat auslöst.
public enum HotkeyChoice: String, Codable, CaseIterable, Identifiable, Sendable {
    case fn, rightOption, rightCommand, rightControl
    public var id: String { rawValue }
    public var label: String {
        switch self {
        case .fn: return "Fn / 🌐"
        case .rightOption: return "Rechte ⌥ Option"
        case .rightCommand: return "Rechte ⌘ Command"
        case .rightControl: return "Rechte ⌃ Control"
        }
    }
}

/// Spracherkennung.
public enum EngineChoice: String, Codable, CaseIterable, Identifiable, Sendable {
    /// Apple SpeechAnalyzer (macOS 26), auf dem Mac, kostenlos
    case apple
    /// whisper.cpp als lokaler Server (brew install whisper-cpp), kostenlos
    case whisperLocal
    /// OpenAI-kompatible Cloud (Groq Gratis-Kontingent, OpenAI …)
    case cloud
    public var id: String { rawValue }
    public var label: String {
        switch self {
        case .apple: return "Apple (auf dem Mac)"
        case .whisperLocal: return "Whisper lokal (whisper.cpp)"
        case .cloud: return "Cloud (Groq / OpenAI)"
        }
    }
}

/// KI-Bereinigung nach der Erkennung.
public enum CleanupChoice: String, Codable, CaseIterable, Identifiable, Sendable {
    /// Nur feste Regeln (Füllwörter, Leerzeichen, Wörterbuch) – sofort, offline
    case rules
    /// Apple Intelligence (Foundation Models, macOS 26), auf dem Mac
    case appleIntelligence
    /// Sprachmodell über OpenAI-kompatible API (Groq, OpenAI, Ollama lokal …)
    case llm
    public var id: String { rawValue }
    public var label: String {
        switch self {
        case .rules: return "Nur Regeln (sofort, offline)"
        case .appleIntelligence: return "Apple Intelligence (auf dem Mac)"
        case .llm: return "Sprachmodell (Groq / OpenAI / Ollama)"
        }
    }
}

/// Wie der Text in die App kommt.
public enum InsertMethod: String, Codable, CaseIterable, Identifiable, Sendable {
    case paste, type
    public var id: String { rawValue }
    public var label: String { self == .paste ? "Einfügen (schnell, Zwischenablage bleibt erhalten)" : "Tippen (langsamer, ohne Zwischenablage)" }
}

/// Fertige Anbieter-Vorlagen für OpenAI-kompatible Schnittstellen.
public struct ProviderPreset: Identifiable, Hashable, Sendable {
    public let id: String
    public let name: String
    public let baseURL: String
    public let transcriptionModel: String
    public let chatModel: String
    public let needsKey: Bool
    public let keyURL: String

    public static let groq = ProviderPreset(id: "groq", name: "Groq (Gratis-Kontingent)", baseURL: "https://api.groq.com/openai/v1",
                                            transcriptionModel: "whisper-large-v3-turbo", chatModel: "llama-3.3-70b-versatile",
                                            needsKey: true, keyURL: "https://console.groq.com/keys")
    public static let openai = ProviderPreset(id: "openai", name: "OpenAI", baseURL: "https://api.openai.com/v1",
                                              transcriptionModel: "gpt-4o-mini-transcribe", chatModel: "gpt-4o-mini",
                                              needsKey: true, keyURL: "https://platform.openai.com/api-keys")
    public static let ollama = ProviderPreset(id: "ollama", name: "Ollama (lokal)", baseURL: "http://localhost:11434/v1",
                                              transcriptionModel: "", chatModel: "qwen2.5:7b",
                                              needsKey: false, keyURL: "https://ollama.com/download")
    public static let custom = ProviderPreset(id: "custom", name: "Eigener Server", baseURL: "", transcriptionModel: "", chatModel: "",
                                              needsKey: false, keyURL: "")
    public static let all: [ProviderPreset] = [.groq, .openai, .ollama, .custom]
    public static func byID(_ id: String) -> ProviderPreset { all.first { $0.id == id } ?? .groq }
}

/// Verbindung zu einer OpenAI-kompatiblen API. Der Schlüssel liegt im Schlüsselbund, nicht hier.
public struct APIConfig: Codable, Equatable, Sendable {
    public var providerID: String
    public var baseURL: String
    public var model: String

    public init(providerID: String, baseURL: String, model: String) {
        self.providerID = providerID
        self.baseURL = baseURL
        self.model = model
    }
}

/// Lokales Whisper-Modell für whisper.cpp.
public struct WhisperModel: Identifiable, Hashable, Sendable {
    public let id: String
    public let name: String
    public let file: String
    public let sizeMB: Int
    public let note: String
    public var url: URL { URL(string: "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/\(file)")! }

    public static let all: [WhisperModel] = [
        WhisperModel(id: "turbo-q5", name: "Large v3 Turbo (empfohlen)", file: "ggml-large-v3-turbo-q5_0.bin", sizeMB: 547, note: "Beste Qualität für Deutsch und Englisch, schnell auf Apple Silicon"),
        WhisperModel(id: "turbo", name: "Large v3 Turbo (voll)", file: "ggml-large-v3-turbo.bin", sizeMB: 1620, note: "Minimal genauer, braucht mehr Speicher"),
        WhisperModel(id: "small", name: "Small", file: "ggml-small.bin", sizeMB: 466, note: "Für ältere Macs"),
        WhisperModel(id: "base", name: "Base", file: "ggml-base.bin", sizeMB: 142, note: "Sehr schnell, deutlich ungenauer")
    ]
    public static func byID(_ id: String) -> WhisperModel { all.first { $0.id == id } ?? all[0] }
}

/// Alle Einstellungen, als JSON in den UserDefaults.
public struct Settings: Codable, Equatable, Sendable {
    // Bedienung
    public var hotkey: HotkeyChoice = .fn
    public var handsFreeDoubleTap: Bool = true
    public var commandModeEnabled: Bool = true
    public var sounds: Bool = true
    public var showIdleBar: Bool = false
    public var insertMethod: InsertMethod = .paste
    public var smartSpacing: Bool = true
    public var microphoneUID: String = ""
    public var launchAtLogin: Bool = false
    public var onboardingDone: Bool = false
    public var keepHistory: Bool = true

    // Erkennung
    public var engine: EngineChoice = .apple
    /// "auto" oder BCP-47-Code, z. B. "de-DE", "en-US"
    public var language: String = "auto"
    public var whisperModelID: String = "turbo-q5"
    public var cloudTranscription = APIConfig(providerID: "groq", baseURL: ProviderPreset.groq.baseURL, model: ProviderPreset.groq.transcriptionModel)

    // Bereinigung
    public var cleanup: CleanupChoice = .appleIntelligence
    public var cloudChat = APIConfig(providerID: "groq", baseURL: ProviderPreset.groq.baseURL, model: ProviderPreset.groq.chatModel)
    public var removeFillers: Bool = true
    public var applySelfCorrections: Bool = true
    public var formatLists: Bool = true
    public var spokenCommands: Bool = true
    public var spokenPunctuation: Bool = false

    // Persönlich
    public var dictionary: [DictionaryEntry] = []
    public var snippets: [Snippet] = []
    public var styles: StyleSettings = StyleSettings()

    public init() {}

    // Neue Felder sollen alte gespeicherte Einstellungen nicht unlesbar machen
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = Settings.defaults
        func v<T: Decodable>(_ k: CodingKeys, _ fallback: T) -> T {
            if let x = try? c.decodeIfPresent(T.self, forKey: k) { return x }
            return fallback
        }
        hotkey = v(.hotkey, d.hotkey)
        handsFreeDoubleTap = v(.handsFreeDoubleTap, d.handsFreeDoubleTap)
        commandModeEnabled = v(.commandModeEnabled, d.commandModeEnabled)
        sounds = v(.sounds, d.sounds)
        showIdleBar = v(.showIdleBar, d.showIdleBar)
        insertMethod = v(.insertMethod, d.insertMethod)
        smartSpacing = v(.smartSpacing, d.smartSpacing)
        microphoneUID = v(.microphoneUID, d.microphoneUID)
        launchAtLogin = v(.launchAtLogin, d.launchAtLogin)
        onboardingDone = v(.onboardingDone, d.onboardingDone)
        keepHistory = v(.keepHistory, d.keepHistory)
        engine = v(.engine, d.engine)
        language = v(.language, d.language)
        whisperModelID = v(.whisperModelID, d.whisperModelID)
        cloudTranscription = v(.cloudTranscription, d.cloudTranscription)
        cleanup = v(.cleanup, d.cleanup)
        cloudChat = v(.cloudChat, d.cloudChat)
        removeFillers = v(.removeFillers, d.removeFillers)
        applySelfCorrections = v(.applySelfCorrections, d.applySelfCorrections)
        formatLists = v(.formatLists, d.formatLists)
        spokenCommands = v(.spokenCommands, d.spokenCommands)
        spokenPunctuation = v(.spokenPunctuation, d.spokenPunctuation)
        dictionary = v(.dictionary, d.dictionary)
        snippets = v(.snippets, d.snippets)
        styles = v(.styles, d.styles)
    }

    public static let defaults = Settings()

    /// Sprache für die Bereinigung als Klartext für Prompts.
    public var languageName: String {
        switch language.prefix(2) {
        case "de": return "Deutsch"
        case "en": return "Englisch"
        case "fr": return "Französisch"
        case "es": return "Spanisch"
        case "it": return "Italienisch"
        default: return "automatisch erkannt"
        }
    }
}

/// Sprachen in der Auswahl. "auto" lässt die Erkennung entscheiden (Whisper/Cloud) bzw. nimmt die Systemsprache (Apple).
public struct LanguageChoice: Identifiable, Hashable, Sendable {
    public let code: String
    public let name: String
    public var id: String { code }
}

public let languageChoices: [LanguageChoice] = [
    ("auto", "Automatisch"), ("de-DE", "Deutsch (Deutschland)"), ("de-AT", "Deutsch (Österreich)"), ("de-CH", "Deutsch (Schweiz)"),
    ("en-US", "Englisch (USA)"), ("en-GB", "Englisch (UK)"), ("fr-FR", "Französisch"), ("es-ES", "Spanisch"), ("it-IT", "Italienisch"),
    ("nl-NL", "Niederländisch"), ("pt-BR", "Portugiesisch"), ("pl-PL", "Polnisch"), ("tr-TR", "Türkisch")
].map { LanguageChoice(code: $0.0, name: $0.1) }
