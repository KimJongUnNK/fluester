using System.Security.Cryptography;
using System.Text.Json;
using Fluester.Core;

namespace Fluester.Engine;

/// <summary>Neue Version auf GitHub finden und das passende Paket geprüft laden (Prüfsumme aus SHA256SUMS.txt).
/// Das Ersetzen der laufenden App macht jede Plattform selbst.</summary>
public static class Updates
{
    public const string Repo = "KimJongUnNK/fluester";

    /// <summary>Für Tests: anderer Server statt github.com (FLUESTER_UPDATE_BASE, z. B. http://127.0.0.1:8080).</summary>
    static string? TestBase => Environment.GetEnvironmentVariable("FLUESTER_UPDATE_BASE") is { Length: > 0 } b ? b.TrimEnd('/') : null;

    public static string LatestApi => TestBase is { } b ? b + "/latest.json" : $"https://api.github.com/repos/{Repo}/releases/latest";

    public static string AssetUrl(string version, string asset) =>
        TestBase is { } b ? $"{b}/{asset}" : $"https://github.com/{Repo}/releases/download/v{version}/{asset}";

    /// <summary>Version aus der Antwort von /releases/latest („v1.3.2“ → „1.3.2“), sonst null.</summary>
    public static string? ParseLatest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String)
                return null;
            var v = tag.GetString()!.Trim().TrimStart('v', 'V');
            return Version.TryParse(v, out _) ? v : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Ist <paramref name="latest"/> neuer als <paramref name="current"/>? Unlesbare Versionen zählen nie als neuer.</summary>
    public static bool IsNewer(string? latest, string? current)
    {
        if (!Version.TryParse(latest?.TrimStart('v', 'V'), out var l) || !Version.TryParse(current?.TrimStart('v', 'V'), out var c)) return false;
        return Normalize(l) > Normalize(c);
        static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    /// <summary>Prüfsumme einer Datei aus SHA256SUMS.txt („hash  Dateiname“ bzw. „hash *Dateiname“).</summary>
    public static string? ChecksumFor(string sums, string asset)
    {
        foreach (var raw in sums.Split('\n'))
        {
            var line = raw.Trim();
            int sp = line.IndexOf(' ');
            if (sp != 64) continue;
            var name = line[sp..].Trim().TrimStart('*');
            if (name == asset) return line[..64].ToLowerInvariant();
        }
        return null;
    }

    static HttpClient Client(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Fluester-Updater");
        return http;
    }

    /// <summary>Neueste veröffentlichte Version.</summary>
    public static async Task<string> LatestAsync(CancellationToken ct = default)
    {
        using var http = Client(TimeSpan.FromSeconds(10));
        string json;
        try { json = await http.GetStringAsync(LatestApi, ct); }
        catch (HttpRequestException e) { throw new FluesterException("Update-Server nicht erreichbar: " + e.Message); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new FluesterException("Update-Server antwortet nicht"); }
        return ParseLatest(json) ?? throw new FluesterException("Keine Version auf GitHub gefunden");
    }

    /// <summary>Paket <paramref name="asset"/> der Version laden, Größe und SHA-256 prüfen. Liefert den Pfad der Datei.</summary>
    public static async Task<string> DownloadAsync(string version, string asset, string folder, IProgress<double>? progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(folder);
        using var http = Client(Timeout.InfiniteTimeSpan);
        string sums;
        try { sums = await http.GetStringAsync(AssetUrl(version, "SHA256SUMS.txt"), ct); }
        catch (HttpRequestException e) { throw new FluesterException("Prüfsummen nicht ladbar: " + e.Message); }
        var expected = ChecksumFor(sums, asset) ?? throw new FluesterException($"Keine Prüfsumme für {asset} im Release");

        var dest = Path.Combine(folder, asset);
        using (var resp = await http.GetAsync(AssetUrl(version, asset), HttpCompletionOption.ResponseHeadersRead, ct))
        {
            if (!resp.IsSuccessStatusCode) throw new FluesterException($"Download fehlgeschlagen (HTTP {(int)resp.StatusCode})");
            long? total = resp.Content.Headers.ContentLength;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(dest);
            var buf = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total is > 0) progress?.Report(Math.Min(1, (double)done / total.Value));
            }
        }
        string actual;
        await using (var f = File.OpenRead(dest)) actual = Convert.ToHexString(await SHA256.HashDataAsync(f, ct)).ToLowerInvariant();
        if (actual != expected)
        {
            File.Delete(dest);
            throw new FluesterException("Download beschädigt (Prüfsumme passt nicht) – bitte erneut versuchen");
        }
        progress?.Report(1);
        return dest;
    }
}
