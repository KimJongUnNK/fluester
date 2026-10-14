import XCTest
@testable import FluesterCore

final class TextPipelineTests: XCTestCase {
    let formal = CleanupOptions(style: .formal)

    func rules(_ s: String, _ o: CleanupOptions? = nil, dict: [DictionaryEntry] = []) -> String {
        TextPipeline.ruleBased(s, options: o ?? formal, dictionary: dict)
    }

    // MARK: Füllwörter

    func testRemovesGermanFillers() {
        XCTAssertEqual(rules("Ich, äh, wollte morgen kommen"), "Ich wollte morgen kommen.")
        XCTAssertEqual(rules("Äh, ich wollte ähm fragen, ob das passt?"), "Ich wollte fragen, ob das passt?")
        XCTAssertEqual(rules("Öhm also das ist gut"), "Also das ist gut.")
    }

    func testKeepsCommaBeforeConjunction() {
        XCTAssertEqual(rules("Das Problem ist, äh, dass wir zu spät sind"), "Das Problem ist, dass wir zu spät sind.")
    }

    func testRemovesEnglishFillers() {
        XCTAssertEqual(rules("Um, I think, uh, we should go"), "I think we should go.")
        XCTAssertEqual(rules("So, uhm, let's meet tomorrow"), "So let's meet tomorrow.")
    }

    func testGermanUmIsNotAFiller() {
        XCTAssertEqual(rules("Wir treffen uns um 4 Uhr"), "Wir treffen uns um 4 Uhr.")
        XCTAssertEqual(rules("Das ist eh klar"), "Das ist eh klar.")
    }

    func testStutters() {
        XCTAssertEqual(rules("ich ich ich wollte nur sagen"), "Ich wollte nur sagen.")
        XCTAssertEqual(rules("Die Frau, die die Tür öffnet"), "Die Frau, die die Tür öffnet.")
        XCTAssertEqual(rules("Ja ja, passt"), "Ja ja, passt.")
    }

    func testKeepsNewlinesAroundFillers() {
        XCTAssertEqual(TextPipeline.preprocess("Hallo\näh, wie geht es", options: formal), "Hallo\nwie geht es")
    }

    // MARK: Befehle

    func testSpokenNewLineAndParagraph() {
        XCTAssertEqual(rules("Hallo Lisa, neue Zeile, wie geht es dir"), "Hallo Lisa\nWie geht es dir.")
        XCTAssertEqual(rules("Liebe Grüße. Neuer Absatz. Emil"), "Liebe Grüße.\n\nEmil")
        XCTAssertEqual(rules("Thanks new line best Emil"), "Thanks\nBest Emil")
    }

    func testSpokenPunctuationOnlyWhenEnabled() {
        var o = formal
        XCTAssertEqual(rules("Der Punkt ist wichtig", o), "Der Punkt ist wichtig.")
        o.spokenPunctuation = true
        XCTAssertEqual(rules("Hallo Komma wie geht es dir Fragezeichen", o), "Hallo, wie geht es dir?")
    }

    // MARK: Selbstkorrekturen

    func testSelfCorrectionWithAnchor() {
        XCTAssertEqual(rules("Ich komme um 3, ich meine um 4 Uhr"), "Ich komme um 4 Uhr.")
        XCTAssertEqual(rules("Schick es an Paul, ich meine an Peter"), "Schick es an Peter.")
        XCTAssertEqual(rules("Let's meet at 5, I mean at 6"), "Let's meet at 6.")
    }

    func testSelfCorrectionSingleWord() {
        XCTAssertEqual(rules("Wir treffen uns am Montag, ich meine Dienstag"), "Wir treffen uns am Dienstag.")
    }

    func testUnclearCorrectionLeavesText() {
        XCTAssertEqual(rules("Das ist gut, ich meine, wirklich gut"), "Das ist gut, ich meine, wirklich gut.")
        XCTAssertEqual(rules("Ich meine, das ist gut"), "Ich meine, das ist gut.")
    }

    func testScratchThat() {
        XCTAssertEqual(rules("Wir treffen uns morgen. Streich das. Wir treffen uns übermorgen."), "Wir treffen uns übermorgen.")
        XCTAssertEqual(rules("Hallo Paul. Ich nehme den Bus, streich das, ich nehme das Rad"), "Hallo Paul. Ich nehme das Rad.")
    }

    // MARK: Listen

    func testNumberedList() {
        XCTAssertEqual(rules("Ich brauche erstens Milch, zweitens Brot und drittens Eier."),
                       "Ich brauche:\n1. Milch\n2. Brot\n3. Eier")
        XCTAssertEqual(rules("First buy milk second call mom"), "1. Buy milk\n2. Call mom")
    }

    func testNoListForSingleOrdinal() {
        XCTAssertEqual(rules("Ich war erstens müde"), "Ich war erstens müde.")
    }

    // MARK: Stil

    func testCasualDropsFinalPeriod() {
        XCTAssertEqual(rules("bin gleich da.", CleanupOptions(style: .casual)), "Bin gleich da")
        XCTAssertEqual(rules("Kommst du?", CleanupOptions(style: .casual)), "Kommst du?")
    }

    func testVeryCasualLowercases() {
        XCTAssertEqual(rules("Hallo Lisa, passt dir Donnerstag.", CleanupOptions(style: .veryCasual)), "hallo lisa, passt dir donnerstag")
    }

    func testRawKeepsWordsButCleansFillers() {
        XCTAssertEqual(rules("äh git status zeigen", CleanupOptions(style: .raw)), "Git status zeigen")
    }

    func testShortTextGetsNoPeriod() {
        XCTAssertEqual(rules("okay danke"), "Okay danke")
    }

    // MARK: Großschreibung

    func testCapitalizationRespectsAbbreviations() {
        XCTAssertEqual(TextPipeline.capitalizeSentences("das ist z. B. gut. wir gehen am 3. oktober"), "Das ist z. B. gut. Wir gehen am 3. oktober")
        XCTAssertEqual(TextPipeline.capitalizeSentences("hallo!wie geht's? gut"), "Hallo!wie geht's? Gut")
    }

    func testSpacing() {
        XCTAssertEqual(TextPipeline.fixSpacing("Hallo ,  wie geht es ( gut ) ?"), "Hallo, wie geht es (gut)?")
        XCTAssertEqual(TextPipeline.fixSpacing("eins,zwei"), "eins, zwei")
        XCTAssertEqual(TextPipeline.fixSpacing("Preis 3,50 €"), "Preis 3,50 €")
    }

    // MARK: Halluzinationen

    func testStripsWhisperHallucinations() {
        XCTAssertEqual(TextPipeline.stripHallucinations("[BLANK_AUDIO]"), "")
        XCTAssertEqual(TextPipeline.stripHallucinations("Untertitel im Auftrag des ZDF, 2021"), "")
        XCTAssertEqual(TextPipeline.stripHallucinations("Hallo zusammen. Vielen Dank fürs Zuschauen!"), "Hallo zusammen.")
        XCTAssertEqual(TextPipeline.stripHallucinations("(Musik) Wir starten jetzt"), "Wir starten jetzt")
        XCTAssertEqual(TextPipeline.stripHallucinations("Ganz normaler Satz."), "Ganz normaler Satz.")
    }

    // MARK: Wörterbuch

    func testDictionaryInPipeline() {
        let dict = [DictionaryEntry(term: "Voltage Events", soundsLike: ["voltage event's"]), DictionaryEntry(term: "Beeni")]
        XCTAssertEqual(rules("der gig von voltage event's mit beeni war stark", dict: dict), "Der gig von Voltage Events mit Beeni war stark.")
    }
}
