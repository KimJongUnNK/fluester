namespace Fluester.Core.Tests;

/// <summary>Gleiche Fälle wie macos/Tests/FluesterCoreTests/TextPipelineTests.swift – Windows/Linux müssen identisch bereinigen.</summary>
public class TextPipelineTests
{
    static readonly CleanupOptions Formal = new(Style: WritingStyle.Formal);

    static string Rules(string s, CleanupOptions? o = null, IEnumerable<DictionaryEntry>? dict = null) =>
        TextPipeline.RuleBased(s, o ?? Formal, dict);

    [Fact]
    public void RemovesGermanFillers()
    {
        Assert.Equal("Ich wollte morgen kommen.", Rules("Ich, äh, wollte morgen kommen"));
        Assert.Equal("Ich wollte fragen, ob das passt?", Rules("Äh, ich wollte ähm fragen, ob das passt?"));
        Assert.Equal("Also das ist gut.", Rules("Öhm also das ist gut"));
    }

    [Fact]
    public void KeepsCommaBeforeConjunction() =>
        Assert.Equal("Das Problem ist, dass wir zu spät sind.", Rules("Das Problem ist, äh, dass wir zu spät sind"));

    [Fact]
    public void RemovesEnglishFillers()
    {
        Assert.Equal("I think we should go.", Rules("Um, I think, uh, we should go"));
        Assert.Equal("So let's meet tomorrow.", Rules("So, uhm, let's meet tomorrow"));
    }

    [Fact]
    public void GermanUmIsNotAFiller()
    {
        Assert.Equal("Wir treffen uns um 4 Uhr.", Rules("Wir treffen uns um 4 Uhr"));
        Assert.Equal("Das ist eh klar.", Rules("Das ist eh klar"));
    }

    [Fact]
    public void Stutters()
    {
        Assert.Equal("Ich wollte nur sagen.", Rules("ich ich ich wollte nur sagen"));
        Assert.Equal("Die Frau, die die Tür öffnet.", Rules("Die Frau, die die Tür öffnet"));
        Assert.Equal("Ja ja, passt.", Rules("Ja ja, passt"));
    }

    [Fact]
    public void KeepsNewlinesAroundFillers() =>
        Assert.Equal("Hallo\nwie geht es", TextPipeline.Preprocess("Hallo\näh, wie geht es", Formal));

    [Fact]
    public void SpokenNewLineAndParagraph()
    {
        Assert.Equal("Hallo Lisa\nWie geht es dir.", Rules("Hallo Lisa, neue Zeile, wie geht es dir"));
        Assert.Equal("Liebe Grüße.\n\nEmil", Rules("Liebe Grüße. Neuer Absatz. Emil"));
        Assert.Equal("Thanks\nBest Emil", Rules("Thanks new line best Emil"));
    }

    [Fact]
    public void SpokenPunctuationOnlyWhenEnabled()
    {
        Assert.Equal("Der Punkt ist wichtig.", Rules("Der Punkt ist wichtig", Formal));
        Assert.Equal("Hallo, wie geht es dir?", Rules("Hallo Komma wie geht es dir Fragezeichen", Formal with { SpokenPunctuation = true }));
    }

    [Fact]
    public void SelfCorrectionWithAnchor()
    {
        Assert.Equal("Ich komme um 4 Uhr.", Rules("Ich komme um 3, ich meine um 4 Uhr"));
        Assert.Equal("Schick es an Peter.", Rules("Schick es an Paul, ich meine an Peter"));
        Assert.Equal("Let's meet at 6.", Rules("Let's meet at 5, I mean at 6"));
    }

    [Fact]
    public void SelfCorrectionSingleWord() =>
        Assert.Equal("Wir treffen uns am Dienstag.", Rules("Wir treffen uns am Montag, ich meine Dienstag"));

    [Fact]
    public void UnclearCorrectionLeavesText()
    {
        Assert.Equal("Das ist gut, ich meine, wirklich gut.", Rules("Das ist gut, ich meine, wirklich gut"));
        Assert.Equal("Ich meine, das ist gut.", Rules("Ich meine, das ist gut"));
    }

    [Fact]
    public void ScratchThat()
    {
        Assert.Equal("Wir treffen uns übermorgen.", Rules("Wir treffen uns morgen. Streich das. Wir treffen uns übermorgen."));
        Assert.Equal("Hallo Paul. Ich nehme das Rad.", Rules("Hallo Paul. Ich nehme den Bus, streich das, ich nehme das Rad"));
    }

    [Fact]
    public void NumberedList()
    {
        Assert.Equal("Ich brauche:\n1. Milch\n2. Brot\n3. Eier", Rules("Ich brauche erstens Milch, zweitens Brot und drittens Eier."));
        Assert.Equal("1. Buy milk\n2. Call mom", Rules("First buy milk second call mom"));
    }

    [Fact]
    public void NoListForSingleOrdinal() => Assert.Equal("Ich war erstens müde.", Rules("Ich war erstens müde"));

    [Fact]
    public void CasualDropsFinalPeriod()
    {
        Assert.Equal("Bin gleich da", Rules("bin gleich da.", new CleanupOptions(Style: WritingStyle.Casual)));
        Assert.Equal("Kommst du?", Rules("Kommst du?", new CleanupOptions(Style: WritingStyle.Casual)));
    }

    [Fact]
    public void VeryCasualLowercases() =>
        Assert.Equal("hallo lisa, passt dir donnerstag", Rules("Hallo Lisa, passt dir Donnerstag.", new CleanupOptions(Style: WritingStyle.VeryCasual)));

    [Fact]
    public void RawKeepsWordsButCleansFillers() =>
        Assert.Equal("Git status zeigen", Rules("äh git status zeigen", new CleanupOptions(Style: WritingStyle.Raw)));

    [Fact]
    public void ShortTextGetsNoPeriod() => Assert.Equal("Okay danke", Rules("okay danke"));

    [Fact]
    public void CapitalizationRespectsAbbreviations()
    {
        Assert.Equal("Das ist z. B. gut. Wir gehen am 3. oktober", TextPipeline.CapitalizeSentences("das ist z. B. gut. wir gehen am 3. oktober"));
        Assert.Equal("Hallo!wie geht's? Gut", TextPipeline.CapitalizeSentences("hallo!wie geht's? gut"));
    }

    [Fact]
    public void Spacing()
    {
        Assert.Equal("Hallo, wie geht es (gut)?", TextPipeline.FixSpacing("Hallo ,  wie geht es ( gut ) ?"));
        Assert.Equal("eins, zwei", TextPipeline.FixSpacing("eins,zwei"));
        Assert.Equal("Preis 3,50 €", TextPipeline.FixSpacing("Preis 3,50 €"));
    }

    [Fact]
    public void StripsWhisperHallucinations()
    {
        Assert.Equal("", TextPipeline.StripHallucinations("[BLANK_AUDIO]"));
        Assert.Equal("", TextPipeline.StripHallucinations("Untertitel im Auftrag des ZDF, 2021"));
        Assert.Equal("Hallo zusammen.", TextPipeline.StripHallucinations("Hallo zusammen. Vielen Dank fürs Zuschauen!"));
        Assert.Equal("Wir starten jetzt", TextPipeline.StripHallucinations("(Musik) Wir starten jetzt"));
        Assert.Equal("Ganz normaler Satz.", TextPipeline.StripHallucinations("Ganz normaler Satz."));
    }

    [Fact]
    public void DictionaryInPipeline()
    {
        var dict = new[] { new DictionaryEntry("Voltage Events", "voltage event's"), new DictionaryEntry("Beeni") };
        Assert.Equal("Der gig von Voltage Events mit Beeni war stark.", Rules("der gig von voltage event's mit beeni war stark", dict: dict));
    }

    [Fact]
    public void WisprFlowSpelling()
    {
        // Echter Fall aus dem ersten Test auf dem Mac
        var dict = new[] { new DictionaryEntry("Wispr Flow", "Whisperflow", "whisper flow") };
        Assert.Equal("Sie ist wie Wispr Flow.", Rules("Sie ist wie Whisperflow.", dict: dict));
    }
}
