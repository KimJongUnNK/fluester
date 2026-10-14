using System.Text;

namespace Fluester.Core.Tests;

public class VocabularyTests
{
    [Fact]
    public void WholeWordReplacementIsCaseInsensitive()
    {
        Assert.Equal("ich war bei Voltage Events", Vocabulary.ReplaceWholeWords("ich war bei voltage   events", "Voltage Events", "Voltage Events"));
        Assert.Equal("Beenis Set", Vocabulary.ReplaceWholeWords("Beenis Set", "beeni", "Beeni"));
    }

    [Fact]
    public void ReplacementWithDollarStaysLiteral() =>
        Assert.Equal("Preis $5", Vocabulary.ReplaceWholeWords("Preis fünf", "fünf", "$5"));

    [Fact]
    public void SnippetWholeUtterance()
    {
        var s = new[] { new Snippet("meine Adresse", "Musterstraße 1, 6850 Dornbirn") };
        Assert.Equal("Musterstraße 1, 6850 Dornbirn", Vocabulary.ExpandSnippets("Meine Adresse.", s).Text);
        Assert.True(Vocabulary.ExpandSnippets("meine adresse", s).MatchedWhole);
        Assert.True(Vocabulary.ExpandSnippets("Füge meine Adresse ein", s).MatchedWhole);
    }

    [Fact]
    public void SnippetInsideSentence()
    {
        var s = new[] { new Snippet("mein Calendly Link", "https://calendly.com/emil") };
        var r = Vocabulary.ExpandSnippets("Buch dir einen Termin über mein Calendly Link", s);
        Assert.Equal("Buch dir einen Termin über https://calendly.com/emil", r.Text);
        Assert.False(r.MatchedWhole);
    }

    [Fact]
    public void HintsDeduplicate() =>
        Assert.Equal(new[] { "Beeni", "Exodus" }, Vocabulary.Hints(new[] { new DictionaryEntry("Beeni"), new DictionaryEntry("beeni"), new DictionaryEntry("Exodus") }));

    [Fact]
    public void Normalize() => Assert.Equal("meine adresse", Vocabulary.Normalize("  Meine, Adresse!! "));
}

public class PromptsTests
{
    [Fact]
    public void SanitizeStripsWrappers()
    {
        Assert.Equal("Hallo Welt.", Prompts.Sanitize("\"Hallo Welt.\"", "hallo welt"));
        Assert.Equal("Hallo Welt.", Prompts.Sanitize("Hier ist der bereinigte Text: Hallo Welt.", "hallo welt"));
        Assert.Equal("Hallo", Prompts.Sanitize("<diktat>Hallo</diktat>", "hallo"));
        Assert.Equal("Hallo.", Prompts.Sanitize("<think>hmm</think>\nHallo.", "hallo"));
        Assert.Equal("Hallo", Prompts.Sanitize("```\nHallo\n```", "hallo"));
    }

    [Fact]
    public void SanitizeRejectsAnswers()
    {
        var answer = string.Concat(Enumerable.Repeat("Die Hauptstadt von Frankreich ist Paris und sie hat viele Sehenswürdigkeiten. ", 4));
        Assert.Null(Prompts.Sanitize(answer, "was ist die hauptstadt von frankreich"));
        Assert.Null(Prompts.Sanitize("Als KI kann ich das nicht", "bitte schreib das auf"));
    }

    [Fact]
    public void CleanupPromptContainsRules()
    {
        var p = Prompts.CleanupSystem(WritingStyle.Casual, "WhatsApp", "Deutsch", new[] { new DictionaryEntry("Voltage Events") }, true, true);
        Assert.Contains("Voltage Events", p);
        Assert.Contains("WhatsApp", p);
        Assert.Contains("NICHT an dich gerichtet", p);
        Assert.Contains("Selbstkorrekturen", p);
        Assert.Equal("<diktat>\nhallo\n</diktat>", Prompts.CleanupUser("hallo"));
    }

    [Fact]
    public void CommandUser()
    {
        Assert.Equal("<anweisung>kürzer</anweisung>\n<text>\nLanger Text\n</text>", Prompts.CommandUser("kürzer", "Langer Text"));
        Assert.Equal("<anweisung>schreib eine Absage</anweisung>", Prompts.CommandUser("schreib eine Absage", "  "));
    }
}

public class AudioTests
{
    [Fact]
    public void WavHeader()
    {
        var d = AudioUtil.Wav(new float[] { 0, 0.5f, -0.5f, 1 }, 16000);
        Assert.Equal(44 + 8, d.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(d, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(d, 8, 4));
        Assert.Equal(16000u, BitConverter.ToUInt32(d, 24));
        Assert.Equal((short)32767, BitConverter.ToInt16(d, 50));
    }

    [Fact]
    public void WavRoundTrip()
    {
        var tone = Enumerable.Range(0, 48000).Select(i => (float)(Math.Sin(i * 0.05) * 0.4)).ToArray();
        var wav = AudioUtil.Wav(tone, 48000);
        var back = AudioUtil.ReadWav(wav);
        Assert.Equal(16000, back.Length);
        Assert.InRange(back.Max(), 0.35f, 0.41f);
    }

    [Fact]
    public void SpeechDetection()
    {
        Assert.False(AudioUtil.HasSpeech(Enumerable.Repeat(0.001f, 16000).ToArray(), 16000));
        Assert.True(AudioUtil.HasSpeech(Enumerable.Range(0, 16000).Select(i => (float)(Math.Sin(i * 0.2) * 0.3)).ToArray(), 16000));
    }

    [Fact]
    public void TrimSilenceKeepsSpeech()
    {
        var s = new float[16000].Concat(Enumerable.Range(0, 8000).Select(i => (float)(Math.Sin(i * 0.2) * 0.3))).Concat(new float[16000]).ToArray();
        var t = AudioUtil.TrimSilence(s, 16000);
        Assert.True(t.Length < s.Length);
        Assert.True(t.Length >= 8000);
    }

    [Fact]
    public void Resample() => Assert.Equal(16000, AudioUtil.Resample(Enumerable.Repeat(0.5f, 48000).ToArray(), 48000, 16000).Length);

    [Fact]
    public void Pcm16() => Assert.Equal(new[] { 0f, -1f }, AudioUtil.FromPcm16(new byte[] { 0, 0, 0, 0x80 }));
}

public class GestureTests
{
    static readonly HotkeyGestures.Action None = HotkeyGestures.Action.None;

    [Fact]
    public void HoldAndRelease()
    {
        var g = new HotkeyGestures();
        Assert.Equal(HotkeyGestures.Action.Start(false), g.Press(0, false));
        Assert.Equal(HotkeyGestures.Action.Stop, g.Release(1.2));
        Assert.Equal(HotkeyGestures.GestureState.Idle, g.State);
    }

    [Fact]
    public void DoubleTapGoesHandsFree()
    {
        var g = new HotkeyGestures();
        g.Press(0, false);
        Assert.Equal(None, g.Release(0.1));
        Assert.Equal(HotkeyGestures.Action.HandsFree, g.Press(0.3, false));
        Assert.Equal(None, g.Release(0.35));
        Assert.Equal(HotkeyGestures.GestureState.HandsFree, g.State);
        Assert.Equal(None, g.Press(5, false));
        Assert.Equal(HotkeyGestures.Action.Stop, g.Release(5.1));
    }

    [Fact]
    public void SingleTapCancelsAfterWindow()
    {
        var g = new HotkeyGestures();
        g.Press(0, false);
        g.Release(0.1);
        Assert.Equal(None, g.Tick(0.3));
        Assert.Equal(HotkeyGestures.Action.CancelIt, g.Tick(0.6));
    }

    [Fact]
    public void OtherKeyCancels()
    {
        var g = new HotkeyGestures();
        g.Press(0, false);
        g.OtherKeyPressed();
        Assert.Equal(HotkeyGestures.Action.CancelIt, g.Release(0.5));
    }

    [Fact]
    public void CommandFlag() => Assert.Equal(HotkeyGestures.Action.Start(true), new HotkeyGestures().Press(0, true));
}

public class SettingsTests
{
    [Fact]
    public void DecodesOldJsonWithMissingKeys()
    {
        var s = Settings.FromJson("""{"hotkey":"capsLock","language":"de-DE"}""");
        Assert.Equal(HotkeyChoice.CapsLock, s.Hotkey);
        Assert.Equal("de-DE", s.Language);
        Assert.Equal(EngineChoice.WhisperLocal, s.Engine);
        Assert.True(s.RemoveFillers);
    }

    [Fact]
    public void UnknownEnumFallsBack()
    {
        var s = Settings.FromJson("""{"hotkey":"fn","engine":"apple","cleanup":"appleIntelligence"}""");
        Assert.Equal(HotkeyChoice.RightCtrl, s.Hotkey);
        Assert.Equal(EngineChoice.WhisperLocal, s.Engine);
        Assert.Equal(CleanupChoice.Rules, s.Cleanup);
    }

    [Fact]
    public void BrokenJsonGivesDefaults() => Assert.Equal(HotkeyChoice.RightCtrl, Settings.FromJson("{kaputt").Hotkey);

    [Fact]
    public void RoundTrip()
    {
        var s = new Settings();
        s.Snippets.Add(new Snippet("a", "b"));
        s.Styles.Set(WritingStyle.Excited, AppCategory.Messages);
        s.Styles.Overrides["notepad"] = AppCategory.Work;
        var back = Settings.FromJson(s.ToJson());
        Assert.Equal(WritingStyle.Excited, back.Styles.StyleFor(AppCategory.Messages));
        Assert.Equal("b", back.Snippets[0].Text);
        Assert.Equal(AppCategory.Work, back.Styles.Overrides["notepad"]);
        Assert.Contains("\"hotkey\": \"rightCtrl\"", s.ToJson());
    }

    [Fact]
    public void Categorizer()
    {
        Assert.Equal(AppCategory.Messages, AppCategorizer.Category("WhatsApp.exe"));
        Assert.Equal(AppCategory.Email, AppCategorizer.Category("OUTLOOK"));
        Assert.Equal(AppCategory.Email, AppCategorizer.Category("thunderbird"));
        Assert.Equal(AppCategory.Code, AppCategorizer.Category("idea64.exe"));
        Assert.Equal(AppCategory.Code, AppCategorizer.Category("jetbrains-webstorm"));
        Assert.Equal(AppCategory.Email, AppCategorizer.Category("chrome", "Posteingang – Gmail"));
        Assert.Equal(AppCategory.Other, AppCategorizer.Category("notepad"));
        Assert.Equal(AppCategory.Work, AppCategorizer.Category("notepad.exe", null, new Dictionary<string, AppCategory> { ["Notepad"] = AppCategory.Work }));
    }
}

public class StatsTests
{
    [Fact]
    public void Summary()
    {
        var now = DateTime.Now;
        var items = new[]
        {
            new HistoryItem { Date = now, Text = "eins zwei drei vier", Seconds = 2 },
            new HistoryItem { Date = now.AddDays(-1), Text = "fünf sechs", Seconds = 4 },
            new HistoryItem { Date = now, Text = "kürzer", Seconds = 1, IsCommand = true },
        };
        var s = Stats.Summary(items, now);
        Assert.Equal(6, s.TotalWords);
        Assert.Equal(4, s.TodayWords);
        Assert.Equal(2, s.Dictations);
        Assert.Equal(60, s.Wpm);
        Assert.Equal(2, s.StreakDays);
    }

    [Fact]
    public void WordCountIgnoresPunctuation() => Assert.Equal(2, Stats.WordCount("Hallo, – Welt !"));
}

public class ApiTests
{
    [Fact]
    public async Task TranscriptionRequest()
    {
        var c = new OpenAICompatibleClient("https://api.groq.com/openai/v1/", "k");
        var r = c.TranscriptionRequest(new byte[] { 1, 2 }, "whisper-large-v3-turbo", "de-DE", "Beeni");
        Assert.Equal("https://api.groq.com/openai/v1/audio/transcriptions", r.RequestUri!.ToString());
        Assert.Equal("Bearer k", r.Headers.Authorization!.ToString());
        var body = await r.Content!.ReadAsStringAsync();
        Assert.Contains("name=language", body);
        Assert.Contains("\r\n\r\nde\r\n", body);
        Assert.Contains("Beeni", body);
        Assert.Contains("filename=diktat.wav", body);
    }

    [Fact]
    public void ChatParsing()
    {
        Assert.Equal("Hallo.", OpenAICompatibleClient.ParseChat("""{"choices":[{"message":{"role":"assistant","content":"Hallo."}}]}"""));
        Assert.Equal("Hallo Welt", OpenAICompatibleClient.ParseTranscription("""{"text":" Hallo Welt "}"""));
        Assert.Throws<FluesterException>(() => OpenAICompatibleClient.ParseChat("{}"));
    }

    [Fact]
    public void ErrorMessages() =>
        Assert.Equal("API-Schlüssel ungültig oder fehlt (Invalid API Key)",
            OpenAICompatibleClient.Describe(401, """{"error":{"message":"Invalid API Key"}}"""));

    [Fact]
    public void BadUrl() => Assert.Throws<FluesterException>(() => new OpenAICompatibleClient("", null).Url("/x"));
}
