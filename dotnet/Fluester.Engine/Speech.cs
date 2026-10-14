using System.Text;
using Fluester.Core;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace Fluester.Engine;

/// <summary>Eine Spracherkennung: Samples (16 kHz mono) → Text.</summary>
public interface ISpeechEngine
{
    string Name { get; }
    Task<string> TranscribeAsync(float[] samples, string language, IReadOnlyList<string> hints, CancellationToken ct = default);
}

public static class SpeechEngines
{
    public static ISpeechEngine Create(Settings s, ISecretStore secrets) => s.Engine switch
    {
        EngineChoice.Cloud => new CloudSpeechEngine(s.CloudTranscription, secrets.Get(s.CloudTranscription.ProviderId)),
        _ => new WhisperLocalEngine(WhisperModel.ById(s.WhisperModelId), s.UseGpu),
    };
}

/// <summary>Cloud (Groq, OpenAI, eigener Server).</summary>
public sealed class CloudSpeechEngine(ApiConfig config, string? key) : ISpeechEngine
{
    public string Name => ProviderPreset.ById(config.ProviderId).Name;

    public Task<string> TranscribeAsync(float[] samples, string language, IReadOnlyList<string> hints, CancellationToken ct = default)
    {
        var preset = ProviderPreset.ById(config.ProviderId);
        if (preset.NeedsKey && string.IsNullOrEmpty(key))
            throw new FluesterException($"API-Schlüssel für {preset.Name} fehlt – Einstellungen › Erkennung");
        var client = new OpenAICompatibleClient(config.BaseUrl, key, TimeSpan.FromSeconds(45));
        return client.TranscribeAsync(AudioUtil.Wav(samples, AudioUtil.SampleRate), config.Model, language, string.Join(", ", hints), ct);
    }
}

/// <summary>whisper.cpp direkt im Programm (Whisper.net). Das Modell bleibt nach dem ersten Diktat im Speicher.
/// Mit Grafikkarte über Vulkan (NVIDIA, AMD, Intel), sonst CPU.</summary>
public sealed class WhisperLocalEngine(WhisperModel model, bool useGpu) : ISpeechEngine
{
    static readonly SemaphoreSlim Gate = new(1, 1);
    static WhisperFactory? factory;
    static string factoryKey = "";
    static bool runtimeConfigured;

    public string Name => "Whisper " + model.Name;

    /// <summary>Welche Bibliothek tatsächlich läuft (Vulkan = Grafikkarte, Cpu = Prozessor), sobald ein Modell geladen ist.</summary>
    public static string? Backend => RuntimeOptions.LoadedLibrary?.ToString();

    /// <summary>Reihenfolge der Laufzeit-Bibliotheken – muss vor dem ersten Laden feststehen (gilt bis zum Neustart).</summary>
    public static void ConfigureRuntime(bool useGpu)
    {
        if (runtimeConfigured) return;
        runtimeConfigured = true;
        RuntimeOptions.RuntimeLibraryOrder = useGpu
            ? new List<RuntimeLibrary> { RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx }
            : new List<RuntimeLibrary> { RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx };
    }

    /// <summary>Modell vorab laden, damit das erste Diktat schnell ist.</summary>
    public async Task PrewarmAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try { EnsureFactory(); } finally { Gate.Release(); }
    }

    WhisperFactory EnsureFactory()
    {
        var path = ModelStore.PathOf(model);
        if (!File.Exists(path))
            throw new FluesterException("Whisper-Modell fehlt – Einstellungen › Erkennung › „" + model.Name + "“ laden");
        var key = path + "|" + useGpu;
        if (factory != null && factoryKey == key) return factory;
        factory?.Dispose();
        factory = null;
        ConfigureRuntime(useGpu);
        try
        {
            factory = WhisperFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = useGpu });
        }
        catch (Exception e) when (useGpu)
        {
            // Grafikkarten-Treiber zickt → ohne GPU weiter
            Log.Write("Whisper mit GPU fehlgeschlagen, nehme CPU: " + e.Message);
            factory = WhisperFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = false });
        }
        factoryKey = key;
        Log.Write($"Whisper geladen: {model.File}, Laufzeit {Backend}");
        return factory;
    }

    public static void Unload()
    {
        Gate.Wait();
        try { factory?.Dispose(); factory = null; factoryKey = ""; } finally { Gate.Release(); }
    }

    public async Task<string> TranscribeAsync(float[] samples, string language, IReadOnlyList<string> hints, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var f = EnsureFactory();
            var lang = string.IsNullOrEmpty(language) || language == "auto" ? "auto" : language[..2].ToLowerInvariant();
            var builder = f.CreateBuilder()
                .WithLanguage(lang)
                .WithThreads(Math.Clamp(Environment.ProcessorCount - 1, 2, 8))
                .WithTemperature(0f)
                .WithNoContext();
            if (hints.Count > 0) builder = builder.WithPrompt(string.Join(", ", hints));
            await using var processor = builder.Build();
            // Whisper braucht mindestens ~1 s Audio
            var input = samples.Length < AudioUtil.SampleRate ? samples.Concat(new float[AudioUtil.SampleRate - samples.Length]).ToArray() : samples;
            var sb = new StringBuilder();
            await foreach (var seg in processor.ProcessAsync(input, ct))
                sb.Append(seg.Text);
            return sb.ToString().Trim();
        }
        finally
        {
            Gate.Release();
        }
    }
}

/// <summary>Whisper-Modelle laden, prüfen, löschen.</summary>
public static class ModelStore
{
    public static string PathOf(WhisperModel m) => Path.Combine(Paths.Models, m.File);

    public static bool IsInstalled(WhisperModel m) => File.Exists(PathOf(m)) && new FileInfo(PathOf(m)).Length > 10_000_000;

    public static void Delete(WhisperModel m)
    {
        WhisperLocalEngine.Unload();
        if (File.Exists(PathOf(m))) File.Delete(PathOf(m));
    }

    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Lädt ein Modell mit Fortschritt (0…1). Erst in eine Temp-Datei, dann umbenennen – nie halbe Modelle.</summary>
    public static async Task DownloadAsync(WhisperModel m, IProgress<double>? progress, CancellationToken ct)
    {
        var dest = PathOf(m);
        var tmp = dest + ".download";
        try
        {
            using var resp = await Http.GetAsync(m.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) throw new FluesterException($"Download fehlgeschlagen (HTTP {(int)resp.StatusCode})");
            long total = resp.Content.Headers.ContentLength ?? m.SizeMB * 1_000_000L;
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(tmp))
            {
                var buf = new byte[1 << 16];
                long done = 0;
                int n;
                var lastReport = DateTime.MinValue;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 150)
                    {
                        progress?.Report(Math.Min(1, (double)done / total));
                        lastReport = DateTime.UtcNow;
                    }
                }
            }
            if (new FileInfo(tmp).Length < 10_000_000) throw new FluesterException("Download unvollständig – bitte erneut versuchen");
            WhisperLocalEngine.Unload();
            File.Move(tmp, dest, overwrite: true);
            progress?.Report(1);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
