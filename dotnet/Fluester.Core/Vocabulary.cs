using System.Text;
using System.Text.RegularExpressions;

namespace Fluester.Core;

/// <summary>Ein Wort im persönlichen Wörterbuch: richtige Schreibweise plus Varianten, wie die Erkennung es oft verhört.</summary>
public sealed class DictionaryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>So soll es geschrieben werden, z. B. „Voltage Events“.</summary>
    public string Term { get; set; } = "";
    /// <summary>So kommt es aus der Erkennung, z. B. „voltage event's“.</summary>
    public List<string> SoundsLike { get; set; } = new();

    public DictionaryEntry() { }
    public DictionaryEntry(string term, params string[] soundsLike) { Term = term; SoundsLike = soundsLike.ToList(); }
}

/// <summary>Textbaustein: gesprochenes Stichwort wird zu einem längeren Text.</summary>
public sealed class Snippet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Trigger { get; set; } = "";
    public string Text { get; set; } = "";

    public Snippet() { }
    public Snippet(string trigger, string text) { Trigger = trigger; Text = text; }
}

public static class Vocabulary
{
    /// <summary>Wörter, die der Erkennung als Kontext mitgegeben werden (Namen, Fachbegriffe).</summary>
    public static List<string> Hints(IEnumerable<DictionaryEntry> entries, IEnumerable<Snippet>? snippets = null, int limit = 80)
    {
        var seen = new HashSet<string>();
        var output = new List<string>();
        foreach (var t in entries.Select(e => e.Term).Concat((snippets ?? Enumerable.Empty<Snippet>()).Select(s => s.Trigger)))
        {
            var s = (t ?? "").Trim();
            if (s.Length == 0 || !seen.Add(s.ToLowerInvariant())) continue;
            output.Add(s);
            if (output.Count >= limit) break;
        }
        return output;
    }

    /// <summary>Ersetzt Varianten und falsche Groß-/Kleinschreibung durch die Wörterbuch-Schreibweise. Längste Varianten zuerst.</summary>
    public static string ApplyDictionary(string text, IEnumerable<DictionaryEntry> entries)
    {
        var pairs = new List<(string From, string To)>();
        foreach (var e in entries)
        {
            var term = (e.Term ?? "").Trim();
            if (term.Length == 0) continue;
            pairs.Add((term, term));
            foreach (var v in e.SoundsLike ?? new())
            {
                var s = (v ?? "").Trim();
                if (s.Length > 0) pairs.Add((s, term));
            }
        }
        var result = text;
        foreach (var (from, to) in pairs.OrderByDescending(p => p.From.Length))
            result = ReplaceWholeWords(result, from, to);
        return result;
    }

    /// <summary>Für den Vergleich mit Stichworten: klein, ohne Satzzeichen, einfache Leerzeichen.</summary>
    public static string Normalize(string s)
    {
        var sb = new StringBuilder();
        bool lastSpace = true;
        foreach (var ch in s.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); lastSpace = false; }
            else if (!lastSpace) { sb.Append(' '); lastSpace = true; }
        }
        return Text.TrimH(sb.ToString());
    }

    /// <summary>Ganze Aussage ist ein Stichwort → nur der Baustein. Sonst Stichwort im Satz ersetzen.</summary>
    public static (string Text, bool MatchedWhole) ExpandSnippets(string text, IEnumerable<Snippet> snippets)
    {
        var valid = snippets.Where(s => Normalize(s.Trigger).Length > 0).ToList();
        if (valid.Count == 0) return (text, false);
        var norm = Normalize(text);
        var stripped = norm;
        foreach (var f in new[] { "snippet ", "baustein ", "füge ", "einfügen ", "insert ", "paste " })
            if (stripped.StartsWith(f)) stripped = stripped[f.Length..];
        foreach (var suffix in new[] { " ein", " einfügen", " please", " bitte" })
            if (stripped.EndsWith(suffix)) stripped = stripped[..^suffix.Length];
        var whole = valid.FirstOrDefault(s => Normalize(s.Trigger) == norm || Normalize(s.Trigger) == stripped);
        if (whole != null) return (whole.Text, true);
        var result = text;
        foreach (var s in valid.OrderByDescending(s => s.Trigger.Length))
            result = ReplaceWholeWords(result, s.Trigger, s.Text);
        return (result, false);
    }

    /// <summary>Ersetzt <paramref name="find"/> als ganzes Wort bzw. ganze Wortfolge (Groß/klein egal).</summary>
    public static string ReplaceWholeWords(string text, string find, string replacement)
    {
        var words = Text.Words(find).Select(Regex.Escape).ToArray();
        if (words.Length == 0) return text;
        var pattern = @"(?<![\p{L}\p{N}])" + string.Join(@"\s+", words) + @"(?![\p{L}\p{N}])";
        return Text.Replace(text, pattern, Text.EscapeReplacement(replacement));
    }
}
