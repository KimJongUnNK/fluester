using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fluester.Core;

/// <summary>Welche Taste das Diktat auslöst (Windows-Tastaturen melden Fn nicht ans System).</summary>
public enum HotkeyChoice { RightCtrl, CtrlWin, CapsLock, RightAlt, F8 }

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
        _ => "F8",
    };

    public static string Label(this EngineChoice e) => e switch
    {
        EngineChoice.WhisperLocal => "Whisper lokal (offline, kostenlos)",
        _ => "Cloud (Groq / OpenAI)",
    };

    public static string Label(this CleanupChoice c) => c switch
    {
        CleanupChoice.Rules => "Nur Regeln (sofort, offline)",
        _ => "Sprachmodell (Groq / OpenAI / Ollama)",
    };

    public static string Label(this InsertMethod m) => m == InsertMethod.Paste
        ? "Einfügen (schnell, Zwischenablage bleibt erhalten)"
        : "Tippen (langsamer, ohne Zwischenablage)";
}

/// <summary>Fertige Anbieter-Vorlagen für OpenAI-kompatible Schnittstellen.</summary>
public sealed record ProviderPreset(string Id, string Name, string BaseUrl, string TranscriptionModel, string ChatModel, bool NeedsKey, string KeyUrl)
{
    public static readonly ProviderPreset Groq = new("groq", "Groq (Gratis-Kontingent)", "https://api.groq.com/openai/v1",
        "whisper-large-v3-turbo", "llama-3.3-70b-versatile", true, "https://console.groq.com/keys");
    public static readonly ProviderPreset OpenAI = new("openai", "OpenAI", "https://api.openai.com/v1",
        "gpt-4o-mini-transcribe", "gpt-4o-mini", true, "https://platform.openai.com/api-keys");
    public static readonly ProviderPreset Ollama = new("ollama", "Ollama (lokal)", "http://localhost:11434/v1",
        "", "qwen2.5:7b", false, "https://ollama.com/download");
    public static readonly ProviderPreset Custom = new("custom", "Eigener Server", "", "", "", false, "");
    public static readonly ProviderPreset[] All = { Groq, OpenAI, Ollama, Custom };
    public static ProviderPreset ById(string? id) => All.FirstOrDefault(p => p.Id == id) ?? Groq;
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
    public bool HandsFreeDoubleTap { get; set; } = true;
    public bool CommandModeEnabled { get; set; } = true;
    public bool Sounds { get; set; } = true;
    public bool ShowIdleBar { get; set; }
    public InsertMethod InsertMethod { get; set; } = InsertMethod.Paste;
    public bool SmartSpacing { get; set; } = true;
    public string MicrophoneId { get; set; } = "";
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

    public static Settings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Settings();
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(json, Json) ?? new Settings();
            s.Dictionary ??= new();
            s.Snippets ??= new();
            s.Styles ??= new();
            s.Styles.Overrides ??= new();
            s.CloudTranscription ??= new Settings().CloudTranscription;
            s.CloudChat ??= new Settings().CloudChat;
            s.Language ??= "auto";
            s.MicrophoneId ??= "";
            s.PasteShortcut ??= "auto";
            return s;
        }
        catch (JsonException)
        {
            return new Settings();
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
