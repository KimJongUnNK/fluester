import SwiftUI
import AppKit
import FluesterCore

/// Erster Start: Freigaben, Taste, Erkennung, ausprobieren.
struct OnboardingView: View {
    @ObservedObject var store: SettingsStore
    let delegate: AppDelegate
    @State private var tryText = ""

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { _ in
            VStack(alignment: .leading, spacing: 18) {
                header
                step(1, "Freigaben") {
                    VStack(spacing: 10) {
                        PermissionRow(title: "Mikrofon", detail: "Nur während du die Taste hältst.", ok: Permissions.microphoneGranted) {
                            if Permissions.microphone == .notDetermined { Permissions.requestMicrophone { _ in } }
                            else { Permissions.openPrivacy("Privacy_Microphone") }
                        }
                        PermissionRow(title: "Bedienungshilfen", detail: "Für die Taste und das Einfügen in jede App. Flüster in der Liste einschalten.",
                                      ok: Permissions.accessibility) {
                            Permissions.promptAccessibility()
                            Permissions.openPrivacy("Privacy_Accessibility")
                        }
                        if store.s.engine == .apple {
                            PermissionRow(title: "Spracherkennung", detail: "Für die Apple-Erkennung, solange das neue Sprachpaket im Hintergrund lädt.",
                                          ok: Permissions.speechRecognitionGranted) {
                                if Permissions.speechRecognitionUndetermined { Permissions.requestSpeechRecognition() }
                                else { Permissions.openPrivacy("Privacy_SpeechRecognition") }
                            }
                        }
                    }
                }
                step(2, "Diktat-Taste") {
                    VStack(alignment: .leading, spacing: 8) {
                        Picker("Taste", selection: $store.s.hotkey) {
                            ForEach(HotkeyChoice.allCases) { Text($0.label).tag($0) }
                        }
                        .pickerStyle(.segmented)
                        .labelsHidden()
                        if store.s.hotkey == .fn {
                            HStack {
                                Text("Tipp: Tastatur › „🌐-Taste drücken zum“ → „Keine Aktion“")
                                    .font(.callout).foregroundStyle(.secondary)
                                Spacer()
                                Button("Öffnen") { Permissions.openKeyboardSettings() }.controlSize(.small)
                            }
                        }
                    }
                }
                step(3, "Erkennung") {
                    Picker("Erkennung", selection: $store.s.engine) {
                        ForEach(EngineChoice.allCases) { Text($0.label).tag($0) }
                    }
                    .pickerStyle(.radioGroup)
                    .labelsHidden()
                }
                step(4, "Ausprobieren") {
                    VStack(alignment: .leading, spacing: 6) {
                        Text("Klick ins Feld, halte \(store.s.hotkey.label), sprich, lass los. Doppeltipp = freihändig.")
                            .font(.callout).foregroundStyle(.secondary)
                        TextEditor(text: $tryText)
                            .font(.body)
                            .frame(height: 70)
                            .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.primary.opacity(0.15)))
                    }
                }
                HStack {
                    Button("Alle Einstellungen …") { delegate.showSettings(tab: .general) }
                    Spacer()
                    Button("Los geht's") {
                        store.s.onboardingDone = true
                        delegate.restartHotkey()
                        delegate.closeOnboarding()
                    }
                    .keyboardShortcut(.defaultAction)
                    .controlSize(.large)
                }
            }
            .padding(28)
            .frame(width: 560)
        }
    }

    private var header: some View {
        HStack(spacing: 14) {
            Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 64, height: 64)
            VStack(alignment: .leading, spacing: 4) {
                Text("Willkommen bei Flüster").font(.system(size: 24, weight: .bold, design: .rounded))
                Text("Halten. Sprechen. Loslassen. – In jeder App, kostenlos.").foregroundStyle(.secondary)
            }
        }
    }

    private func step<Content: View>(_ n: Int, _ title: String, @ViewBuilder _ content: () -> Content) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Text("\(n)")
                .font(.system(size: 13, weight: .bold, design: .rounded))
                .foregroundStyle(.white)
                .frame(width: 22, height: 22)
                .background(Circle().fill(Color.accentColor))
            VStack(alignment: .leading, spacing: 8) {
                Text(title).font(.headline)
                content()
            }
        }
    }
}
