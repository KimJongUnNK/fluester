import AppKit
import SwiftUI

/// Zustand der schwebenden Leiste unten am Bildschirm.
@MainActor
final class FlowBarModel: ObservableObject {
    enum Phase: Equatable {
        case idle
        case listening(handsFree: Bool, command: Bool)
        case processing(command: Bool)
        case done(String)
        case error(String)
    }

    @Published var phase: Phase = .idle
    @Published var levels: [Float] = Array(repeating: 0, count: 30)
    @Published var seconds: Int = 0

    func push(_ level: Float) {
        var l = levels
        l.removeFirst()
        // etwas glätten, damit es nicht flackert
        let prev = l.last ?? 0
        l.append(max(level, prev * 0.6))
        levels = l
    }

    func resetLevels() { levels = Array(repeating: 0, count: 30) }
}

struct FlowBarView: View {
    @ObservedObject var model: FlowBarModel

    var body: some View {
        HStack(spacing: 10) {
            icon
            content
        }
        .padding(.horizontal, 14)
        .frame(height: 38)
        .background(
            Capsule(style: .continuous)
                .fill(Color.black.opacity(0.82))
                .overlay(Capsule(style: .continuous).strokeBorder(Color.white.opacity(0.14), lineWidth: 0.5))
                .shadow(color: .black.opacity(0.35), radius: 14, y: 6)
        )
        .fixedSize()
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .animation(.spring(response: 0.32, dampingFraction: 0.82), value: model.phase)
    }

    @ViewBuilder private var icon: some View {
        switch model.phase {
        case .idle:
            Image(systemName: "waveform").foregroundStyle(.white.opacity(0.6))
        case .listening(let handsFree, let command):
            if command {
                Image(systemName: "sparkles").foregroundStyle(Color(red: 0.75, green: 0.55, blue: 1))
            } else if handsFree {
                Image(systemName: "lock.fill").foregroundStyle(.white)
            } else {
                Circle().fill(Color(red: 1, green: 0.27, blue: 0.23)).frame(width: 8, height: 8)
            }
        case .processing(let command):
            ProgressView().controlSize(.small).tint(command ? Color(red: 0.75, green: 0.55, blue: 1) : .white)
        case .done:
            Image(systemName: "checkmark").foregroundStyle(Color(red: 0.19, green: 0.82, blue: 0.35))
        case .error:
            Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(Color(red: 1, green: 0.62, blue: 0.04))
        }
    }

    @ViewBuilder private var content: some View {
        switch model.phase {
        case .idle:
            Text("Fn halten zum Diktieren").foregroundStyle(.white.opacity(0.7))
        case .listening(let handsFree, let command):
            HStack(spacing: 8) {
                Waveform(levels: model.levels, tint: command ? Color(red: 0.75, green: 0.55, blue: 1) : .white)
                if handsFree || command || model.seconds >= 10 {
                    Text(label(handsFree: handsFree, command: command)).foregroundStyle(.white.opacity(0.75)).monospacedDigit()
                }
            }
        case .processing(let command):
            Text(command ? "Befehl wird ausgeführt …" : "Schreibt …").foregroundStyle(.white.opacity(0.85))
        case .done(let msg):
            Text(msg).foregroundStyle(.white.opacity(0.9))
        case .error(let msg):
            Text(msg).foregroundStyle(.white).lineLimit(2).frame(maxWidth: 420, alignment: .leading)
        }
    }

    private func label(handsFree: Bool, command: Bool) -> String {
        let t = String(format: "%d:%02d", model.seconds / 60, model.seconds % 60)
        if command { return "Befehl · \(t)" }
        if handsFree { return "Freihändig · \(t) · Fn beendet" }
        return t
    }
}

/// Live-Wellenform aus den letzten Pegeln.
struct Waveform: View {
    let levels: [Float]
    var tint: Color = .white

    var body: some View {
        HStack(alignment: .center, spacing: 2.5) {
            ForEach(Array(levels.enumerated()), id: \.offset) { _, l in
                Capsule()
                    .fill(tint.opacity(0.55 + Double(l) * 0.45))
                    .frame(width: 2.5, height: 3 + CGFloat(l) * 18)
            }
        }
        .frame(height: 22)
        .animation(.easeOut(duration: 0.08), value: levels)
    }
}

/// Fenster für die Leiste: schwebt über allem, nimmt keinen Fokus, ignoriert Klicks.
@MainActor
final class FlowBarController {
    let model = FlowBarModel()
    private let panel: NSPanel
    private var hideWork: DispatchWorkItem?
    var showWhenIdle = false { didSet { if model.phase == .idle { showWhenIdle ? show() : hide() } } }

    init() {
        panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 520, height: 64),
                        styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.isFloatingPanel = true
        panel.level = .statusBar
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.ignoresMouseEvents = true
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        let host = NSHostingView(rootView: FlowBarView(model: model))
        host.frame = NSRect(x: 0, y: 0, width: 520, height: 64)
        panel.contentView = host
    }

    func set(_ phase: FlowBarModel.Phase, autoHide: Double? = nil) {
        hideWork?.cancel()
        model.phase = phase
        if case .listening = phase { model.resetLevels() }
        show()
        if let delay = autoHide {
            let work = DispatchWorkItem { [weak self] in
                MainActor.assumeIsolated {
                    guard let self = self else { return }
                    self.model.phase = .idle
                    if !self.showWhenIdle { self.hide() }
                }
            }
            hideWork = work
            DispatchQueue.main.asyncAfter(deadline: .now() + delay, execute: work)
        }
    }

    func idle() {
        hideWork?.cancel()
        model.phase = .idle
        if showWhenIdle { show() } else { hide() }
    }

    func show() {
        position()
        panel.orderFrontRegardless()
    }

    func hide() { panel.orderOut(nil) }

    private func position() {
        let mouse = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { NSMouseInRect(mouse, $0.frame, false) } ?? NSScreen.main ?? NSScreen.screens.first
        guard let vf = screen?.visibleFrame else { return }
        panel.setFrameOrigin(NSPoint(x: vf.midX - panel.frame.width / 2, y: vf.minY + 14))
    }
}
