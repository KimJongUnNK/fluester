using System.Globalization;
using System.Text.RegularExpressions;

namespace Fluester.Core;

/// <summary>Kleine String-Hilfen, die Swifts Semantik nachbilden (z. B. ".whitespaces" ohne Zeilenumbrüche).</summary>
public static class Text
{
    /// <summary>Horizontales Leerzeichen (Leerzeichen, Tab, geschütztes Leerzeichen …), aber kein Zeilenumbruch.</summary>
    public static bool IsHSpace(char c) =>
        c == '\t' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator;

    /// <summary>Trimmt nur horizontale Leerzeichen.</summary>
    public static string TrimH(string s)
    {
        int a = 0, b = s.Length;
        while (a < b && IsHSpace(s[a])) a++;
        while (b > a && IsHSpace(s[b - 1])) b--;
        return s.Substring(a, b - a);
    }

    /// <summary>Trimmt Leerzeichen und Zeilenumbrüche.</summary>
    public static string TrimAll(string s) => s.Trim();

    public static string[] Words(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    public static string Lower(string s) => s.ToLowerInvariant();

    public static string Upper(string s) => s.ToUpperInvariant();

    const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>Ersetzen ohne Rücksicht auf Groß-/Kleinschreibung (wie NSRegularExpression mit .caseInsensitive).</summary>
    public static string Replace(string s, string pattern, string replacement) =>
        Regex.Replace(s, pattern, replacement, Opt);

    public static Regex Re(string pattern) => new(pattern, Opt);

    /// <summary>Text als Ersetzungsvorlage ohne Sonderzeichen ($).</summary>
    public static string EscapeReplacement(string s) => s.Replace("$", "$$");
}
