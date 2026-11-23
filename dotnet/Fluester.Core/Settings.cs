using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fluester.Core;

/// <summary>Welche Taste das Diktat auslöst (Windows-Tastaturen melden Fn nicht ans System).
/// <see cref="Custom"/>: frei gewählte Taste aus <see cref="Settings.CustomHotkeyCode"/>.</summary>
public enum HotkeyChoice { RightCtrl, CtrlWin, CapsLock, RightAlt, F8, Custom }

/// <summary>Spracherkennung.</summary>
public enum EngineChoice
{
    /// <summary>whisper.cpp direkt in der App (Whisper.net), offline, kostenlos.</summary>
    WhisperLocal,
    /// <summary>OpenAI-kompatible Cloud (Groq Gratis-Kontingent, OpenAI …).</summary>
    Cloud,
}

/// <summary>KI-Bereinigung nach der Erkennung.</summary>
public enum CleanupChoice
{
    /// <summary>Nur feste Regeln – sofort, offline.</summary>
    Rules,
    /// <summary>Sprachmodell über OpenAI-kompatible API (Groq, OpenAI, Ollama lokal …).</summary>
    Llm,
}

/// <summary>Wie der Text in die App kommt.</summary>
public enum InsertMethod { Paste, Type }

public static class ChoiceLabels
{
    public static string Label(this HotkeyChoice h, bool linux = false) => h switch
    {
        HotkeyChoice.RightCtrl => linux ? "Strg rechts" : "Strg rechts (empfohlen)",
        HotkeyChoice.CtrlWin => linux ? "Strg + Super" : "Strg + Win",
        HotkeyChoice.CapsLock => "Feststelltaste ⇪",
        HotkeyChoice.RightAlt => "Alt rechts / AltGr",
        HotkeyChoice.Custom => "Eigene Taste …",
        _ => "F8",
    };

    public static string Label(this EngineChoice e) => e switch
    {
        EngineChoice.WhisperLocal => "Whisper lokal (offline, kostenlos)",
        _ => "Cloud (Groq / OpenAI / Mistral …)",
    };

    public static string Label(this CleanupChoice c) => c switch
    {
        CleanupChoice.Rules => "Nur Regeln (sofort, offline)",
        _ => "Sprachmodell (Claude, GPT, Gemini, Llama …)",
    };

    public static string Label(this InsertMethod m) => m == InsertMethod.Paste
        ? "Einfügen (schnell, Zwischenablage bleibt erhalten)"
        : "Tippen (langsamer, ohne Zwischenablage)";
}

/// <summary>Fertige Anbieter-Vorlagen für OpenAI-kompatible Schnittstellen. <see cref="Transcription"/>: bietet /audio/transcriptions an.
/// Die Modell-Listen sind Vorschläge – jedes Modell des Anbieters lässt sich eintippen oder per „Modelle laden“ abrufen.</summary>
public sealed record ProviderPreset(string Id, string Name, string BaseUrl, string TranscriptionModel, string ChatModel, bool NeedsKey, string KeyUrl,
    bool Transcription = false, string[]? ChatModels = null, string[]? TranscriptionModels = null)
{
    public static readonly ProviderPreset Groq = new("groq", "Groq (Gratis-Kontingent)", "https://api.groq.com/openai/v1",
        "whisper-large-v3-turbo", "llama-3.3-70b-versatile", true, "https://console.groq.com/keys", true,
        new[] { "llama-3.3-70b-versatile", "openai/gpt-oss-120b", "openai/gpt-oss-20b", "llama-3.1-8b-instant" },
        new[] { "whisper-large-v3-turbo", "whisper-large-v3" });
    public static readonly ProviderPreset OpenAI = new("openai", "OpenAI (GPT)", "https://api.openai.com/v1",
        "gpt-4o-mini-transcribe", "gpt-4o-mini", true, "https://platform.openai.com/api-keys", true,
        new[] { "gpt-4o-mini", "gpt-4.1-mini", "gpt-4.1", "gpt-4o", "gpt-5-mini", "gpt-5" },
        new[] { "gpt-4o-mini-transcribe", "gpt-4o-transcribe", "whisper-1" });
    public static readonly ProviderPreset Anthropic = new("anthropic", "Anthropic (Claude)", "https://api.anthropic.com/v1",
        "", "claude-haiku-5-5", true, "https://console.anthropic.com/settings/keys", false,
        new[] { "claude-haiku-5-5", "claude-sonnet-5-5", "claude-opus-5-5" });
    public static readonly ProviderPreset Gemini = new("gemini", "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai",
        "", "gemini-2.5-flash", true, "https://aistudio.google.com/apikey", false,
        new[] { "gemini-2.5-flash", "gemini-2.5-flash-lite", "gemini-2.5-pro" });
    public static readonly ProviderPreset Mistral = new("mistral", "Mistral AI", "https://api.mistral.ai/v1",
        "", "mistral-small-latest", true, "https://console.mistral.ai/api-keys", false,
        new[] { "mistral-small-latest", "mistral-medium-latest", "mistral-large-latest", "ministral-8b-latest" });
    public static readonly ProviderPreset DeepSeek = new("deepseek", "DeepSeek", "https://api.deepseek.com/v1",
        "", "deepseek-chat", true, "https://platform.deepseek.com/api_keys", false,
        new[] { "deepseek-chat", "deepseek-reasoner" });
    public static readonly ProviderPreset XAI = new("xai", "xAI (Grok)", "https://api.x.ai/v1",
        "", "grok-3-mini", true, "https://console.x.ai", false,
        new[] { "grok-3-mini", "grok-3", "grok-4" });
    public static readonly ProviderPreset OpenRouter = new("openrouter", "OpenRouter (hunderte Modelle)", "https://openrouter.ai/api/v1",
        "", "openai/gpt-4o-mini", true, "https://openrouter.ai/keys", false,
        new[] { "openai/gpt-4o-mini", "google/gemini-2.5-flash", "meta-llama/llama-3.3-70b-instruct", "mistralai/mistral-small-3.2-24b-instruct" });
    public static readonly ProviderPreset Ollama = new("ollama", "Ollama (lokal)", "http://localhost:11434/v1",
        "", "qwen2.5:7b", false, "https://ollama.com/download", false,
        new[] { "qwen2.5:7b", "llama3.1:8b", "gemma3:4b", "mistral:7b" });
    public static readonly ProviderPreset LmStudio = new("lmstudio", "LM Studio (lokal)", "http://localhost:1234/v1",
        "", "qwen2.5-7b-instruct", false, "https://lmstudio.ai", false,
        new[] { "qwen2.5-7b-instruct", "llama-3.1-8b-instruct", "gemma-3-4b-it" });
    public static readonly ProviderPreset Custom = new("custom", "Eigener Server", "", "", "", false, "", true);
    public static readonly ProviderPreset[] All = { Groq, OpenAI, Anthropic, Gemini, Mistral, DeepSeek, XAI, OpenRouter, Ollama, LmStudio, Custom };
    public static ProviderPreset ById(string? id) => All.FirstOrDefault(p => p.Id == id) ?? Groq;

    /// <summary>Anbieter für die Spracherkennung bzw. für die KI-Bereinigung.</summary>
    public static ProviderPreset[] For(bool transcription) => transcription ? All.Where(p => p.Transcription).ToArray() : All;

    /// <summary>Modell-Vorschläge; das aktuelle Modell steht immer mit drin.</summary>
    public string[] Suggestions(bool transcription, string? current = null)
    {
        var list = (transcription ? TranscriptionModels : ChatModels) ?? Array.Empty<string>();
        return string.IsNullOrWhiteSpace(current) || list.Contains(current) ? list : list.Prepend(current).ToArray();
    }
}

/// <summary>Verbindung zu einer OpenAI-kompatiblen API. Der Schlüssel liegt verschlüsselt separat, nicht hier.</summary>
public sealed class ApiConfig
{
    public string ProviderId { get; set; } = "groq";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";

    public ApiConfig() { }
    public ApiConfig(string providerId, string baseUrl, string model) { ProviderId = providerId; BaseUrl = baseUrl; Model = model; }
}

/// <summary>Lokales Whisper-Modell (ggml-Format von whisper.cpp).</summary>
public sealed record WhisperModel(string Id, string Name, string File, int SizeMB, string Note)
{
    public const string DefaultSource = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main";

    /// <summary>Download-Adresse. FLUESTER_MODEL_MIRROR ersetzt Hugging Face (Spiegel hinter Firmen-Firewalls, z. B. https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main; Tests).</summary>
    public Uri Url => new($"{(Environment.GetEnvironmentVariable("FLUESTER_MODEL_MIRROR") is { Length: > 0 } m ? m.TrimEnd('/') : DefaultSource)}/{File}");

    public static readonly WhisperModel[] All =
    {
        new("turbo-q5", "Large v3 Turbo (empfohlen)", "ggml-large-v3-turbo-q5_0.bin", 547, "Beste Qualität für Deutsch und Englisch – flott mit Grafikkarte"),
        new("turbo", "Large v3 Turbo (voll)", "ggml-large-v3-turbo.bin", 1620, "Minimal genauer, braucht mehr Speicher"),
        new("small", "Small", "ggml-small.bin", 466, "Für Rechner ohne starke Grafikkarte"),
        new("base", "Base", "ggml-base.bin", 142, "Sehr schnell, deutlich ungenauer"),
    };

    public static WhisperModel ById(string? id) => All.FirstOrDefault(m => m.Id == id) ?? All[0];
}

public sealed record LanguageChoice(string Code, string Name)
{
    public static readonly LanguageChoice[] All =
    {
        new("auto", "Automatisch"), new("de-DE", "Deutsch (Deutschland)"), new("de-AT", "Deutsch (Österreich)"), new("de-CH", "Deutsch (Schweiz)"),
        new("en-US", "Englisch (USA)"), new("en-GB", "Englisch (UK)"), new("fr-FR", "Französisch"), new("es-ES", "Spanisch"), new("it-IT", "Italienisch"),
        new("nl-NL", "Niederländisch"), new("pt-BR", "Portugiesisch"), new("pl-PL", "Polnisch"), new("tr-TR", "Türkisch"),
    };
}

/// <summary>Alle Einstellungen, als JSON gespeichert. Fehlende oder unbekannte Felder fallen auf Standardwerte zurück.</summary>
public sealed class Settings
{
    public const string AutoLanguageName = "automatisch erkannt";

    // Bedienung
    public HotkeyChoice Hotkey { get; set; } = HotkeyChoice.RightCtrl;
    /// <summary>Eigene Taste (<see cref="HotkeyChoice.Custom"/>): Windows virtueller Tastencode, Linux evdev-Code.</summary>
    public int CustomHotkeyCode { get; set; }
    /// <summary>Anzeigename der eigenen Taste, z. B. „F13“ oder „Pause“.</summary>
    public string CustomHotkeyName { get; set; } = "";
    public bool HandsFreeDoubleTap { get; set; } = true;
    public bool CommandModeEnabled { get; set; } = true;
    public bool Sounds { get; set; } = true;
    public bool ShowIdleBar { get; set; }
    public InsertMethod InsertMethod { get; set; } = InsertMethod.Paste;
    public bool SmartSpacing { get; set; } = true;
    public string MicrophoneId { get; set; } = "";
    /// <summary>Name des gewählten Mikrofons – zum Wiederfinden, wenn es an einem anderen USB-Port oder Hub eine neue Id bekommt.</summary>
    public string MicrophoneName { get; set; } = "";
    public bool LaunchAtLogin { get; set; } = true;
    public bool OnboardingDone { get; set; }
    public bool KeepHistory { get; set; } = true;
    /// <summary>Linux: Tastenkürzel zum Einfügen – „auto“ (Strg+Umschalt+V in Terminals, sonst Strg+V), „ctrl+v“, „ctrl+shift+v“, „shift+insert“.</summary>
    public string PasteShortcut { get; set; } = "auto";
    /// <summary>Linux: Desktop-Benachrichtigungen bei Aufnahme und Ergebnis.</summary>
    public bool Notifications { get; set; } = true;

    // Erkennung
    public EngineChoice Engine { get; set; } = EngineChoice.WhisperLocal;
    /// <summary>„auto“ oder BCP-47-Code, z. B. „de-DE“.</summary>
    public string Language { get; set; } = "auto";
    public string WhisperModelId { get; set; } = "turbo-q5";
    public bool UseGpu { get; set; } = true;
    public ApiConfig CloudTranscription { get; set; } = new("groq", ProviderPreset.Groq.BaseUrl, ProviderPreset.Groq.TranscriptionModel);

    // Bereinigung
    public CleanupChoice Cleanup { get; set; } = CleanupChoice.Rules;
    public ApiConfig CloudChat { get; set; } = new("groq", ProviderPreset.Groq.BaseUrl, ProviderPreset.Groq.ChatModel);
    public bool RemoveFillers { get; set; } = true;
    public bool ApplySelfCorrections { get; set; } = true;
    public bool FormatLists { get; set; } = true;
    public bool SpokenCommands { get; set; } = true;
    public bool SpokenPunctuation { get; set; }

    // Persönlich
    public List<DictionaryEntry> Dictionary { get; set; } = new();
    public List<Snippet> Snippets { get; set; } = new();
    public StyleSettings Styles { get; set; } = new();

    /// <summary>Name der Diktat-Taste für Hinweise – bei eigener Taste ihr Name.</summary>
    public string HotkeyLabel(bool linux = false) =>
        Hotkey == HotkeyChoice.Custom ? (CustomHotkeyName.Length > 0 ? CustomHotkeyName : $"Taste {CustomHotkeyCode}") : Hotkey.Label(linux);

    /// <summary>Sprache als Klartext für Prompts.</summary>
    [JsonIgnore]
    public string LanguageName => (Language.Length >= 2 ? Language[..2] : Language) switch
    {
        "de" => "Deutsch",
        "en" => "Englisch",
        "fr" => "Französisch",
        "es" => "Spanisch",
        "it" => "Italienisch",
        _ => AutoLanguageName,
    };

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new LenientEnumConverterFactory() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static Settings FromJson(string? json) => TryFromJson(json, out var s) ? s : new Settings();

    /// <summary>false, wenn die Datei nicht lesbar ist (z. B. von Hand kaputt bearbeitet) – dann sind es die Standardwerte,
    /// und der Aufrufer sichert die alte Datei, bevor sie überschrieben wird.</summary>
    public static bool TryFromJson(string? json, out Settings settings)
    {
        settings = new Settings();
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(json, Json) ?? new Settings();
            // Einzelne kaputte Einträge („null“) nicht bis zum Absturz mitschleppen
            s.Dictionary = (s.Dictionary ?? new()).Where(d => d != null).ToList();
            s.Snippets = (s.Snippets ?? new()).Where(n => n != null).ToList();
            foreach (var d in s.Dictionary) { d.Term ??= ""; d.SoundsLike = (d.SoundsLike ?? new()).Where(v => v != null).ToList(); }
            foreach (var n in s.Snippets) { n.Trigger ??= ""; n.Text ??= ""; }
            s.Styles ??= new();
            s.Styles.Overrides ??= new();
            s.CloudTranscription ??= new Settings().CloudTranscription;
            s.CloudChat ??= new Settings().CloudChat;
            s.Language ??= "auto";
            s.MicrophoneId ??= "";
            s.MicrophoneName ??= "";
            s.PasteShortcut ??= "auto";
            s.WhisperModelId ??= "turbo-q5";
            s.CustomHotkeyName ??= "";
            // Eigene Taste ohne Tastencode (Datei von Hand bearbeitet) → Standard, sonst reagiert keine Taste
            if (s.Hotkey == HotkeyChoice.Custom && s.CustomHotkeyCode <= 0) s.Hotkey = HotkeyChoice.RightCtrl;
            s.CloudTranscription.BaseUrl ??= "";
            s.CloudTranscription.Model ??= "";
            s.CloudTranscription.ProviderId ??= "groq";
            s.CloudChat.BaseUrl ??= "";
            s.CloudChat.Model ??= "";
            s.CloudChat.ProviderId ??= "groq";
            settings = s;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public Settings Clone() => FromJson(ToJson());
}

/// <summary>Enums als Text speichern; unbekannte Werte (z. B. aus einer neueren Version) werden zum Standardwert statt zum Fehler.</summary>
public sealed class LenientEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type t) => t.IsEnum;

    public override JsonConverter CreateConverter(Type t, JsonSerializerOptions o) =>
        (JsonConverter)Activator.CreateInstance(typeof(LenientEnumConverter<>).MakeGenericType(t))!;

    sealed class LenientEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
        {
            if (r.TokenType == JsonTokenType.String && Enum.TryParse<T>(r.GetString(), true, out var v)) return v;
            if (r.TokenType == JsonTokenType.Number && r.TryGetInt32(out var n) && Enum.IsDefined(typeof(T), n)) return (T)(object)n;
            return default;
        }

        public override void Write(Utf8JsonWriter w, T value, JsonSerializerOptions o) =>
            w.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));

        public override T ReadAsPropertyName(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
            Enum.TryParse<T>(r.GetString(), true, out var v) ? v : default;

        public override void WriteAsPropertyName(Utf8JsonWriter w, T value, JsonSerializerOptions o) =>
            w.WritePropertyName(value.ToString());
    }
}
