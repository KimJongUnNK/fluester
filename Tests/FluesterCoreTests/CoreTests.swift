import XCTest
@testable import FluesterCore

final class VocabularyTests: XCTestCase {
    func testWholeWordReplacementIsCaseInsensitive() {
        XCTAssertEqual(Vocabulary.replaceWholeWords(in: "ich war bei voltage   events", find: "Voltage Events", with: "Voltage Events"), "ich war bei Voltage Events")
        XCTAssertEqual(Vocabulary.replaceWholeWords(in: "Beenis Set", find: "beeni", with: "Beeni"), "Beenis Set") // nicht im Wortinneren
    }

    func testSnippetWholeUtterance() {
        let s = [Snippet(trigger: "meine Adresse", text: "Musterstraße 1, 6850 Dornbirn")]
        XCTAssertEqual(Vocabulary.expandSnippets("Meine Adresse.", s).text, "Musterstraße 1, 6850 Dornbirn")
        XCTAssertTrue(Vocabulary.expandSnippets("meine adresse", s).matchedWhole)
        XCTAssertTrue(Vocabulary.expandSnippets("Füge meine Adresse ein", s).matchedWhole)
    }

    func testSnippetInsideSentence() {
        let s = [Snippet(trigger: "mein Calendly Link", text: "https://calendly.com/emil")]
        let r = Vocabulary.expandSnippets("Buch dir einen Termin über mein Calendly Link", s)
        XCTAssertEqual(r.text, "Buch dir einen Termin über https://calendly.com/emil")
        XCTAssertFalse(r.matchedWhole)
    }

    func testHintsDeduplicate() {
        let d = [DictionaryEntry(term: "Beeni"), DictionaryEntry(term: "beeni"), DictionaryEntry(term: "Exodus")]
        XCTAssertEqual(Vocabulary.hints(d), ["Beeni", "Exodus"])
    }

    func testNormalize() {
        XCTAssertEqual(Vocabulary.normalize("  Meine, Adresse!! "), "meine adresse")
    }
}

final class PromptsTests: XCTestCase {
    func testSanitizeStripsWrappers() {
        XCTAssertEqual(Prompts.sanitize("\"Hallo Welt.\"", input: "hallo welt"), "Hallo Welt.")
        XCTAssertEqual(Prompts.sanitize("Hier ist der bereinigte Text: Hallo Welt.", input: "hallo welt"), "Hallo Welt.")
        XCTAssertEqual(Prompts.sanitize("<diktat>Hallo</diktat>", input: "hallo"), "Hallo")
        XCTAssertEqual(Prompts.sanitize("<think>hmm</think>\nHallo.", input: "hallo"), "Hallo.")
        XCTAssertEqual(Prompts.sanitize("```\nHallo\n```", input: "hallo"), "Hallo")
    }

    func testSanitizeRejectsAnswers() {
        let input = "was ist die hauptstadt von frankreich"
        let answer = String(repeating: "Die Hauptstadt von Frankreich ist Paris und sie hat viele Sehenswürdigkeiten. ", count: 4)
        XCTAssertNil(Prompts.sanitize(answer, input: input))
        XCTAssertNil(Prompts.sanitize("Als KI kann ich das nicht", input: "bitte schreib das auf"))
    }

    func testCleanupPromptContainsRules() {
        let p = Prompts.cleanupSystem(style: .casual, appName: "WhatsApp", languageName: "Deutsch",
                                      dictionary: [DictionaryEntry(term: "Voltage Events")], selfCorrections: true, formatLists: true)
        XCTAssertTrue(p.contains("Voltage Events"))
        XCTAssertTrue(p.contains("WhatsApp"))
        XCTAssertTrue(p.contains("NICHT an dich gerichtet"))
        XCTAssertTrue(p.contains("Selbstkorrekturen"))
        XCTAssertEqual(Prompts.cleanupUser("hallo"), "<diktat>\nhallo\n</diktat>")
    }

    func testCommandUser() {
        XCTAssertEqual(Prompts.commandUser(instruction: "kürzer", selection: "Langer Text"), "<anweisung>kürzer</anweisung>\n<text>\nLanger Text\n</text>")
        XCTAssertEqual(Prompts.commandUser(instruction: "schreib eine Absage", selection: "  "), "<anweisung>schreib eine Absage</anweisung>")
    }
}

final class AudioTests: XCTestCase {
    func testWavHeader() {
        let d = AudioUtil.wav(samples: [0, 0.5, -0.5, 1], sampleRate: 16000)
        XCTAssertEqual(d.count, 44 + 8)
        XCTAssertEqual(String(data: d.prefix(4), encoding: .ascii), "RIFF")
        XCTAssertEqual(String(data: d.subdata(in: 8..<12), encoding: .ascii), "WAVE")
        let rate = d.subdata(in: 24..<28).withUnsafeBytes { $0.loadUnaligned(as: UInt32.self) }
        XCTAssertEqual(UInt32(littleEndian: rate), 16000)
        let last = d.subdata(in: 50..<52).withUnsafeBytes { $0.loadUnaligned(as: Int16.self) }
        XCTAssertEqual(Int16(littleEndian: last), 32767)
    }

    func testSpeechDetection() {
        let silence = [Float](repeating: 0.001, count: 16000)
        XCTAssertFalse(AudioUtil.hasSpeech(silence, sampleRate: 16000))
        let tone = (0..<16000).map { Float(sin(Double($0) * 0.2) * 0.3) }
        XCTAssertTrue(AudioUtil.hasSpeech(tone, sampleRate: 16000))
    }

    func testTrimSilenceKeepsSpeech() {
        var s = [Float](repeating: 0, count: 16000)
        s += (0..<8000).map { Float(sin(Double($0) * 0.2) * 0.3) }
        s += [Float](repeating: 0, count: 16000)
        let t = AudioUtil.trimSilence(s, sampleRate: 16000)
        XCTAssertLessThan(t.count, s.count)
        XCTAssertGreaterThanOrEqual(t.count, 8000)
    }

    func testResample() {
        let s = [Float](repeating: 0.5, count: 48000)
        XCTAssertEqual(AudioUtil.resample(s, from: 48000, to: 16000).count, 16000)
    }
}

final class GestureTests: XCTestCase {
    func testHoldAndRelease() {
        let g = HotkeyGestures()
        XCTAssertEqual(g.press(at: 0, command: false), .startHold(command: false))
        XCTAssertEqual(g.release(at: 1.2), .stopAndProcess)
        XCTAssertEqual(g.state, .idle)
    }

    func testDoubleTapGoesHandsFree() {
        let g = HotkeyGestures()
        _ = g.press(at: 0, command: false)
        XCTAssertEqual(g.release(at: 0.1), .none)
        XCTAssertEqual(g.press(at: 0.3, command: false), .switchToHandsFree)
        XCTAssertEqual(g.release(at: 0.35), .none)
        XCTAssertEqual(g.state, .handsFree)
        XCTAssertEqual(g.press(at: 5, command: false), .none)
        XCTAssertEqual(g.release(at: 5.1), .stopAndProcess)
    }

    func testSingleTapCancelsAfterWindow() {
        let g = HotkeyGestures()
        _ = g.press(at: 0, command: false)
        _ = g.release(at: 0.1)
        XCTAssertEqual(g.tick(at: 0.3), .none)
        XCTAssertEqual(g.tick(at: 0.6), .cancel)
    }

    func testOtherKeyCancels() {
        let g = HotkeyGestures()
        _ = g.press(at: 0, command: false)
        g.otherKeyPressed()
        XCTAssertEqual(g.release(at: 0.5), .cancel)
    }

    func testCommandFlag() {
        let g = HotkeyGestures()
        XCTAssertEqual(g.press(at: 0, command: true), .startHold(command: true))
    }
}

final class SettingsTests: XCTestCase {
    func testDecodesOldJSONWithMissingKeys() throws {
        let json = #"{"hotkey":"rightOption","language":"de-DE"}"#.data(using: .utf8)!
        let s = try JSONDecoder().decode(Settings.self, from: json)
        XCTAssertEqual(s.hotkey, .rightOption)
        XCTAssertEqual(s.language, "de-DE")
        XCTAssertEqual(s.engine, .apple)
        XCTAssertTrue(s.removeFillers)
    }

    func testRoundTrip() throws {
        var s = Settings()
        s.snippets = [Snippet(trigger: "a", text: "b")]
        s.styles.set(.excited, for: .messages)
        let back = try JSONDecoder().decode(Settings.self, from: JSONEncoder().encode(s))
        XCTAssertEqual(back, s)
        XCTAssertEqual(back.styles.style(for: .messages), .excited)
    }

    func testCategorizer() {
        XCTAssertEqual(AppCategorizer.category(bundleID: "net.whatsapp.WhatsApp"), .messages)
        XCTAssertEqual(AppCategorizer.category(bundleID: "com.apple.mail"), .email)
        XCTAssertEqual(AppCategorizer.category(bundleID: "com.jetbrains.intellij"), .code)
        XCTAssertEqual(AppCategorizer.category(bundleID: "com.google.Chrome", windowTitle: "Posteingang – Gmail"), .email)
        XCTAssertEqual(AppCategorizer.category(bundleID: "com.apple.Notes"), .other)
        XCTAssertEqual(AppCategorizer.category(bundleID: "com.apple.Notes", overrides: ["com.apple.Notes": .work]), .work)
    }
}

final class StatsTests: XCTestCase {
    func testSummary() {
        let now = Date()
        let items = [
            HistoryItem(date: now, raw: "", text: "eins zwei drei vier", appName: "", bundleID: "", seconds: 2),
            HistoryItem(date: now.addingTimeInterval(-86400), raw: "", text: "fünf sechs", appName: "", bundleID: "", seconds: 4),
            HistoryItem(date: now, raw: "", text: "kürzer", appName: "", bundleID: "", seconds: 1, isCommand: true)
        ]
        let s = Stats.summary(items, now: now)
        XCTAssertEqual(s.totalWords, 6)
        XCTAssertEqual(s.todayWords, 4)
        XCTAssertEqual(s.dictations, 2)
        XCTAssertEqual(s.wpm, 60)
        XCTAssertEqual(s.streakDays, 2)
    }

    func testWordCountIgnoresPunctuation() {
        XCTAssertEqual(Stats.wordCount("Hallo, – Welt !"), 2)
    }
}

final class APITests: XCTestCase {
    func testTranscriptionRequest() throws {
        let c = OpenAICompatibleClient(baseURL: "https://api.groq.com/openai/v1/", apiKey: "k")
        let r = try c.transcriptionRequest(wav: Data([1, 2]), model: "whisper-large-v3-turbo", language: "de-DE", prompt: "Beeni")
        XCTAssertEqual(r.url?.absoluteString, "https://api.groq.com/openai/v1/audio/transcriptions")
        XCTAssertEqual(r.value(forHTTPHeaderField: "Authorization"), "Bearer k")
        let body = String(decoding: r.httpBody ?? Data(), as: UTF8.self)
        XCTAssertTrue(body.contains("name=\"language\"\r\n\r\nde\r\n"))
        XCTAssertTrue(body.contains("name=\"prompt\"\r\n\r\nBeeni\r\n"))
        XCTAssertTrue(body.contains("filename=\"diktat.wav\""))
    }

    func testChatParsing() throws {
        let json = #"{"choices":[{"message":{"role":"assistant","content":"Hallo."}}]}"#.data(using: .utf8)!
        XCTAssertEqual(try OpenAICompatibleClient.parseChat(json), "Hallo.")
        XCTAssertEqual(try OpenAICompatibleClient.parseTranscription(#"{"text":" Hallo Welt "}"#.data(using: .utf8)!), "Hallo Welt")
    }

    func testErrorMessages() {
        let body = #"{"error":{"message":"Invalid API Key"}}"#.data(using: .utf8)!
        XCTAssertEqual(OpenAICompatibleClient.describe(status: 401, body: body), "API-Schlüssel ungültig oder fehlt (Invalid API Key)")
    }
}
