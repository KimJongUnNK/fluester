using System.Text.Json;

namespace Fluester.Engine;

/// <summary>Ein Mikrofon: <see cref="Id"/> versteht das Aufnahme-Werkzeug, <see cref="Name"/> ist für Menschen.</summary>
public sealed record Microphone(string Id, string Name, bool Muted = false, bool IsDefault = false);

/// <summary>Liest Mikrofone aus der Ausgabe von <c>pactl</c>, <c>pw-dump</c> und <c>arecord</c> (Linux).
/// Reine Textverarbeitung, daher auf jedem System testbar.</summary>
public static class MicrophoneParser
{
    /// <summary><c>LC_ALL=C pactl list sources</c> – ohne Monitore (das wäre der Ton der Lautsprecher).</summary>
    public static List<Microphone> Pactl(string text, string? defaultId = null)
    {
        var list = new List<Microphone>();
        string? name = null, desc = null, monitorOf = null, cls = null;
        bool muted = false;
        void Flush()
        {
            if (name != null && (monitorOf is null or "n/a") && cls != "monitor" && !name.EndsWith(".monitor"))
                list.Add(new Microphone(name, string.IsNullOrWhiteSpace(desc) ? name : desc, muted, name == defaultId));
            name = desc = monitorOf = cls = null;
            muted = false;
        }
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("Source #")) { Flush(); continue; }
            var t = line.Trim();
            if (t.StartsWith("Name: ")) name = t[6..].Trim();
            else if (t.StartsWith("Description: ")) desc = t[13..].Trim();
            else if (t.StartsWith("Monitor of Sink: ")) monitorOf = t[17..].Trim();
            else if (t.StartsWith("Mute: ")) muted = t[6..].Trim() == "yes";
            else if (t.StartsWith("device.class = ")) cls = t[15..].Trim().Trim('"');
        }
        Flush();
        return list;
    }

    /// <summary>Standard-Quelle aus <c>LC_ALL=C pactl info</c>.</summary>
    public static string? PactlDefault(string info) =>
        info.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("Default Source: "))?[16..].Trim() is { Length: > 0 } s ? s : null;

    /// <summary><c>pw-dump</c> (JSON): alle Knoten der Klasse Audio/Source…, Standard aus den Metadaten.</summary>
    public static List<Microphone> PwDump(string json)
    {
        var list = new List<Microphone>();
        string? def = null;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return list; }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var o in doc.RootElement.EnumerateArray())
            {
                if (o.ValueKind != JsonValueKind.Object) continue;
                var type = o.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                if (type == "PipeWire:Interface:Metadata" && o.TryGetProperty("metadata", out var md) && md.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in md.EnumerateArray())
                        if (Str(m, "key") == "default.audio.source" && m.TryGetProperty("value", out var v))
                            def = v.ValueKind == JsonValueKind.Object ? Str(v, "name") : def;
                    continue;
                }
                if (type != "PipeWire:Interface:Node" || !o.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object
                    || !info.TryGetProperty("props", out var props) || props.ValueKind != JsonValueKind.Object) continue;
                if (Str(props, "media.class") is not { } cls || !cls.StartsWith("Audio/Source")) continue;
                if (Str(props, "node.name") is not { } id) continue;
                list.Add(new Microphone(id, Str(props, "node.description") ?? Str(props, "node.nick") ?? id));
            }
        }
        return list.Select(m => m with { IsDefault = m.Id == def }).ToList();
    }

    static string? Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary><c>LC_ALL=C arecord -L</c>: nur <c>plughw:</c>-Geräte (wandeln selbst auf 16 kHz um).
    /// Der Systemstandard läuft ohnehin über „default“.</summary>
    public static List<Microphone> Arecord(string text)
    {
        var list = new List<Microphone>();
        string? name = null, desc = null;
        void Flush()
        {
            if (name != null && name.StartsWith("plughw:")) list.Add(new Microphone(name, desc ?? name));
            name = desc = null;
        }
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0) continue;
            if (!char.IsWhiteSpace(line[0])) { Flush(); name = line.Trim(); }
            else desc ??= line.Trim();
        }
        Flush();
        return list;
    }

    /// <summary>Mikrofone zur Eingabe: Nummer (1-basiert), genaue Id oder Teil von Name/Id (ohne Groß-/Kleinschreibung).</summary>
    public static List<Microphone> Matches(IReadOnlyList<Microphone> mics, string query)
    {
        query = query.Trim();
        if (query.Length == 0) return new();
        if (int.TryParse(query, out var n)) return n >= 1 && n <= mics.Count ? new() { mics[n - 1] } : new();
        if (mics.FirstOrDefault(m => m.Id == query) is { } exact) return new() { exact };
        return mics.Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || m.Id.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
