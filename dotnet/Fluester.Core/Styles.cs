namespace Fluester.Core;

/// <summary>Schreibstil je nach App.</summary>
public enum WritingStyle { Formal, Casual, VeryCasual, Excited, Raw }

public static class WritingStyleInfo
{
    public static readonly WritingStyle[] All = Enum.GetValues<WritingStyle>();

    public static string Label(this WritingStyle s) => s switch
    {
        WritingStyle.Formal => "Formell",
        WritingStyle.Casual => "Locker",
        WritingStyle.VeryCasual => "Sehr locker",
        WritingStyle.Excited => "Begeistert",
        _ => "Wörtlich",
    };

    public static string Example(this WritingStyle s) => s switch
    {
        WritingStyle.Formal => "Hallo Lisa, passt dir Donnerstag um 15 Uhr? Liebe Grüße",
        WritingStyle.Casual => "Hallo Lisa, passt dir Donnerstag um 15 Uhr",
        WritingStyle.VeryCasual => "hallo lisa passt dir donnerstag um 15 uhr",
        WritingStyle.Excited => "Hallo Lisa! Passt dir Donnerstag um 15 Uhr?",
        _ => "Genau wie gesprochen, nur Füllwörter weg",
    };

    /// <summary>Anweisung an das Sprachmodell.</summary>
    public static string Prompt(this WritingStyle s) => s switch
    {
        WritingStyle.Formal => "Stil: formell und korrekt. Vollständige Sätze, korrekte Groß-/Kleinschreibung und Zeichensetzung.",
        WritingStyle.Casual => "Stil: locker. Normale Groß-/Kleinschreibung, sparsame Zeichensetzung, kein Punkt am Ende einer kurzen Nachricht.",
        WritingStyle.VeryCasual => "Stil: sehr locker wie im Chat. Alles kleingeschrieben, kaum Satzzeichen, kein Punkt am Ende.",
        WritingStyle.Excited => "Stil: begeistert und freundlich. Ausrufezeichen wo passend, aber nicht übertreiben.",
        _ => "Stil: wörtlich. Formuliere nichts um, korrigiere nur Zeichensetzung und entferne Füllwörter.",
    };
}

/// <summary>App-Kategorien, für die je ein Stil gilt.</summary>
public enum AppCategory { Messages, Email, Work, Code, Other }

public static class AppCategoryInfo
{
    public static readonly AppCategory[] All = Enum.GetValues<AppCategory>();

    public static string Label(this AppCategory c) => c switch
    {
        AppCategory.Messages => "Private Nachrichten",
        AppCategory.Email => "E-Mail",
        AppCategory.Work => "Arbeits-Chats",
        AppCategory.Code => "Code & Terminal",
        _ => "Alle anderen Apps",
    };

    public static string AppsHint(this AppCategory c) => c switch
    {
        AppCategory.Messages => "WhatsApp, Telegram, Signal, Instagram",
        AppCategory.Email => "Outlook, Thunderbird, Gmail, Mailspring",
        AppCategory.Work => "Slack, Teams, Discord, Notion, Linear",
        AppCategory.Code => "VS Code, Cursor, Visual Studio, Terminal",
        _ => "Notizen, Word, Browser …",
    };
}

public sealed class StyleSettings
{
    public WritingStyle Messages { get; set; } = WritingStyle.Casual;
    public WritingStyle Email { get; set; } = WritingStyle.Formal;
    public WritingStyle Work { get; set; } = WritingStyle.Casual;
    public WritingStyle Code { get; set; } = WritingStyle.Raw;
    public WritingStyle Other { get; set; } = WritingStyle.Formal;
    /// <summary>Eigene Zuordnung Programmname → Kategorie (überschreibt die eingebaute).</summary>
    public Dictionary<string, AppCategory> Overrides { get; set; } = new();

    public WritingStyle StyleFor(AppCategory c) => c switch
    {
        AppCategory.Messages => Messages,
        AppCategory.Email => Email,
        AppCategory.Work => Work,
        AppCategory.Code => Code,
        _ => Other,
    };

    public void Set(WritingStyle s, AppCategory c)
    {
        switch (c)
        {
            case AppCategory.Messages: Messages = s; break;
            case AppCategory.Email: Email = s; break;
            case AppCategory.Work: Work = s; break;
            case AppCategory.Code: Code = s; break;
            default: Other = s; break;
        }
    }
}

/// <summary>Ordnet das Programm im Vordergrund einer Kategorie zu – über den Prozess- bzw. Programmnamen
/// (Windows: „outlook“, „ms-teams“ …; Linux: WM_CLASS wie „thunderbird“, „slack“ …) und bei Browsern über den Fenstertitel.</summary>
public static class AppCategorizer
{
    static readonly (string, AppCategory)[] Known =
    {
        ("whatsapp", AppCategory.Messages), ("whatsapp.root", AppCategory.Messages), ("telegram", AppCategory.Messages),
        ("telegramdesktop", AppCategory.Messages), ("signal", AppCategory.Messages), ("instagram", AppCategory.Messages),
        ("messenger", AppCategory.Messages), ("element", AppCategory.Messages), ("threema", AppCategory.Messages),
        ("outlook", AppCategory.Email), ("olk", AppCategory.Email), ("hxoutlook", AppCategory.Email), ("thunderbird", AppCategory.Email),
        ("mailspring", AppCategory.Email), ("evolution", AppCategory.Email), ("geary", AppCategory.Email), ("emclient", AppCategory.Email),
        ("betterbird", AppCategory.Email), ("spark", AppCategory.Email), ("superhuman", AppCategory.Email),
        ("slack", AppCategory.Work), ("ms-teams", AppCategory.Work), ("teams", AppCategory.Work), ("discord", AppCategory.Work),
        ("notion", AppCategory.Work), ("linear", AppCategory.Work), ("figma", AppCategory.Work), ("zoom", AppCategory.Work),
        ("mattermost", AppCategory.Work), ("trello", AppCategory.Work),
        ("code", AppCategory.Code), ("code - insiders", AppCategory.Code), ("cursor", AppCategory.Code), ("windsurf", AppCategory.Code),
        ("devenv", AppCategory.Code), ("windowsterminal", AppCategory.Code), ("cmd", AppCategory.Code), ("powershell", AppCategory.Code),
        ("pwsh", AppCategory.Code), ("conhost", AppCategory.Code), ("wezterm-gui", AppCategory.Code), ("alacritty", AppCategory.Code),
        ("kitty", AppCategory.Code), ("gnome-terminal-server", AppCategory.Code), ("gnome-terminal", AppCategory.Code),
        ("konsole", AppCategory.Code), ("xterm", AppCategory.Code), ("tilix", AppCategory.Code), ("terminator", AppCategory.Code),
        ("ghostty", AppCategory.Code), ("warp", AppCategory.Code), ("zed", AppCategory.Code), ("sublime_text", AppCategory.Code),
        ("idea64", AppCategory.Code), ("pycharm64", AppCategory.Code), ("webstorm64", AppCategory.Code), ("rider64", AppCategory.Code),
        ("clion64", AppCategory.Code), ("goland64", AppCategory.Code), ("phpstorm64", AppCategory.Code), ("rustrover64", AppCategory.Code),
        ("jetbrains-idea", AppCategory.Code), ("jetbrains-pycharm", AppCategory.Code), ("notepad++", AppCategory.Code),
    };

    static readonly (string, AppCategory)[] TitleHints =
    {
        ("gmail", AppCategory.Email), ("outlook", AppCategory.Email), ("posteingang", AppCategory.Email), ("inbox", AppCategory.Email),
        ("whatsapp", AppCategory.Messages), ("messenger", AppCategory.Messages), ("telegram", AppCategory.Messages), ("instagram", AppCategory.Messages),
        ("slack", AppCategory.Work), ("teams", AppCategory.Work), ("discord", AppCategory.Work), ("notion", AppCategory.Work), ("linear", AppCategory.Work),
        ("github", AppCategory.Code), ("gitlab", AppCategory.Code), ("stack overflow", AppCategory.Code), ("replit", AppCategory.Code),
    };

    static readonly string[] Browsers =
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "zen", "chromium", "google-chrome", "microsoft-edge",
        "librewolf", "floorp", "thorium", "waterfox",
    };

    /// <summary>Programmname normalisieren: klein, ohne „.exe“.</summary>
    public static string Normalize(string? app)
    {
        var a = (app ?? "").Trim().ToLowerInvariant();
        return a.EndsWith(".exe") ? a[..^4] : a;
    }

    public static AppCategory Category(string? app, string? windowTitle = null, IReadOnlyDictionary<string, AppCategory>? overrides = null)
    {
        var id = Normalize(app);
        if (id.Length == 0) return AppCategory.Other;
        if (overrides != null)
            foreach (var (k, v) in overrides)
                if (Normalize(k) == id) return v;
        foreach (var (name, cat) in Known)
            if (id == name) return cat;
        // JetBrains-IDEs unter Linux: „jetbrains-…“
        if (id.StartsWith("jetbrains-")) return AppCategory.Code;
        if (Browsers.Contains(id) && windowTitle is { Length: > 0 })
        {
            var t = windowTitle.ToLowerInvariant();
            foreach (var (hint, cat) in TitleHints)
                if (t.Contains(hint)) return cat;
        }
        return AppCategory.Other;
    }
}
