using Fluester.Engine;

namespace Fluester.Core.Tests;

public sealed class TempHome : IDisposable
{
    public readonly string Dir = Path.Combine(Path.GetTempPath(), "fluester-test-" + Guid.NewGuid().ToString("N"));
    public TempHome() { Directory.CreateDirectory(Dir); }
    public void Dispose() { try { Directory.Delete(Dir, true); } catch { } }
}

public class EngineTests
{
    [Fact]
    public void SettingsStorePersists()
    {
        using var h = new TempHome();
        var file = Path.Combine(h.Dir, "settings.json");
        var store = new SettingsStore(file);
        Settings? seen = null;
        store.Changed += s => seen = s;
        store.Update(s => { s.Hotkey = HotkeyChoice.CapsLock; s.Dictionary.Add(new DictionaryEntry("Beeni")); });
        Assert.Equal(HotkeyChoice.CapsLock, seen!.Hotkey);
        var again = new SettingsStore(file).Current;
        Assert.Equal(HotkeyChoice.CapsLock, again.Hotkey);
        Assert.Equal("Beeni", again.Dictionary[0].Term);
        // Current ist eine Kopie
        var copy = store.Current;
        copy.Hotkey = HotkeyChoice.F8;
        Assert.Equal(HotkeyChoice.CapsLock, store.Current.Hotkey);
    }

    [Fact]
    public void HistoryNewestFirstAndCapped()
    {
        using var h = new TempHome();
        var file = Path.Combine(h.Dir, "history.json");
        var store = new HistoryStore(file);
        for (int i = 0; i < 2005; i++) store.Add(new HistoryItem { Text = "Nr " + i });
        Assert.Equal(2000, store.Items.Count);
        Assert.Equal("Nr 2004", store.Items[0].Text);
        Assert.Equal("Nr 2004", new HistoryStore(file).Last!.Text);
    }

    [Fact]
    public void SecretsRoundTripAndEnvironmentWins()
    {
        using var h = new TempHome();
        var s = new FileSecretStore(Path.Combine(h.Dir, "keys.json"));
        s.Set("custom", " abc ");
        Assert.Equal("abc", s.Get("custom"));
        s.Set("custom", null);
        Assert.Null(s.Get("custom"));
        Environment.SetEnvironmentVariable("FLUESTER_KEY_TESTPROV", "env-key");
        Assert.Equal("env-key", s.Get("testprov"));
        Environment.SetEnvironmentVariable("FLUESTER_KEY_TESTPROV", null);
        if (!OperatingSystem.IsWindows())
        {
            s.Set("custom", "x");
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(h.Dir, "keys.json")));
        }
    }

    [Fact]
    public async Task CleanTextWithRules()
    {
        var svc = new DictationService(new FileSecretStore(Path.Combine(Path.GetTempPath(), "none-" + Guid.NewGuid() + ".json")));
        var s = new Settings { Cleanup = CleanupChoice.Rules };
        var (text, warning) = await svc.CleanTextAsync("äh also wir treffen uns um 3, ich meine um 4 Uhr", WritingStyle.Formal, "Test", s);
        Assert.Equal("Also wir treffen uns um 4 Uhr.", text);
        Assert.Null(warning);
    }

    [Fact]
    public async Task CleanTextSnippetWins()
    {
        var svc = new DictationService(new FileSecretStore(Path.Combine(Path.GetTempPath(), "none-" + Guid.NewGuid() + ".json")));
        var s = new Settings { Cleanup = CleanupChoice.Llm };
        s.Snippets.Add(new Snippet("meine Adresse", "Musterstraße 1"));
        Assert.Equal("Musterstraße 1", (await svc.CleanTextAsync("Meine Adresse.", WritingStyle.Formal, "", s)).Text);
    }

    [Fact]
    public async Task LlmWithoutKeyFallsBackToRules()
    {
        var svc = new DictationService(new FileSecretStore(Path.Combine(Path.GetTempPath(), "none-" + Guid.NewGuid() + ".json")));
        var s = new Settings { Cleanup = CleanupChoice.Llm };
        Environment.SetEnvironmentVariable("GROQ_API_KEY", null);
        var (text, warning) = await svc.CleanTextAsync("ähm hallo zusammen wie geht es euch", WritingStyle.Formal, "", s);
        Assert.Equal("Hallo zusammen wie geht es euch.", text);
        Assert.Contains("Regeln", warning);
    }

    [Fact]
    public async Task SilenceIsIgnored()
    {
        var svc = new DictationService(new FileSecretStore(Path.Combine(Path.GetTempPath(), "none-" + Guid.NewGuid() + ".json")));
        Assert.Null(await svc.ProcessAsync(new float[16000], new Settings(), TargetApp.Unknown, false, null, default));
    }

    [Fact]
    public void MissingModelGivesClearError()
    {
        using var h = new TempHome();
        Environment.SetEnvironmentVariable("FLUESTER_HOME", h.Dir);
        try
        {
            var e = Assert.Throws<FluesterException>(() =>
                new WhisperLocalEngine(WhisperModel.ById("base"), false).TranscribeAsync(new float[16000], "de", Array.Empty<string>()).GetAwaiter().GetResult());
            Assert.Contains("Modell fehlt", e.Message);
            Assert.False(ModelStore.IsInstalled(WhisperModel.ById("base")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FLUESTER_HOME", null);
        }
    }
}
