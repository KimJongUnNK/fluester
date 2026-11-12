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

final class MicrophoneTests: XCTestCase {
    // USB-Mikro ohne Seriennummer: Die Location-ID (vorletztes Feld) hängt am Port
    let devices = [
        MicrophoneMatch.Candidate(uid: "BuiltInMicrophoneDevice", name: "MacBook Pro-Mikrofon"),
        MicrophoneMatch.Candidate(uid: "AppleUSBAudioEngine:Unknown Manufacturer:USB PnP Sound Device:14130000:1", name: "USB PnP Sound Device"),
        MicrophoneMatch.Candidate(uid: "AppleUSBAudioEngine:Blue Microphones:Yeti Stereo Microphone:REV8:1,2", name: "Yeti Stereo Microphone")
    ]

    func testExactUIDWins() {
        XCTAssertEqual(MicrophoneMatch.find(devices, uid: "BuiltInMicrophoneDevice", name: "egal"), 0)
        XCTAssertNil(MicrophoneMatch.find(devices, uid: "", name: "Yeti Stereo Microphone"))
    }

    func testFindsUSBMicrophoneOnAnotherPortOrHub() {
        // Gemerkt an Port 0x14200000, jetzt an 0x14130000 (z. B. über einen Hub)
        XCTAssertEqual(MicrophoneMatch.find(devices, uid: "AppleUSBAudioEngine:Unknown Manufacturer:USB PnP Sound Device:14200000:1", name: ""), 1)
        // Ganz andere UID, gleicher Name (z. B. nach Firmware-Update)
        XCTAssertEqual(MicrophoneMatch.find(devices, uid: "alt", name: "  yeti   stereo microphone "), 2)
        XCTAssertNil(MicrophoneMatch.find(devices, uid: "alt", name: ""))
        XCTAssertNil(MicrophoneMatch.find(devices, uid: "alt", name: "RØDE NT-USB"))
    }

    func testTwoIdenticalMicrophonesAreNotGuessed() {
        let two = [
            MicrophoneMatch.Candidate(uid: "AppleUSBAudioEngine:Generic:USB Audio:14100000:1", name: "USB Audio"),
            MicrophoneMatch.Candidate(uid: "AppleUSBAudioEngine:Generic:USB Audio:14300000:1", name: "USB Audio")
        ]
        XCTAssertNil(MicrophoneMatch.find(two, uid: "AppleUSBAudioEngine:Generic:USB Audio:14200000:1", name: "USB Audio"))
    }

    func testStableUID() {
        XCTAssertEqual(MicrophoneMatch.stableUID("AppleUSBAudioEngine:A:B:14200000:1"), "AppleUSBAudioEngine:A:B:1")
        XCTAssertEqual(MicrophoneMatch.stableUID("BuiltInMicrophoneDevice"), "BuiltInMicrophoneDevice")
        XCTAssertEqual(MicrophoneMatch.stableUID("AppleUSBAudioEngine:kurz:1"), "AppleUSBAudioEngine:kurz:1")
    }

    func testDownmixKeepsVoiceOnASingleChannel() {
        // Audio-Interface: Mikro an Eingang 2, Eingang 1 leer → volle Lautstärke statt halber oder Stille
        XCTAssertEqual(AudioUtil.downmix([[0, 0, 0], [0.5, -0.5, 0.25]]), [0.5, -0.5, 0.25])
    }

    func testDownmixAveragesActiveChannels() {
        let both = AudioUtil.downmix([[0.2, -0.2], [0.4, -0.2]])
        XCTAssertEqual(both[0], 0.3, accuracy: 1e-6)
        XCTAssertEqual(both[1], -0.2, accuracy: 1e-6)
        // Rauschen 30 dB unter der Stimme zählt nicht mit
        XCTAssertEqual(AudioUtil.downmix([[0.5, -0.5], [0.01, -0.01]]), [0.5, -0.5])
        XCTAssertEqual(AudioUtil.downmix([[0, 0], [0, 0]]), [0, 0])
        XCTAssertEqual(AudioUtil.downmix([[0.1, 0.2]]), [0.1, 0.2])
        XCTAssertEqual(AudioUtil.downmix([]), [])
    }

    func testMicrophoneNameInSettings() throws {
        var s = Settings()
        s.microphoneUID = "AppleUSBAudioEngine:A:B:14200000:1"
        s.microphoneName = "USB PnP Sound Device"
        let back = try JSONDecoder().decode(Settings.self, from: JSONEncoder().encode(s))
        XCTAssertEqual(back.microphoneName, "USB PnP Sound Device")
        let old = try JSONDecoder().decode(Settings.self, from: #"{"microphoneUID":"x"}"#.data(using: .utf8)!)
        XCTAssertEqual(old.microphoneUID, "x")
        XCTAssertEqual(old.microphoneName, "")
    }
}

final class RobustnessTests: XCTestCase {
    private func rules(_ s: String) -> String {
        TextPipeline.ruleBased(s, options: CleanupOptions(style: .formal))
    }

    func testStuttersAcrossACommaOnlyAtTheStartOfAClause() {
        // Zwei Teilsätze – das zweite „ich“ ist kein Stotterer
        XCTAssertEqual(rules("Das glaube ich, ich bin sicher"), "Das glaube ich, ich bin sicher.")
        XCTAssertEqual(rules("Das meine ich, ich meine es ernst"), "Das meine ich, ich meine es ernst.")
        XCTAssertEqual(rules("Ich, ich wollte fragen"), "Ich wollte fragen.")
        XCTAssertEqual(rules("Ähm, ich, ich wollte fragen"), "Ich wollte fragen.")
        XCTAssertEqual(rules("Gut. Ich, ich wollte fragen"), "Gut. Ich wollte fragen.")
        XCTAssertEqual(rules("wir wir müssen los"), "Wir müssen los.")
    }

    func testDictionaryEntryWithoutIDDoesNotWipeTheDictionary() throws {
        let json = #"{"dictionary":[{"term":"Beeni"},{"id":"0E6C9D5B-7E0B-4C38-9D57-1D7C2B0F6A11","term":"Exodus","soundsLike":["exodos"]}],"snippets":[{"trigger":"adresse","text":"Musterweg 1"}]}"#
        let s = try JSONDecoder().decode(Settings.self, from: json.data(using: .utf8)!)
        XCTAssertEqual(s.dictionary.map(\.term), ["Beeni", "Exodus"])
        XCTAssertEqual(s.dictionary[1].soundsLike, ["exodos"])
        XCTAssertEqual(s.snippets.first?.text, "Musterweg 1")
    }
}

final class UpdatesTests: XCTestCase {
    func testIsNewer() {
        XCTAssertTrue(Updates.isNewer("1.3.2", than: "1.3.1"))
        XCTAssertTrue(Updates.isNewer("1.10.0", than: "1.9.9"))      // Zahlen, nicht Text
        XCTAssertTrue(Updates.isNewer("v2.0.0", than: "1.3.1"))
        XCTAssertTrue(Updates.isNewer("1.3.1", than: "0.0.214"))     // CI-Bauten
        XCTAssertFalse(Updates.isNewer("1.3.1", than: "1.3.1"))
        XCTAssertFalse(Updates.isNewer("1.3.0", than: "1.3.1"))
        XCTAssertFalse(Updates.isNewer("1.3", than: "1.3.0"))
        XCTAssertFalse(Updates.isNewer("kaputt", than: "1.0.0"))
        XCTAssertFalse(Updates.isNewer(nil, than: "1.0.0"))
        XCTAssertFalse(Updates.isNewer("1.0.0", than: "dev"))
    }

    func testParseLatest() {
        XCTAssertEqual(Updates.parseLatest(#"{"tag_name":"v1.3.1"}"#.data(using: .utf8)!), "1.3.1")
        XCTAssertNil(Updates.parseLatest(#"{"message":"API rate limit exceeded"}"#.data(using: .utf8)!))
        XCTAssertNil(Updates.parseLatest(#"{"tag_name":"nightly"}"#.data(using: .utf8)!))
        XCTAssertNil(Updates.parseLatest("<html>".data(using: .utf8)!))
    }

    func testChecksum() {
        let a = String(repeating: "a", count: 64), b = String(repeating: "B", count: 64)
        let sums = "\(a)  Fluester-macOS.zip\n\(b) *Fluester-Windows-x64.zip\r\nkaputt Fluester-Linux-x64.tar.gz\n"
        XCTAssertEqual(Updates.checksum(in: sums, for: "Fluester-macOS.zip"), a)
        XCTAssertEqual(Updates.checksum(in: sums, for: "Fluester-Windows-x64.zip"), b.lowercased())
        XCTAssertNil(Updates.checksum(in: sums, for: "Fluester-Linux-x64.tar.gz"))
    }
}
