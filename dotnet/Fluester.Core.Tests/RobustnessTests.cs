using Fluester.Engine;

namespace Fluester.Core.Tests;

/// <summary>Kaputte Dateien, Rechte und Text-Regeln, die früher still Daten verloren oder Wörter verschluckt haben.</summary>
public class RobustnessTests
{
    const string Valid = """{ "hotkey": "capsLock", "dictionary": [ { "term": "Beeni" } ], "snippets": [ { "trigger": "adresse", "text": "Musterweg 1" } ] }""";

    [Fact]
    public void BrokenSettingsAreBackedUpInsteadOfSilentlyOverwritten()
    {
        using var h = new TempHome();
        var file = Path.Combine(h.Dir, "settings.json");
        // Von Hand bearbeitet, Komma vergessen
        var broken = Valid.Replace("\"capsLock\",", "\"capsLock\"");
        File.WriteAllText(file, broken);
        var store = new SettingsStore(file);
        Assert.Equal(HotkeyChoice.RightCtrl, store.Current.Hotkey);
        var backup = Assert.Single(Directory.GetFiles(h.Dir, "settings.json.kaputt-*"));
        Assert.Equal(broken, File.ReadAllText(backup));
        // Nächstes Speichern überschreibt die Datei – Wörterbuch und Textbausteine liegen aber noch in der Sicherung
        store.Update(s => s.Sounds = false);
        Assert.Contains("Beeni", File.ReadAllText(backup));
    }

    [Fact]
    public void ReloadKeepsCurrentSettingsWhileTheFileIsBroken()
    {
        using var h = new TempHome();
        var file = Path.Combine(h.Dir, "settings.json");
        File.WriteAllText(file, Valid);
        var store = new SettingsStore(file);
        int changes = 0;
        store.Changed += _ => changes++;
        File.WriteAllText(file, "{ \"hotkey\": ");
        store.Reload();
        Assert.Equal(HotkeyChoice.CapsLock, store.Current.Hotkey);
        Assert.Equal("Beeni", store.Current.Dictionary[0].Term);
        Assert.Equal(0, changes);
        File.WriteAllText(file, Valid.Replace("capsLock", "f8"));
        store.Reload();
        Assert.Equal(HotkeyChoice.F8, store.Current.Hotkey);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void NullEntriesInSettingsDoNotCrashLater()
    {
        Assert.True(Settings.TryFromJson("""
            { "dictionary": [ null, { "term": null, "soundsLike": [ null, "bini" ] } ],
              "snippets": [ null, { "trigger": null, "text": null } ],
              "cloudChat": { "baseUrl": null, "model": null, "providerId": null },
              "whisperModelId": null }
            """, out var s));
        var entry = Assert.Single(s.Dictionary);
        Assert.Equal("", entry.Term);
        Assert.Equal(new[] { "bini" }, entry.SoundsLike);
        Assert.Equal("", Assert.Single(s.Snippets).Trigger);
        Assert.Equal("", s.CloudChat.BaseUrl);
        Assert.Equal("groq", s.CloudChat.ProviderId);
        Assert.Equal("turbo-q5", s.WhisperModelId);
        // Und die Regeln laufen damit durch
        Assert.Equal("Hallo bini", TextPipeline.RuleBased("hallo bini", CleanupOptions.From(s, WritingStyle.Formal), s.Dictionary));
        Assert.Equal(("hallo", false), Vocabulary.ExpandSnippets("hallo", s.Snippets));
        Assert.False(Settings.TryFromJson("kaputt", out var d));
        Assert.Equal(HotkeyChoice.RightCtrl, d.Hotkey);
        Assert.True(Settings.TryFromJson("", out _));
    }

    [Fact]
    public void BrokenHistoryIsBackedUp()
    {
        using var h = new TempHome();
        var file = Path.Combine(h.Dir, "history.json");
        File.WriteAllText(file, "[ { \"text\": \"Wichtiges Diktat\" }, ");
        var store = new HistoryStore(file);
        Assert.Empty(store.Items);
        store.Add(new HistoryItem { Text = "Neu" });
        var backup = Assert.Single(Directory.GetFiles(h.Dir, "history.json.kaputt-*"));
        Assert.Contains("Wichtiges Diktat", File.ReadAllText(backup));
        // Einzelne null-Einträge fallen still weg
        File.WriteAllText(file, "[ null, { \"text\": \"ok\" } ]");
        Assert.Equal("ok", Assert.Single(new HistoryStore(file).Items).Text);
    }

    [Fact]
    public void KeyFileIsPrivateAndBrokenKeysAreBackedUp()
    {
        using var h = new TempHome();
        var file = Path.Combine(h.Dir, "keys.json");
        var store = new FileSecretStore(file);
        store.Set("custom", "  geheim  ");
        Assert.Equal("geheim", store.Get("custom"));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        File.WriteAllText(file, "{ kaputt");
        store.Set("custom", "neu");
        Assert.Equal("neu", store.Get("custom"));
        Assert.Contains("kaputt", File.ReadAllText(Assert.Single(Directory.GetFiles(h.Dir, "keys.json.kaputt-*"))));
    }

    static string Rules(string s) => TextPipeline.RuleBased(s, new CleanupOptions());

    [Fact]
    public void StuttersAcrossACommaOnlyAtTheStartOfAClause()
    {
        // Zwei Teilsätze – das zweite „ich“ ist kein Stotterer
        Assert.Equal("Das glaube ich, ich bin sicher.", Rules("Das glaube ich, ich bin sicher"));
        Assert.Equal("Das meine ich, ich meine es ernst.", Rules("Das meine ich, ich meine es ernst"));
        // Echte Stotterer am Satzanfang, auch mit Komma
        Assert.Equal("Ich wollte fragen.", Rules("Ich, ich wollte fragen"));
        Assert.Equal("Ich wollte fragen.", Rules("Ähm, ich, ich wollte fragen"));
        Assert.Equal("Gut. Ich wollte fragen.", Rules("Gut. Ich, ich wollte fragen"));
        // Ohne Komma wie bisher
        Assert.Equal("Wir müssen los.", Rules("wir wir müssen los"));
    }
}
