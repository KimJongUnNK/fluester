import Foundation

/// Welche Taste das Diktat auslöst.
public enum HotkeyChoice: String, Codable, CaseIterable, Identifiable, Sendable {
    case fn, rightOption, rightCommand, rightControl
    /// Frei gewählte Taste (`Settings.customHotkeyCode`)
    case custom
    public var id: String { rawValue }
    public var label: String {
        switch self {
        case .fn: return "Fn / 🌐"
        case .rightOption: return "Rechte ⌥ Option"
        case .rightCommand: return "Rechte ⌘ Command"
        case .rightControl: return "Rechte ⌃ Control"
        case .custom: return "Eigene Taste …"
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
        case .cloud: return "Cloud (Groq / OpenAI …)"
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
        case .llm: return "Sprachmodell (Claude, GPT, Gemini, Llama …)"
        }
    }
}

/// Wie der Text in die App kommt.
public enum InsertMethod: String, Codable, CaseIterable, Identifiable, Sendable {
    case paste, type
    public var id: String { rawValue }
    public var label: String { self == .paste ? "Einfügen (schnell, Zwischenablage bleibt erhalten)" : "Tippen (langsamer, ohne Zwischenablage)" }
}

/// Fertige Anbieter-Vorlagen für OpenAI-kompatible Schnittstellen. `transcription`: bietet /audio/transcriptions an.
/// Die Modell-Listen sind Vorschläge – jedes Modell des Anbieters lässt sich eintippen oder per „Modelle laden“ abrufen.
/// Gleiche Liste wie unter Windows und Linux (dotnet/Fluester.Core/Settings.cs).
public struct ProviderPreset: Identifiable, Hashable, Sendable {
    public let id: String
    public let name: String
    public let baseURL: String
    public let transcriptionModel: String
    public let chatModel: String
    public let needsKey: Bool
    public let keyURL: String
    public var transcription: Bool = false
    public var chatModels: [String] = []
    public var transcriptionModels: [String] = []

    public static let groq = ProviderPreset(id: "groq", name: "Groq (Gratis-Kontingent)", baseURL: "https://api.groq.com/openai/v1",
                                            transcriptionModel: "whisper-large-v3-turbo", chatModel: "llama-3.3-70b-versatile",
                                            needsKey: true, keyURL: "https://console.groq.com/keys", transcription: true,
                                            chatModels: ["llama-3.3-70b-versatile", "openai/gpt-oss-120b", "openai/gpt-oss-20b", "llama-3.1-8b-instant"],
                                            transcriptionModels: ["whisper-large-v3-turbo", "whisper-large-v3"])
    public static let openai = ProviderPreset(id: "openai", name: "OpenAI (GPT)", baseURL: "https://api.openai.com/v1",
                                              transcriptionModel: "gpt-4o-mini-transcribe", chatModel: "gpt-4o-mini",
                                              needsKey: true, keyURL: "https://platform.openai.com/api-keys", transcription: true,
                                              chatModels: ["gpt-4o-mini", "gpt-4.1-mini", "gpt-4.1", "gpt-4o", "gpt-5-mini", "gpt-5"],
                                              transcriptionModels: ["gpt-4o-mini-transcribe", "gpt-4o-transcribe", "whisper-1"])
    public static let anthropic = ProviderPreset(id: "anthropic", name: "Anthropic (Claude)", baseURL: "https://api.anthropic.com/v1",
                                                 transcriptionModel: "", chatModel: "claude-haiku-5-5",
                                                 needsKey: true, keyURL: "https://console.anthropic.com/settings/keys",
                                                 chatModels: ["claude-haiku-5-5", "claude-sonnet-5-5", "claude-opus-5-5"])
    public static let gemini = ProviderPreset(id: "gemini", name: "Google Gemini", baseURL: "https://generativelanguage.googleapis.com/v1beta/openai",
                                              transcriptionModel: "", chatModel: "gemini-2.5-flash",
                                              needsKey: true, keyURL: "https://aistudio.google.com/apikey",
                                              chatModels: ["gemini-2.5-flash", "gemini-2.5-flash-lite", "gemini-2.5-pro"])
    public static let mistral = ProviderPreset(id: "mistral", name: "Mistral AI", baseURL: "https://api.mistral.ai/v1",
                                               transcriptionModel: "", chatModel: "mistral-small-latest",
                                               needsKey: true, keyURL: "https://console.mistral.ai/api-keys",
                                               chatModels: ["mistral-small-latest", "mistral-medium-latest", "mistral-large-latest", "ministral-8b-latest"])
    public static let deepseek = ProviderPreset(id: "deepseek", name: "DeepSeek", baseURL: "https://api.deepseek.com/v1",
                                                transcriptionModel: "", chatModel: "deepseek-chat",
                                                needsKey: true, keyURL: "https://platform.deepseek.com/api_keys",
                                                chatModels: ["deepseek-chat", "deepseek-reasoner"])
    public static let xai = ProviderPreset(id: "xai", name: "xAI (Grok)", baseURL: "https://api.x.ai/v1",
                                           transcriptionModel: "", chatModel: "grok-3-mini",
                                           needsKey: true, keyURL: "https://console.x.ai",
                                           chatModels: ["grok-3-mini", "grok-3", "grok-4"])
    public static let openrouter = ProviderPreset(id: "openrouter", name: "OpenRouter (hunderte Modelle)", baseURL: "https://openrouter.ai/api/v1",
                                                  transcriptionModel: "", chatModel: "openai/gpt-4o-mini",
                                                  needsKey: true, keyURL: "https://openrouter.ai/keys",
                                                  chatModels: ["openai/gpt-4o-mini", "google/gemini-2.5-flash", "meta-llama/llama-3.3-70b-instruct", "mistralai/mistral-small-3.2-24b-instruct"])
    public static let ollama = ProviderPreset(id: "ollama", name: "Ollama (lokal)", baseURL: "http://localhost:11434/v1",
                                              transcriptionModel: "", chatModel: "qwen2.5:7b",
                                              needsKey: false, keyURL: "https://ollama.com/download",
                                              chatModels: ["qwen2.5:7b", "llama3.1:8b", "gemma3:4b", "mistral:7b"])
    public static let lmstudio = ProviderPreset(id: "lmstudio", name: "LM Studio (lokal)", baseURL: "http://localhost:1234/v1",
                                                transcriptionModel: "", chatModel: "qwen2.5-7b-instruct",
                                                needsKey: false, keyURL: "https://lmstudio.ai",
                                                chatModels: ["qwen2.5-7b-instruct", "llama-3.1-8b-instruct", "gemma-3-4b-it"])
    public static let custom = ProviderPreset(id: "custom", name: "Eigener Server", baseURL: "", transcriptionModel: "", chatModel: "",
                                              needsKey: false, keyURL: "", transcription: true)
    public static let all: [ProviderPreset] = [.groq, .openai, .anthropic, .gemini, .mistral, .deepseek, .xai, .openrouter, .ollama, .lmstudio, .custom]
    public static func byID(_ id: String) -> ProviderPreset { all.first { $0.id == id } ?? .groq }

    /// Anbieter für die Spracherkennung bzw. für die KI-Bereinigung.
    public static func forKind(transcription: Bool) -> [ProviderPreset] { transcription ? all.filter(\.transcription) : all }

    /// Modell-Vorschläge; das aktuelle Modell steht immer mit drin.
    public func suggestions(transcription: Bool, current: String = "") -> [String] {
        let list = transcription ? transcriptionModels : chatModels
        let c = current.trimmingCharacters(in: .whitespaces)
        return c.isEmpty || list.contains(c) ? list : [c] + list
    }
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
    /// Eigene Taste (`.custom`): macOS-Tastencode (kVK_…), -1 = keine
    public var customHotkeyCode: Int = -1
    /// Anzeigename der eigenen Taste, z. B. „F13“
    public var customHotkeyName: String = ""
    public var handsFreeDoubleTap: Bool = true
    public var commandModeEnabled: Bool = true
    public var sounds: Bool = true
    public var showIdleBar: Bool = false
    public var insertMethod: InsertMethod = .paste
    public var smartSpacing: Bool = true
    public var microphoneUID: String = ""
    /// Name des gewählten Mikrofons – zum Wiederfinden, wenn es an einem anderen USB-Port oder Hub eine neue UID bekommt.
    public var microphoneName: String = ""
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
        customHotkeyCode = v(.customHotkeyCode, d.customHotkeyCode)
        customHotkeyName = v(.customHotkeyName, d.customHotkeyName)
        // Eigene Taste ohne Tastencode → Standard, sonst reagiert keine Taste
        if hotkey == .custom && customHotkeyCode < 0 { hotkey = d.hotkey }
        handsFreeDoubleTap = v(.handsFreeDoubleTap, d.handsFreeDoubleTap)
        commandModeEnabled = v(.commandModeEnabled, d.commandModeEnabled)
        sounds = v(.sounds, d.sounds)
        showIdleBar = v(.showIdleBar, d.showIdleBar)
        insertMethod = v(.insertMethod, d.insertMethod)
        smartSpacing = v(.smartSpacing, d.smartSpacing)
        microphoneUID = v(.microphoneUID, d.microphoneUID)
        microphoneName = v(.microphoneName, d.microphoneName)
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

    /// Name der Diktat-Taste für Hinweise – bei eigener Taste ihr Name.
    public var hotkeyLabel: String {
        guard hotkey == .custom else { return hotkey.label }
        return customHotkeyName.isEmpty ? "Taste \(customHotkeyCode)" : customHotkeyName
    }

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
