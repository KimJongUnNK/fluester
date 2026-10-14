import SwiftUI
import AppKit
import FluesterCore

enum SettingsTab: String, CaseIterable, Identifiable {
    case general, recognition, ai, styles, dictionary, snippets, history, permissions, about
    var id: String { rawValue }
    var title: String {
        switch self {
        case .general: return "Allgemein"
        case .recognition: return "Erkennung"
        case .ai: return "KI-Bereinigung"
        case .styles: return "Stile"
        case .dictionary: return "Wörterbuch"
        case .snippets: return "Textbausteine"
        case .history: return "Verlauf"
        case .permissions: return "Berechtigungen"
        case .about: return "Über Flüster"
        }
    }
    var icon: String {
        switch self {
        case .general: return "gearshape.fill"
        case .recognition: return "waveform"
        case .ai: return "sparkles"
        case .styles: return "textformat"
        case .dictionary: return "character.book.closed.fill"
        case .snippets: return "text.badge.plus"
        case .history: return "clock.fill"
        case .permissions: return "hand.raised.fill"
        case .about: return "info.circle.fill"
        }
    }
    var color: Color {
        switch self {
        case .general: return .gray
        case .recognition: return .blue
        case .ai: return .purple
        case .styles: return .orange
        case .dictionary: return .brown
        case .snippets: return .teal
        case .history: return .indigo
        case .permissions: return .red
        case .about: return .gray
        }
    }
}

final class SettingsTabState: ObservableObject {
    @Published var tab: SettingsTab = .general
}

/// Farbiges Symbol wie in den Systemeinstellungen
struct IconTile: View {
    let symbol: String
    let color: Color
    var body: some View {
        Image(systemName: symbol)
            .font(.system(size: 11, weight: .semibold))
            .foregroundStyle(.white)
            .frame(width: 22, height: 22)
            .background(RoundedRectangle(cornerRadius: 5.5, style: .continuous).fill(color.gradient))
    }
}

struct SettingsView: View {
    @ObservedObject var store: SettingsStore
    @ObservedObject var history: HistoryStore
    @ObservedObject var tabState: SettingsTabState
    let delegate: AppDelegate

    var body: some View {
        let selection = Binding<SettingsTab?>(get: { tabState.tab }, set: { if let t = $0 { tabState.tab = t } })
        NavigationSplitView {
            List(selection: selection) {
                ForEach(SettingsTab.allCases) { t in
                    Label { Text(t.title) } icon: { IconTile(symbol: t.icon, color: t.color) }
                        .tag(t)
                }
            }
            .navigationSplitViewColumnWidth(min: 190, ideal: 210)
        } detail: {
            detail
                .frame(minWidth: 500, minHeight: 480)
                .navigationTitle(tabState.tab.title)
        }
    }

    @ViewBuilder private var detail: some View {
        switch tabState.tab {
        case .general: GeneralPane(store: store)
        case .recognition: RecognitionPane(store: store)
        case .ai: AIPane(store: store)
        case .styles: StylesPane(store: store)
        case .dictionary: DictionaryPane(store: store)
        case .snippets: SnippetsPane(store: store)
        case .history: HistoryPane(history: history)
        case .permissions: PermissionsPane(delegate: delegate)
        case .about: AboutPane()
        }
    }
}

// MARK: - Allgemein

struct GeneralPane: View {
    @ObservedObject var store: SettingsStore
    @State private var devices: [AudioDevices.Device] = []
    @State private var loginError: String?

    var body: some View {
        Form {
            Section {
                Picker("Diktat-Taste", selection: $store.s.hotkey) {
                    ForEach(HotkeyChoice.allCases) { Text($0.label).tag($0) }
                }
                Toggle("Doppeltipp: freihändig diktieren (nochmal tippen zum Beenden)", isOn: $store.s.handsFreeDoubleTap)
                Toggle("Command Mode: Taste + ⌃ halten, markierten Text per Sprache bearbeiten", isOn: $store.s.commandModeEnabled)
                if store.s.hotkey == .fn {
                    HStack(alignment: .top, spacing: 8) {
                        Image(systemName: "globe").foregroundStyle(.secondary)
                        Text("Damit Fn/🌐 nicht das Emoji-Fenster öffnet: Systemeinstellungen › Tastatur › „🌐-Taste drücken zum“ auf „Keine Aktion“ stellen.")
                            .font(.callout).foregroundStyle(.secondary)
                        Spacer()
                        Button("Öffnen") { Permissions.openKeyboardSettings() }
                    }
                }
            } header: { Text("Bedienung") } footer: {
                Text("Halten → sprechen → loslassen. Esc bricht ab.").foregroundStyle(.secondary)
            }

            Section("Einfügen") {
                Picker("Methode", selection: $store.s.insertMethod) {
                    ForEach(InsertMethod.allCases) { Text($0.label).tag($0) }
                }
                Toggle("Leerzeichen automatisch ergänzen", isOn: $store.s.smartSpacing)
            }

            Section("Mikrofon") {
                Picker("Eingang", selection: $store.s.microphoneUID) {
                    Text("Systemstandard").tag("")
                    ForEach(devices) { Text($0.name).tag($0.uid) }
                }
            }

            Section("Darstellung & System") {
                Toggle("Töne beim Start und Ende", isOn: $store.s.sounds)
                Toggle("Leiste auch im Ruhezustand zeigen", isOn: $store.s.showIdleBar)
                Toggle("Verlauf speichern (nur lokal)", isOn: $store.s.keepHistory)
                Toggle("Bei Anmeldung starten", isOn: Binding(get: { LoginItem.isEnabled }, set: { on in
                    loginError = LoginItem.set(on)
                    store.s.launchAtLogin = on
                }))
                if let e = loginError { Text(e).font(.caption).foregroundStyle(.red) }
            }
        }
        .formStyle(.grouped)
        .onAppear { devices = AudioDevices.inputs() }
    }
}

// MARK: - Erkennung

struct RecognitionPane: View {
    @ObservedObject var store: SettingsStore
    @ObservedObject var downloader = ModelDownloader.shared
    @State private var appleStatus: String?
    @State private var appleBusy = false

    var body: some View {
        Form {
            Section {
                Picker("Erkennung", selection: $store.s.engine) {
                    ForEach(EngineChoice.allCases) { Text($0.label).tag($0) }
                }
                .pickerStyle(.radioGroup)
                Picker("Sprache", selection: $store.s.language) {
                    ForEach(languageChoices) { Text($0.name).tag($0.code) }
                }
            } header: { Text("Spracherkennung") } footer: {
                Text(engineFooter).foregroundStyle(.secondary)
            }

            switch store.s.engine {
            case .apple: appleSection
            case .whisperLocal: whisperSection
            case .cloud:
                Section("Anbieter") {
                    ProviderEditor(config: $store.s.cloudTranscription, kind: .transcription)
                }
            }
        }
        .formStyle(.grouped)
    }

    private var engineFooter: String {
        switch store.s.engine {
        case .apple: return "Kostenlos, offline, nichts verlässt den Mac. Mit macOS 26 die neue SpeechAnalyzer-Erkennung."
        case .whisperLocal: return "Kostenlos, offline, sehr genau – auch gemischt Deutsch/Englisch. Braucht whisper.cpp und ein Modell (einmalig)."
        case .cloud: return "Am genauesten und schnell. Groq hat ein großzügiges Gratis-Kontingent (≈ 8 Stunden Audio pro Tag)."
        }
    }

    @ViewBuilder private var appleSection: some View {
        Section("Apple") {
            if #available(macOS 26.0, *) {
                LabeledContent("Engine", value: "SpeechAnalyzer (macOS 26)")
                HStack {
                    Text("Sprachpaket")
                    Spacer()
                    if appleBusy { ProgressView().controlSize(.small) }
                    if let s = appleStatus { Text(s).foregroundStyle(.secondary) }
                    Button("Jetzt laden") {
                        appleBusy = true
                        appleStatus = nil
                        let lang = store.s.language
                        Task {
                            do {
                                try await AppleSpeechEngine.prepare(language: lang)
                                AppleAutoEngine.record(error: nil)
                                appleStatus = "Bereit ✓"
                            } catch {
                                appleStatus = error.localizedDescription
                            }
                            appleBusy = false
                        }
                    }
                    .disabled(appleBusy)
                }
                if let reason = AppleAutoEngine.fallbackReason {
                    Text("Gerade aktiv: ältere Apple-Erkennung (\(reason)). Flüster wechselt automatisch zurück, sobald SpeechAnalyzer bereit ist.")
                        .font(.callout).foregroundStyle(.secondary)
                }
            } else {
                LabeledContent("Engine", value: "SFSpeechRecognizer (macOS 14/15)")
                Text("Ab macOS 26 nutzt Flüster automatisch die deutlich bessere neue Apple-Erkennung.").font(.callout).foregroundStyle(.secondary)
            }
        }
    }

    @ViewBuilder private var whisperSection: some View {
        Section("whisper.cpp") {
            if let path = WhisperServer.binaryPath() {
                LabeledContent("whisper-server", value: "gefunden ✓")
                    .help(path)
            } else {
                VStack(alignment: .leading, spacing: 6) {
                    Text("whisper.cpp ist noch nicht installiert. Im Terminal:")
                    CopyableCommand(command: "brew install whisper-cpp")
                    Link("Homebrew installieren (brew.sh)", destination: URL(string: "https://brew.sh")!)
                }
            }
        }
        Section {
            ForEach(WhisperModel.all) { m in
                ModelRow(model: m, selectedID: $store.s.whisperModelID, downloader: downloader)
            }
            if let e = downloader.error { Text(e).foregroundStyle(.red) }
        } header: { Text("Modell") } footer: {
            Text("Modelle liegen in ~/Library/Application Support/Fluester/models.").foregroundStyle(.secondary)
        }
    }
}

struct ModelRow: View {
    let model: WhisperModel
    @Binding var selectedID: String
    @ObservedObject var downloader: ModelDownloader

    var body: some View {
        let installed = WhisperServer.isInstalled(model)
        let _ = downloader.revision
        HStack(spacing: 10) {
            Image(systemName: selectedID == model.id ? "largecircle.fill.circle" : "circle")
                .foregroundStyle(selectedID == model.id ? Color.accentColor : Color.secondary)
                .onTapGesture { selectedID = model.id }
            VStack(alignment: .leading, spacing: 2) {
                Text(model.name).fontWeight(.medium)
                Text("\(model.note) · \(model.sizeMB) MB").font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            if downloader.activeID == model.id {
                ProgressView(value: downloader.progress).frame(width: 110)
                Button("Stopp") { downloader.cancel() }
            } else if installed {
                Text("installiert").font(.caption).foregroundStyle(.green)
                Button(role: .destructive) { downloader.delete(model) } label: { Image(systemName: "trash") }
                    .buttonStyle(.borderless)
            } else {
                Button("Laden") {
                    selectedID = model.id
                    downloader.download(model)
                }
                .disabled(downloader.activeID != nil)
            }
        }
        .contentShape(Rectangle())
    }
}

struct CopyableCommand: View {
    let command: String
    @State private var copied = false
    var body: some View {
        HStack {
            Text(command).font(.system(.body, design: .monospaced)).textSelection(.enabled)
            Spacer()
            Button(copied ? "Kopiert ✓" : "Kopieren") {
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(command, forType: .string)
                copied = true
            }
        }
        .padding(8)
        .background(RoundedRectangle(cornerRadius: 6).fill(Color.primary.opacity(0.06)))
    }
}

// MARK: - Anbieter (Groq, OpenAI, Ollama, eigener)

struct ProviderEditor: View {
    enum Kind { case transcription, chat }
    @Binding var config: APIConfig
    let kind: Kind
    @State private var key = ""
    @State private var testResult: String?
    @State private var testing = false

    private var presets: [ProviderPreset] {
        kind == .transcription ? ProviderPreset.all.filter { $0.id != "ollama" } : ProviderPreset.all
    }

    var body: some View {
        let preset = ProviderPreset.byID(config.providerID)
        Picker("Anbieter", selection: Binding(get: { config.providerID }, set: { id in
            let p = ProviderPreset.byID(id)
            config.providerID = id
            if id != "custom" {
                config.baseURL = p.baseURL
                config.model = kind == .transcription ? p.transcriptionModel : p.chatModel
            }
        })) {
            ForEach(presets) { Text($0.name).tag($0.id) }
        }
        TextField("Server-Adresse", text: $config.baseURL, prompt: Text("https://…/v1"))
        TextField("Modell", text: $config.model, prompt: Text(kind == .transcription ? "whisper-large-v3-turbo" : "llama-3.3-70b-versatile"))
        if preset.needsKey || preset.id == "custom" {
            HStack {
                SecureField("API-Schlüssel", text: $key, prompt: Text(preset.needsKey ? "erforderlich" : "optional"))
                if !preset.keyURL.isEmpty, let u = URL(string: preset.keyURL) {
                    Link("Schlüssel holen", destination: u)
                }
            }
        }
        if preset.id == "ollama" {
            VStack(alignment: .leading, spacing: 6) {
                Text("Ollama läuft lokal und kostenlos. Einmalig installieren und Modell laden:").font(.callout)
                CopyableCommand(command: "brew install ollama && ollama pull \(config.model.isEmpty ? "qwen2.5:7b" : config.model)")
            }
        }
        HStack {
            Button(testing ? "Teste …" : "Verbindung testen") { test() }.disabled(testing)
            if let r = testResult { Text(r).foregroundStyle(r.hasPrefix("✓") ? Color.green : Color.red).lineLimit(2) }
        }
        .onAppear { key = Keychain.apiKey(for: config.providerID) ?? "" }
        .onChange(of: config.providerID) { _, id in key = Keychain.apiKey(for: id) ?? "" }
        .onChange(of: key) { _, k in Keychain.setAPIKey(k, for: config.providerID) }
    }

    private func test() {
        testing = true
        testResult = nil
        let cfg = config
        let k = key
        let kind = self.kind
        Task {
            do {
                let client = OpenAICompatibleClient(baseURL: cfg.baseURL, apiKey: k, timeout: 20)
                if kind == .chat {
                    let out = try await client.chat(model: cfg.model, system: "Antworte nur mit: ok", user: "Test")
                    testResult = "✓ Antwort: " + out.prefix(40)
                } else {
                    // 0,5 s leises Rauschen genügt, um Schlüssel und Modell zu prüfen
                    let noise = (0..<8000).map { _ in Float.random(in: -0.01...0.01) }
                    _ = try await client.transcribe(wav: AudioUtil.wav(samples: noise, sampleRate: 16000), model: cfg.model, language: nil, prompt: nil)
                    testResult = "✓ Verbindung steht"
                }
            } catch {
                testResult = error.localizedDescription
            }
            testing = false
        }
    }
}

// MARK: - KI-Bereinigung

struct AIPane: View {
    @ObservedObject var store: SettingsStore
    @State private var sample = "äh also ich wollte fragen ob wir uns am Montag ich meine am Dienstag um 3 treffen können"
    @State private var result = ""
    @State private var running = false

    var body: some View {
        Form {
            Section {
                Picker("Bereinigung", selection: $store.s.cleanup) {
                    ForEach(CleanupChoice.allCases) { Text($0.label).tag($0) }
                }
                .pickerStyle(.radioGroup)
                if store.s.cleanup == .appleIntelligence {
                    if let p = Rewriters.appleIntelligenceProblem() {
                        Label(p + " – bis dahin gelten die Regeln.", systemImage: "exclamationmark.triangle").foregroundStyle(.orange)
                    } else {
                        Label("Apple Intelligence bereit – kostenlos und auf dem Mac", systemImage: "checkmark.seal").foregroundStyle(.green)
                    }
                }
            } header: { Text("Wie der Text nach dem Sprechen aufgeräumt wird") } footer: {
                Text("Die KI entfernt Füllwörter, übernimmt Selbstkorrekturen, setzt Satzzeichen und formatiert Listen – wie Wispr Flow. Sie beantwortet nichts und erfindet nichts dazu.")
                    .foregroundStyle(.secondary)
            }

            if store.s.cleanup == .llm {
                Section("Sprachmodell") {
                    ProviderEditor(config: $store.s.cloudChat, kind: .chat)
                }
            }

            Section("Regeln") {
                Toggle("Füllwörter und Stotterer entfernen (äh, ähm, um, uh …)", isOn: $store.s.removeFillers)
                Toggle("Selbstkorrekturen übernehmen („um 3, ich meine um 4“ → „um 4“)", isOn: $store.s.applySelfCorrections)
                Toggle("Aufzählungen als Liste formatieren („erstens …, zweitens …“)", isOn: $store.s.formatLists)
                Toggle("„Neue Zeile“ und „Neuer Absatz“ als Befehl", isOn: $store.s.spokenCommands)
                Toggle("Satzzeichen sprechen („Komma“, „Fragezeichen“ …)", isOn: $store.s.spokenPunctuation)
            }

            Section("Ausprobieren") {
                TextField("Roher Text", text: $sample, axis: .vertical).lineLimit(2...5)
                HStack {
                    Button(running ? "Läuft …" : "Bereinigen") { run() }.disabled(running || sample.isEmpty)
                    Spacer()
                }
                if !result.isEmpty {
                    Text(result).textSelection(.enabled).padding(8).frame(maxWidth: .infinity, alignment: .leading)
                        .background(RoundedRectangle(cornerRadius: 6).fill(Color.accentColor.opacity(0.08)))
                }
            }
        }
        .formStyle(.grouped)
    }

    private func run() {
        running = true
        let s = store.s
        let text = sample
        Task {
            let r = await DictationController.cleanText(text, style: s.styles.other, appName: "Notizen", settings: s)
            result = r.text + (r.warning.map { "\n\n⚠︎ " + $0 } ?? "")
            running = false
        }
    }
}

// MARK: - Stile

struct StylesPane: View {
    @ObservedObject var store: SettingsStore

    var body: some View {
        Form {
            Section {
                ForEach(AppCategory.allCases) { cat in
                    VStack(alignment: .leading, spacing: 6) {
                        Picker(selection: Binding(get: { store.s.styles.style(for: cat) }, set: { store.s.styles.set($0, for: cat) })) {
                            ForEach(WritingStyle.allCases) { Text($0.label).tag($0) }
                        } label: {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(cat.label).fontWeight(.medium)
                                Text(cat.appsHint).font(.caption).foregroundStyle(.secondary)
                            }
                        }
                        Text("„" + store.s.styles.style(for: cat).example + "“")
                            .font(.callout).italic().foregroundStyle(.secondary)
                    }
                    .padding(.vertical, 2)
                }
            } header: { Text("Stil je App") } footer: {
                Text("Flüster erkennt die App, in die du diktierst – im Browser auch Gmail, WhatsApp Web, Slack & Co. am Fenstertitel.").foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }
}

// MARK: - Wörterbuch

struct DictionaryPane: View {
    @ObservedObject var store: SettingsStore
    @State private var newTerm = ""

    var body: some View {
        Form {
            Section {
                HStack {
                    TextField("Neues Wort oder Name", text: $newTerm, prompt: Text("z. B. Voltage Events"))
                        .onSubmit(add)
                    Button("Hinzufügen", action: add).disabled(newTerm.trimmingCharacters(in: .whitespaces).isEmpty)
                }
            } header: { Text("Wörterbuch") } footer: {
                Text("Namen, Marken und Fachbegriffe werden der Erkennung als Hinweis mitgegeben und immer so geschrieben. „Klingt wie“: Varianten, die die Erkennung falsch versteht.")
                    .foregroundStyle(.secondary)
            }
            Section {
                if store.s.dictionary.isEmpty {
                    Text("Noch leer.").foregroundStyle(.secondary)
                }
                ForEach($store.s.dictionary) { $entry in
                    HStack {
                        TextField("Schreibweise", text: $entry.term).frame(maxWidth: 200)
                        TextField("klingt wie (mit Komma trennen)", text: Binding(
                            get: { entry.soundsLike.joined(separator: ", ") },
                            set: { entry.soundsLike = $0.split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty } }))
                        Button(role: .destructive) {
                            store.s.dictionary.removeAll { $0.id == entry.id }
                        } label: { Image(systemName: "minus.circle.fill") }
                        .buttonStyle(.borderless)
                    }
                }
            }
        }
        .formStyle(.grouped)
    }

    private func add() {
        let t = newTerm.trimmingCharacters(in: .whitespaces)
        guard !t.isEmpty else { return }
        store.s.dictionary.insert(DictionaryEntry(term: t), at: 0)
        newTerm = ""
    }
}

// MARK: - Textbausteine

struct SnippetsPane: View {
    @ObservedObject var store: SettingsStore

    var body: some View {
        Form {
            Section {
                Button {
                    store.s.snippets.insert(Snippet(trigger: "", text: ""), at: 0)
                } label: { Label("Neuer Baustein", systemImage: "plus") }
            } header: { Text("Textbausteine") } footer: {
                Text("Sag das Stichwort (z. B. „meine Adresse“) – Flüster fügt den ganzen Text ein. Im Satz wird das Stichwort ersetzt.")
                    .foregroundStyle(.secondary)
            }
            ForEach($store.s.snippets) { $snip in
                Section {
                    HStack {
                        TextField("Stichwort", text: $snip.trigger, prompt: Text("z. B. mein Calendly-Link"))
                        Button(role: .destructive) {
                            store.s.snippets.removeAll { $0.id == snip.id }
                        } label: { Image(systemName: "trash") }
                        .buttonStyle(.borderless)
                    }
                    TextEditor(text: $snip.text)
                        .font(.body)
                        .frame(minHeight: 54)
                }
            }
        }
        .formStyle(.grouped)
    }
}

// MARK: - Verlauf

struct HistoryPane: View {
    @ObservedObject var history: HistoryStore
    @State private var query = ""
    @State private var confirmClear = false

    var body: some View {
        let s = history.summary
        VStack(spacing: 0) {
            HStack(spacing: 10) {
                StatTile(value: "\(s.totalWords)", label: "Wörter gesamt")
                StatTile(value: "\(s.weekWords)", label: "diese Woche")
                StatTile(value: "\(s.wpm)", label: "Wörter/Minute")
                StatTile(value: "\(s.minutesSaved) min", label: "gespart vs. Tippen")
                StatTile(value: "\(s.streakDays)", label: "Tage in Folge")
            }
            .padding(14)
            HStack {
                TextField("Suchen", text: $query).textFieldStyle(.roundedBorder)
                Button("Alles löschen", role: .destructive) { confirmClear = true }
                    .disabled(history.items.isEmpty)
            }
            .padding(.horizontal, 14)
            .padding(.bottom, 8)
            List {
                ForEach(filtered) { item in
                    HistoryRow(item: item, onDelete: { history.delete(item.id) })
                }
            }
            .overlay {
                if history.items.isEmpty {
                    ContentUnavailableView("Noch keine Diktate", systemImage: "waveform", description: Text("Halte die Diktat-Taste und sprich."))
                }
            }
        }
        .confirmationDialog("Gesamten Verlauf löschen?", isPresented: $confirmClear) {
            Button("Löschen", role: .destructive) { history.clear() }
        }
    }

    private var filtered: [HistoryItem] {
        let q = query.trimmingCharacters(in: .whitespaces).lowercased()
        guard !q.isEmpty else { return Array(history.items.prefix(500)) }
        return history.items.filter { $0.text.lowercased().contains(q) || $0.appName.lowercased().contains(q) }
    }
}

struct StatTile: View {
    let value: String
    let label: String
    var body: some View {
        VStack(spacing: 2) {
            Text(value).font(.system(size: 20, weight: .semibold, design: .rounded)).monospacedDigit()
            Text(label).font(.caption).foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 10)
        .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Color.primary.opacity(0.05)))
    }
}

struct HistoryRow: View {
    let item: HistoryItem
    let onDelete: () -> Void
    @State private var showRaw = false

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 6) {
                if item.isCommand { Image(systemName: "sparkles").foregroundStyle(.purple) }
                Text(item.appName.isEmpty ? "–" : item.appName).font(.caption).fontWeight(.medium)
                Text(item.date.formatted(date: .abbreviated, time: .shortened)).font(.caption).foregroundStyle(.secondary)
                Text("· \(item.words) Wörter · \(String(format: "%.1f", item.seconds)) s").font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button { copy(item.text) } label: { Image(systemName: "doc.on.doc") }.buttonStyle(.borderless).help("Kopieren")
                Button { showRaw.toggle() } label: { Image(systemName: "text.magnifyingglass") }.buttonStyle(.borderless).help("Rohtext zeigen")
                Button(role: .destructive, action: onDelete) { Image(systemName: "trash") }.buttonStyle(.borderless)
            }
            Text(item.text).textSelection(.enabled)
            if showRaw {
                Text("Roh: " + item.raw).font(.callout).foregroundStyle(.secondary).textSelection(.enabled)
            }
        }
        .padding(.vertical, 4)
    }

    private func copy(_ s: String) {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(s, forType: .string)
    }
}

// MARK: - Berechtigungen

struct PermissionsPane: View {
    let delegate: AppDelegate

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { _ in
            Form {
                Section {
                    PermissionRow(title: "Mikrofon", detail: "Zum Aufnehmen, nur solange die Taste gedrückt ist.",
                                  ok: Permissions.microphoneGranted, action: {
                        if Permissions.microphone == .notDetermined { Permissions.requestMicrophone { _ in } }
                        else { Permissions.openPrivacy("Privacy_Microphone") }
                    })
                    PermissionRow(title: "Bedienungshilfen", detail: "Für die Diktat-Taste und zum Einfügen in andere Apps.",
                                  ok: Permissions.accessibility, action: {
                        Permissions.promptAccessibility()
                        Permissions.openPrivacy("Privacy_Accessibility")
                    })
                    PermissionRow(title: "Spracherkennung (optional)", detail: "Nur für die ältere Apple-Erkennung, die einspringt, solange das neue Sprachpaket lädt.",
                                  ok: Permissions.speechRecognitionGranted, action: {
                        if Permissions.speechRecognitionUndetermined { Permissions.requestSpeechRecognition() }
                        else { Permissions.openPrivacy("Privacy_SpeechRecognition") }
                    })
                    PermissionRow(title: "Diktat-Taste aktiv", detail: "Startet automatisch, sobald Bedienungshilfen erlaubt sind.",
                                  ok: delegate.hotkeyActive, actionTitle: "Neu starten", action: { delegate.restartHotkey() })
                } header: { Text("Freigaben") } footer: {
                    Text("Nach einem Update von Flüster musst du Flüster unter Bedienungshilfen evtl. einmal entfernen (–) und neu hinzufügen (+), weil sich die Signatur ändert.")
                        .foregroundStyle(.secondary)
                }
                Section("Fn-/🌐-Taste") {
                    HStack {
                        Text("„🌐-Taste drücken zum“ auf „Keine Aktion“ stellen, sonst öffnet sich das Emoji-Fenster.")
                        Spacer()
                        Button("Tastatur öffnen") { Permissions.openKeyboardSettings() }
                    }
                }
            }
            .formStyle(.grouped)
        }
    }
}

struct PermissionRow: View {
    let title: String
    let detail: String
    let ok: Bool
    var actionTitle = "Erlauben"
    let action: () -> Void

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: ok ? "checkmark.circle.fill" : "exclamationmark.circle.fill")
                .foregroundStyle(ok ? Color.green : Color.orange)
                .font(.title3)
            VStack(alignment: .leading, spacing: 2) {
                Text(title).fontWeight(.medium)
                Text(detail).font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            if !ok || actionTitle != "Erlauben" { Button(ok ? actionTitle : "Erlauben", action: action) }
        }
    }
}

// MARK: - Über

struct AboutPane: View {
    var body: some View {
        VStack(spacing: 14) {
            Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 96, height: 96)
            Text("Flüster").font(.system(size: 28, weight: .bold, design: .rounded))
            Text("Version " + (Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev"))
                .foregroundStyle(.secondary)
            Text("Diktieren in jeder App: halten, sprechen, loslassen.\nKostenlos – Erkennung und KI wahlweise komplett auf deinem Mac.")
                .multilineTextAlignment(.center)
            Divider().frame(width: 260)
            Text("Spracherkennung: Apple Speech, whisper.cpp (MIT) oder OpenAI-kompatible APIs.\nBereinigung: Apple Intelligence, OpenAI-kompatible APIs oder Regeln.\nKein Code von Wispr Flow – eigener Nachbau.")
                .font(.caption).foregroundStyle(.secondary).multilineTextAlignment(.center)
        }
        .padding(30)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}
