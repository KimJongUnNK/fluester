using Fluester.Core;

namespace Fluester.Engine;

/// <summary>Wohin der Text geht (für Stil, Prompt, Verlauf).</summary>
public sealed record TargetApp(string Name, string Id, string? WindowTitle = null)
{
    public static readonly TargetApp Unknown = new("", "");
}

/// <summary>Ergebnis eines Diktats.</summary>
public sealed record DictationResult(string Raw, string Text, string Engine, string? Warning, bool IsCommand);

/// <summary>Der komplette Ablauf ohne Systemzugriff: Erkennung → Halluzinationen raus → Bereinigung oder Command Mode.
/// Windows und Linux liefern nur Audio, Ziel-App und Markierung und fügen das Ergebnis ein.</summary>
public sealed class DictationService(ISecretStore secrets)
{
    public async Task<DictationResult?> ProcessAsync(float[] samples, Settings s, TargetApp target, bool command, string? selection, CancellationToken ct)
    {
        var audio = AudioUtil.TrimSilence(samples, AudioUtil.SampleRate);
        if (!AudioUtil.HasSpeech(audio, AudioUtil.SampleRate)) return null;
        var engine = SpeechEngines.Create(s, secrets);
        var hints = Vocabulary.Hints(s.Dictionary, s.Snippets);
        var raw = await engine.TranscribeAsync(audio, s.Language, hints, ct);
        raw = TextPipeline.StripHallucinations(raw);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        ct.ThrowIfCancellationRequested();

        if (command)
        {
            var output = await RunCommandAsync(raw, selection, target, s, ct);
            return new DictationResult(raw, output, engine.Name, null, true);
        }
        var category = AppCategorizer.Category(target.Id, target.WindowTitle, s.Styles.Overrides);
        var style = s.Styles.StyleFor(category);
        var (text, warning) = await CleanTextAsync(raw, style, target.Name, s, ct);
        return new DictationResult(raw, text, engine.Name, warning, false);
    }

    /// <summary>Snippets → Regeln → (Sprachmodell) → Regeln. Liefert Text und ggf. eine Warnung (KI ausgefallen).</summary>
    public async Task<(string Text, string? Warning)> CleanTextAsync(string raw, WritingStyle style, string appName, Settings s, CancellationToken ct = default)
    {
        var (expanded, whole) = Vocabulary.ExpandSnippets(raw, s.Snippets);
        if (whole) return (expanded, null);
        var options = CleanupOptions.From(s, style);
        if (s.Cleanup == CleanupChoice.Rules || style == WritingStyle.Raw)
            return (TextPipeline.RuleBased(expanded, options, s.Dictionary), null);

        var pre = TextPipeline.Preprocess(expanded, options);
        try
        {
            var system = Prompts.CleanupSystem(style, appName, s.LanguageName, s.Dictionary, s.ApplySelfCorrections, s.FormatLists);
            var output = await ChatAsync(s.CloudChat, system, Prompts.CleanupUser(pre), TimeSpan.FromSeconds(12), ct);
            var clean = Prompts.Sanitize(output, pre);
            if (clean == null) return (TextPipeline.RuleBased(expanded, options, s.Dictionary), "KI-Antwort verworfen – Regeln genutzt");
            return (TextPipeline.FinalizeText(clean, options, s.Dictionary), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            Log.Write("Bereinigung: " + e.Message);
            return (TextPipeline.RuleBased(expanded, options, s.Dictionary), "KI nicht erreichbar – Regeln genutzt: " + e.Message);
        }
    }

    /// <summary>Command Mode: Anweisung auf die Markierung anwenden oder ohne Markierung etwas schreiben.</summary>
    public async Task<string> RunCommandAsync(string instruction, string? selection, TargetApp target, Settings s, CancellationToken ct = default)
    {
        var system = Prompts.CommandSystem(target.Name, s.LanguageName);
        var output = await ChatAsync(s.CloudChat, system, Prompts.CommandUser(instruction, selection), TimeSpan.FromSeconds(30), ct);
        return Prompts.Sanitize(output, instruction, isCommand: true) ?? throw new FluesterException("Leere Antwort vom Sprachmodell");
    }

    async Task<string> ChatAsync(ApiConfig cfg, string system, string user, TimeSpan timeout, CancellationToken ct)
    {
        var preset = ProviderPreset.ById(cfg.ProviderId);
        var key = secrets.Get(cfg.ProviderId);
        if (preset.NeedsKey && string.IsNullOrEmpty(key))
            throw new FluesterException($"API-Schlüssel für {preset.Name} fehlt – Einstellungen › KI-Bereinigung");
        return await new OpenAICompatibleClient(cfg.BaseUrl, key, timeout).ChatAsync(cfg.Model, system, user, ct);
    }

    /// <summary>Verbindung zu einem Anbieter prüfen (Einstellungen).</summary>
    public static async Task<string> TestConnectionAsync(ApiConfig cfg, string? key, bool transcription, CancellationToken ct = default)
    {
        var client = new OpenAICompatibleClient(cfg.BaseUrl, key, TimeSpan.FromSeconds(20));
        if (transcription)
        {
            var silence = AudioUtil.Wav(new float[AudioUtil.SampleRate], AudioUtil.SampleRate);
            await client.TranscribeAsync(silence, cfg.Model, "de", null, ct);
        }
        else
        {
            await client.ChatAsync(cfg.Model, "Antworte nur mit: ok", "Test", ct);
        }
        return "✓ Verbindung steht";
    }
}
