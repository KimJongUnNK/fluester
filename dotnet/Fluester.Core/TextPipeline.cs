using System.Text;
using System.Text.RegularExpressions;

namespace Fluester.Core;

/// <summary>Optionen für die regelbasierte Bereinigung.</summary>
public sealed record CleanupOptions(
    bool RemoveFillers = true,
    bool SelfCorrections = true,
    bool SpokenCommands = true,
    bool SpokenPunctuation = false,
    bool FormatLists = true,
    WritingStyle Style = WritingStyle.Formal)
{
    public static CleanupOptions From(Settings s, WritingStyle style) =>
        new(s.RemoveFillers, s.ApplySelfCorrections, s.SpokenCommands, s.SpokenPunctuation, s.FormatLists, style);
}

/// <summary>Regelbasierte Textbereinigung – gleiches Verhalten wie FluesterCore (Swift) auf dem Mac.
/// Läuft immer: vor dem Sprachmodell (Füllwörter, Befehle) und danach (Wörterbuch, Leerzeichen, Stil).</summary>
public static class TextPipeline
{
    // ───────────────────────── Gesamtablauf

    public static string RuleBased(string raw, CleanupOptions options, IEnumerable<DictionaryEntry>? dictionary = null)
    {
        var t = Preprocess(raw, options);
        if (options.SelfCorrections && options.Style != WritingStyle.Raw) t = ApplySelfCorrections(t);
        if (options.FormatLists && options.Style != WritingStyle.Raw) t = FormatLists(t);
        return FinalizeText(t, options, dictionary);
    }

    /// <summary>Vor dem Sprachmodell: was Regeln sicher können.</summary>
    public static string Preprocess(string raw, CleanupOptions options)
    {
        var t = raw.Replace("\r\n", "\n");
        t = CollapseSpaces(t);
        if (options.SpokenCommands) t = ApplySpokenCommands(t);
        if (options.SpokenPunctuation) t = ApplySpokenPunctuation(t);
        if (options.RemoveFillers)
        {
            t = RemoveFillers(t);
            t = RemoveStutters(t);
        }
        return FixSpacing(t);
    }

    /// <summary>Nach dem Sprachmodell: Wörterbuch, Leerzeichen, Großschreibung, Stil.</summary>
    public static string FinalizeText(string text, CleanupOptions options, IEnumerable<DictionaryEntry>? dictionary = null)
    {
        var t = Vocabulary.ApplyDictionary(text, dictionary ?? Enumerable.Empty<DictionaryEntry>());
        t = FixSpacing(t);
        switch (options.Style)
        {
            case WritingStyle.Raw:
                t = CapitalizeSentences(t);
                break;
            case WritingStyle.Formal:
            case WritingStyle.Excited:
                t = CapitalizeSentences(t);
                t = EnsureFinalPunctuation(t);
                break;
            case WritingStyle.Casual:
                t = CapitalizeSentences(t);
                t = DropFinalPeriod(t);
                break;
            case WritingStyle.VeryCasual:
                t = DropFinalPeriod(t).ToLowerInvariant();
                break;
        }
        return t.Trim();
    }

    // ───────────────────────── Whisper-Halluzinationen

    static readonly string[] Hallucinations =
    {
        "untertitel im auftrag des zdf", "untertitelung des zdf", "untertitel von stephanie geiges", "untertitelung im auftrag des zdf",
        "vielen dank fürs zuschauen", "danke fürs zuschauen", "bis zum nächsten mal", "copyright wdr", "swr 2020", "amara.org",
        "thank you for watching", "thanks for watching", "please subscribe", "subtitles by the amara.org community",
    };

    public static string StripHallucinations(string text)
    {
        var t = Text.Replace(text, @"\[[^\]]{1,40}\]|\((?:musik|music|applaus|applause|lachen|laughter|stille|silence|räuspern|seufzen)[^)]{0,20}\)|\*[^*]{1,30}\*", " ");
        var low = t.ToLowerInvariant();
        foreach (var h in Hallucinations)
        {
            if (!low.Contains(h)) continue;
            // Nur entfernen, wenn der Satz allein steht (nicht mitten in echtem Inhalt)
            t = Text.Replace(t, @"(?:^|(?<=[.!?]))\s*[^.!?]*" + Regex.Escape(h) + @"[^.!?]*[.!?]?", " ");
        }
        return CollapseSpaces(t).Trim();
    }

    // ───────────────────────── Füllwörter und Stotterer

    /// <summary>Reine Verlegenheitslaute, die nie Inhalt sind.</summary>
    const string Hesitation = "(?:ä+h+m*|ö+h+m*|e+h+m+|u+h+m+|u+m{2,}|u+h{2,}|e+r+m+|h+m{2,}|ä+m+)";
    /// <summary>Mehrdeutig (dt. „um“ = Präposition): nur wenn mit Komma/Auslassung abgesetzt.</summary>
    const string Ambiguous = "(?:uh|um|hm|eh)";

    static readonly HashSet<string> CommaKeepers = new()
    {
        "dass", "weil", "wenn", "ob", "obwohl", "damit", "sodass", "bevor", "nachdem", "während", "falls",
        "sobald", "aber", "sondern", "denn", "doch", "jedoch", "als", "wie", "welche", "welcher", "welches",
        "but", "which", "who", "because",
    };

    public static string RemoveFillers(string text)
    {
        var t = ReplaceFiller(text, Hesitation, requireDelimiter: false);
        return ReplaceFiller(t, Ambiguous, requireDelimiter: true);
    }

    static string ReplaceFiller(string text, string core, bool requireDelimiter)
    {
        var after = requireDelimiter ? @"(?:[ \t]*(?:,|…|\.\.\.))" : @"(?:[ \t]*(?:,|…|\.\.\.))?";
        var re = Text.Re(@"([ \t]*,)?[ \t]*(?<![\p{L}\p{N}])" + core + @"(?![\p{L}\p{N}])" + after);
        var sb = new StringBuilder();
        int last = 0;
        foreach (Match m in re.Matches(text))
        {
            bool hadComma = m.Groups[1].Success;
            if (requireDelimiter)
            {
                var before = text[..m.Index];
                var bt = Text.TrimH(before);
                bool atStart = bt.Length == 0 || (before.Length > 0 && before[^1] == '\n') || ".!?".Contains(bt.Length > 0 ? bt[^1] : ' ');
                if (!hadComma && !atStart) continue;
            }
            sb.Append(text, last, m.Index - last);
            int end = m.Index + m.Length;
            int k = end;
            while (k < text.Length && char.IsWhiteSpace(text[k])) k++;
            int w = k;
            while (w < text.Length && char.IsLetter(text[w])) w++;
            var nextWord = text[k..w].ToLowerInvariant();
            sb.Append(hadComma && CommaKeepers.Contains(nextWord) ? ", " : " ");
            last = end;
        }
        sb.Append(text, last, text.Length - last);
        return FixSpacing(sb.ToString());
    }

    /// <summary>Wörter, deren Verdopplung oft korrekt ist („die die“, „ja ja“).</summary>
    static readonly HashSet<string> DoubleOk = new()
    {
        "der", "die", "das", "den", "dem", "des", "had", "that", "sie", "is", "es", "ja", "nein", "no", "so",
        "na", "ha", "haha", "bye", "very", "sehr", "yeah", "tschüss", "danke", "bitte", "gut", "mal",
    };

    /// <summary>Stotterer: „ich ich wollte“ → „ich wollte“. Artikel erst ab dreifacher Wiederholung.</summary>
    public static string RemoveStutters(string text)
    {
        var re = Text.Re(@"(?<![\p{L}\p{N}])(\p{L}+)((?:[ ,]+\1)+)(?![\p{L}\p{N}])");
        var sb = new StringBuilder();
        int last = 0;
        foreach (Match m in re.Matches(text))
        {
            var word = m.Groups[1].Value;
            int repeats = m.Groups[2].Value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (DoubleOk.Contains(word.ToLowerInvariant()) && repeats < 2) continue;
            sb.Append(text, last, m.Index - last);
            sb.Append(word);
            last = m.Index + m.Length;
        }
        sb.Append(text, last, text.Length - last);
        return sb.ToString();
    }

    // ───────────────────────── Gesprochene Befehle

    public static string ApplySpokenCommands(string text)
    {
        const string paragraph = @"(?:neuer|neuen|nächster|nächsten)\s+absatz|new\s+paragraph|next\s+paragraph";
        const string line = @"(?:neue|nächste)\s+zeile|zeilenumbruch|new\s+line|next\s+line|line\s+break";
        var t = Text.Replace(text, @"[ \t]*(?:[,;:][ \t]*)?(?<![\p{L}])(?:" + paragraph + @")(?![\p{L}])[.,;:!]?[ \t]*", "\n\n");
        return Text.Replace(t, @"[ \t]*(?:[,;:][ \t]*)?(?<![\p{L}])(?:" + line + @")(?![\p{L}])[.,;:!]?[ \t]*", "\n");
    }

    static readonly (string Word, string Sign)[] PunctuationWords =
    {
        ("fragezeichen", "?"), ("ausrufezeichen", "!"), ("doppelpunkt", ":"), ("semikolon", ";"), ("strichpunkt", ";"),
        ("komma", ","), ("punkt", "."), ("bindestrich", "-"), ("gedankenstrich", " –"), ("klammer auf", " ("), ("klammer zu", ")"),
        ("question mark", "?"), ("exclamation mark", "!"), ("exclamation point", "!"), ("full stop", "."), ("period", "."),
        ("comma", ","), ("colon", ":"), ("semicolon", ";"), ("open paren", " ("), ("close paren", ")"), ("dash", " –"),
    };

    /// <summary>„Hallo Komma wie geht's Fragezeichen“ → „Hallo, wie geht's?“ (nur wenn eingeschaltet).</summary>
    public static string ApplySpokenPunctuation(string text)
    {
        var t = text;
        foreach (var (word, sign) in PunctuationWords)
        {
            var w = string.Join(@"\s+", word.Split(' ').Select(Regex.Escape));
            t = Text.Replace(t, @"[ \t]*[,.]?[ \t]*(?<![\p{L}])" + w + @"(?![\p{L}])[,.]?", Text.EscapeReplacement(sign));
        }
        return FixSpacing(t);
    }

    // ───────────────────────── Selbstkorrekturen

    static readonly string[] CorrectionMarkers =
    {
        "nein, ich meine", "nein ich meine", "nein, warte", "nein warte", "ich meine", "ich mein", "ich meinte", "besser gesagt",
        "oder besser", "korrigiere", "korrektur", "sorry, ich meine", "pardon",
        "no, i mean", "no wait", "no, wait", "i mean", "i meant", "sorry, i meant", "or rather", "correction",
    };

    static readonly string[] ScratchMarkers = { "streich das", "vergiss das", "lösch das", "scratch that", "forget that", "delete that" };

    public static string ApplySelfCorrections(string text)
    {
        var t = text;
        for (int i = 0; i < 8; i++)
        {
            var next = CorrectOnce(t);
            if (next == null) break;
            t = next;
        }
        return FixSpacing(t);
    }

    static bool IsSentenceEnd(char c) => c is '.' or '!' or '?' or '\n';

    static string? CorrectOnce(string text)
    {
        var lower = text.ToLowerInvariant();
        // 1) „streich das“
        foreach (var marker in ScratchMarkers)
        {
            int lo = lower.IndexOf(marker, StringComparison.Ordinal);
            if (lo < 0) continue;
            int hi = lo + marker.Length;
            bool startOk = lo == 0 || !char.IsLetter(text[lo - 1]);
            bool endOk = hi == text.Length || !char.IsLetter(text[hi]);
            if (!startOk || !endOk) continue;
            var before = Text.TrimH(text[..lo]);
            var after = new string(text[hi..].SkipWhile(c => ".,;:!?… ".Contains(c)).ToArray());
            bool endsSentence = before.Length == 0 || IsSentenceEnd(before[^1]);
            if (!endsSentence)
            {
                // mitten im Satz: alles ab Satzanfang fällt weg
                int cut = LastIndexWhere(before, IsSentenceEnd);
                before = cut >= 0 ? before[..(cut + 1)] : "";
            }
            else
            {
                // eigener Satz: vorherigen Satz entfernen
                var b = before;
                if (b.Length > 0 && ".!?".Contains(b[^1])) b = b[..^1];
                int cut = LastIndexWhere(b, IsSentenceEnd);
                before = cut >= 0 ? b[..(cut + 1)] : "";
            }
            return Text.TrimH(before + " " + after);
        }
        // 2) „…, ich meine …“
        foreach (var marker in CorrectionMarkers)
        {
            var body = string.Join(@"\s+", marker.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));
            var match = Text.Re(@",\s*" + body + @"(?![\p{L}])\s*,?\s*").Match(text);
            if (!match.Success) continue;
            var left = text[..match.Index];
            var right = text[(match.Index + match.Length)..];
            int corrEnd = IndexWhere(right, c => ".!?;,\n".Contains(c));
            var correction = Text.TrimH(corrEnd >= 0 ? right[..corrEnd] : right);
            var corrWords = correction.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (corrWords.Length == 0) continue;
            var first = corrWords[0].ToLowerInvariant();
            int sentenceStart = LastIndexWhere(left, IsSentenceEnd) + 1;
            var head = left[..sentenceStart];
            var sentence = left[sentenceStart..];
            var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (words.Count == 0) continue;
            // Anker: gleiches Anfangswort in den letzten 6 Wörtern („um 3, ich meine um 4“)
            int? cutAt = null;
            for (int i = words.Count - 1; i >= Math.Max(0, words.Count - 6); i--)
            {
                if (TrimPunctuation(words[i].ToLowerInvariant()) == first) { cutAt = i; break; }
            }
            if (cutAt == null && corrWords.Length == 1 && words.Count >= 2) cutAt = words.Count - 1;
            // Nicht sicher, was korrigiert wird: Text unverändert lassen
            if (cutAt is not int c) continue;
            words.RemoveRange(c, words.Count - c);
            var sep = head.Length == 0 || head.EndsWith('\n') ? "" : " ";
            var kept = Text.TrimH(head + sep + string.Join(' ', words));
            return Text.TrimH(kept.Length == 0 ? right : kept + " " + right);
        }
        return null;
    }

    static string TrimPunctuation(string s)
    {
        int a = 0, b = s.Length;
        while (a < b && char.IsPunctuation(s[a])) a++;
        while (b > a && char.IsPunctuation(s[b - 1])) b--;
        return s[a..b];
    }

    static int LastIndexWhere(string s, Func<char, bool> f)
    {
        for (int i = s.Length - 1; i >= 0; i--) if (f(s[i])) return i;
        return -1;
    }

    static int IndexWhere(string s, Func<char, bool> f)
    {
        for (int i = 0; i < s.Length; i++) if (f(s[i])) return i;
        return -1;
    }

    // ───────────────────────── Aufzählungen

    static readonly string[][] Ordinals =
    {
        new[] { "erstens", "first", "firstly" }, new[] { "zweitens", "second", "secondly" }, new[] { "drittens", "third", "thirdly" },
        new[] { "viertens", "fourth", "fourthly" }, new[] { "fünftens", "fifth", "fifthly" }, new[] { "sechstens", "sixth" }, new[] { "siebtens", "seventh" },
    };

    /// <summary>„Ich brauche erstens Milch zweitens Brot drittens Eier“ → nummerierte Liste.</summary>
    public static string FormatLists(string text)
    {
        var all = string.Join("|", Ordinals.SelectMany(o => o));
        var matches = Text.Re(@"(?<![\p{L}])(" + all + @")(?![\p{L}])[,:]?").Matches(text);
        if (matches.Count < 2) return text;
        var ranks = new List<int>();
        foreach (Match m in matches)
        {
            var w = m.Groups[1].Value.ToLowerInvariant();
            int rank = Array.FindIndex(Ordinals, o => o.Contains(w));
            if (rank < 0) return text;
            ranks.Add(rank);
        }
        if (!ranks.SequenceEqual(Enumerable.Range(0, ranks.Count))) return text;
        var intro = Text.TrimH(text[..matches[0].Index]);
        while (intro.Length > 0 && ",;".Contains(intro[^1])) intro = intro[..^1];
        var items = new List<string>();
        for (int i = 0; i < matches.Count; i++)
        {
            int start = matches[i].Index + matches[i].Length;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var item = text[start..end].Trim();
            while (item.Length > 0 && ",;.".Contains(item[^1])) item = item[..^1];
            item = Text.TrimH(item);
            foreach (var conj in new[] { " und", " and", " sowie", " oder", " or", " plus" })
                if (item.ToLowerInvariant().EndsWith(conj)) item = Text.TrimH(item[..^conj.Length]);
            while (item.Length > 0 && ",;.".Contains(item[^1])) item = item[..^1];
            if (item.Length == 0) return text;
            items.Add(CapitalizeFirst(item));
        }
        var list = string.Join("\n", items.Select((it, i) => $"{i + 1}. {it}"));
        if (intro.Length == 0) return list;
        if (!intro.EndsWith(':')) intro = intro.Trim('.') + ":";
        return intro + "\n" + list;
    }

    // ───────────────────────── Leerzeichen, Großschreibung, Satzende

    public static string CollapseSpaces(string s) => Text.Replace(s, @"[ \t ]{2,}", " ");

    public static string FixSpacing(string s)
    {
        var t = CollapseSpaces(s);
        t = Text.Replace(t, @"[ \t]+([,.;:!?…)\]])", "$1");   // kein Leerzeichen vor Satzzeichen
        t = Text.Replace(t, @"([(\[])[ \t]+", "$1");          // keins nach öffnender Klammer
        t = Text.Replace(t, @",\s*,", ",");                   // doppelte Kommas
        t = Text.Replace(t, @"([,;])(\p{L})", "$1 $2");       // Leerzeichen nach Komma/Semikolon
        t = Text.Replace(t, @"([!?])(\p{Lu})", "$1 $2");      // und nach !/? vor neuem Satz
        t = Text.Replace(t, @"[ \t]*\n[ \t]*", "\n");         // Zeilen sauber
        t = Text.Replace(t, @"\n{3,}", "\n\n");
        t = Text.Replace(t, @"^[ \t,;]+", "");                 // kein Komma am Anfang
        return Text.TrimH(t);
    }

    static readonly HashSet<string> Abbreviations = new()
    {
        "z", "b", "bzw", "usw", "ca", "d", "h", "u", "a", "evtl", "ggf", "inkl", "nr", "dr", "etc", "e.g", "i.e",
        "vs", "mr", "mrs", "ms", "st", "bspw", "vgl", "sog", "zb", "prof", "mio", "mrd", "jh", "abs", "s",
    };

    /// <summary>Großbuchstabe am Textanfang, nach Zeilenumbruch und nach .!? (außer Abkürzungen und Ordnungszahlen).</summary>
    public static string CapitalizeSentences(string s)
    {
        var chars = s.ToCharArray();
        bool capNext = true;
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (capNext && char.IsLetter(c))
            {
                chars[i] = char.ToUpperInvariant(c);
                capNext = false;
            }
            else if (c == '\n')
            {
                capNext = true;
            }
            else if (c is '.' or '!' or '?')
            {
                bool nextIsSpace = i + 1 < chars.Length && (chars[i + 1] == ' ' || chars[i + 1] == '\n');
                if (nextIsSpace)
                {
                    if (c == '.')
                    {
                        int j = i - 1;
                        var token = new StringBuilder();
                        while (j >= 0 && (char.IsLetter(chars[j]) || char.IsDigit(chars[j]) || chars[j] == '.')) { token.Insert(0, chars[j]); j--; }
                        var t = token.ToString().ToLowerInvariant();
                        bool isAbbrev = Abbreviations.Contains(t) || (t.Length == 1 && char.IsLetter(t[0]));
                        bool isOrdinal = t.Length > 0 && t.All(char.IsDigit);
                        capNext = !(isAbbrev || isOrdinal);
                    }
                    else
                    {
                        capNext = true;
                    }
                }
            }
            else if (!char.IsWhiteSpace(c) && !"\"„“'(«»".Contains(c))
            {
                capNext = false;
            }
        }
        return new string(chars);
    }

    public static string CapitalizeFirst(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Satzende ergänzen, wenn es wie ein Satz aussieht (mind. 3 Wörter, endet mit Buchstabe/Zahl).</summary>
    public static string EnsureFinalPunctuation(string s)
    {
        var t = s.Trim();
        if (t.Length == 0 || !char.IsLetterOrDigit(t[^1])) return t;
        var lines = t.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var lastLine = lines.Length > 0 ? lines[^1] : t;
        if (Regex.IsMatch(lastLine, @"^\s*(\d+[.)]|[-•*])\s")) return t;
        return Text.Words(lastLine).Length >= 3 ? t + "." : t;
    }

    /// <summary>Lockerer Stil: Punkt am Ende einer kurzen Nachricht weg (Frage-/Ausrufezeichen bleiben).</summary>
    public static string DropFinalPeriod(string s)
    {
        var t = s.Trim();
        if (t.EndsWith('.') && !t.EndsWith("..") && !t.Contains('\n'))
        {
            int sentences = t.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (sentences <= 2) t = t[..^1];
        }
        return t;
    }
}
