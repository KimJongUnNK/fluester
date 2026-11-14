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

    static string Heard(string s) => TextPipeline.RuleBased(TextPipeline.StripHallucinations(s), new CleanupOptions());

    [Fact]
    public void RealSentencesAreNotMistakenForHallucinations()
    {
        // Früher fiel der ganze Satz weg – das komplette Diktat war leer
        Assert.Equal("Danke dir, bis zum nächsten Mal.", Heard("Danke dir, bis zum nächsten Mal"));
        Assert.Equal("Viel Erfolg. Bis zum nächsten Mal.", Heard("Viel Erfolg. Bis zum nächsten Mal."));
        Assert.Equal("", Heard("Bis zum nächsten Mal."));                       // allein: Whisper bei Stille
        Assert.Equal("Das Meeting war gut.", Heard("Das Meeting war gut. Vielen Dank fürs Zuschauen."));
        Assert.Equal("Ich sagte am Ende vielen Dank fürs Zuschauen und ging.", Heard("Ich sagte am Ende vielen Dank fürs Zuschauen und ging"));
        Assert.Equal("", Heard("Untertitel der Amara.org-Community"));
        Assert.Equal("Das kostet 5 * 3 * 2 Euro.", Heard("Das kostet 5 * 3 * 2 Euro"));
        Assert.Equal("Er nickt kurz.", Heard("Er *lacht* nickt kurz"));
    }

    [Fact]
    public void SpokenCommandsAreContentAfterAnArticle()
    {
        Assert.Equal("Die neue Zeile im Code ist falsch.", Rules("Die neue Zeile im Code ist falsch"));
        Assert.Equal("Wir brauchen einen neuen Absatz im Vertrag.", Rules("Wir brauchen einen neuen Absatz im Vertrag"));
        Assert.Equal("Hallo Lisa\nWie geht es dir.", Rules("Hallo Lisa, neue Zeile, wie geht es dir"));
        Assert.Equal("Erster Teil\n\nZweiter Teil", Rules("erster Teil neuer Absatz zweiter Teil"));
        var spoken = new CleanupOptions(SpokenPunctuation: true);
        Assert.Equal("Der Punkt ist wichtig und das Komma auch.", TextPipeline.RuleBased("Der Punkt ist wichtig und das Komma auch", spoken));
        Assert.Equal("Hallo, wie geht es dir?", TextPipeline.RuleBased("Hallo Komma wie geht es dir Fragezeichen", spoken));
    }

    [Fact]
    public void SelfCorrectionsAndListsAvoidFalsePositives()
    {
        Assert.Equal("Bitte lies den Text, korrigiere Fehler.", Rules("Bitte lies den Text, korrigiere Fehler"));
        Assert.Equal("Das ist schlecht.", Rules("Das ist gut, sorry, ich meine schlecht"));
        Assert.Equal("Schreib an Peter.", Rules("Schreib an Paul, oder besser an Peter"));
        // „first“/„second“ als normale Wörter sind keine Aufzählung
        Assert.Equal("The first time I saw it was great, the second time was boring.", Rules("The first time I saw it was great, the second time was boring"));
        Assert.Equal("I came first, second was Tom.", Rules("I came first, second was Tom"));
        Assert.Equal("1. We go home\n2. We eat", Rules("First we go home, second we eat"));
        Assert.Equal("Ich brauche:\n1. Milch\n2. Brot\n3. Eier", Rules("Ich brauche erstens Milch zweitens Brot und drittens Eier"));
    }

    [Fact]
    public void DottedAbbreviationsDoNotStartASentence()
    {
        Assert.Equal("Das ist z.B. so und d.h. wir gehen.", Rules("das ist z.B. so und d.h. wir gehen"));
        Assert.Equal("Am 3. Nov. kommt er. Dann sehen wir.", Rules("am 3. Nov. kommt er. dann sehen wir"));
        Assert.Equal("Das mag ich so. Dann eben nicht.", Rules("das mag ich so. dann eben nicht"));
    }
}
