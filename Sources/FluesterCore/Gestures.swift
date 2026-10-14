import Foundation

/// Erkennt aus Drücken/Loslassen der Diktat-Taste: halten (Push-to-talk), Doppeltipp (freihändig), Abbruch.
/// Reine Zustandsmaschine ohne Systemzugriff, damit sie testbar ist.
public final class HotkeyGestures {
    public enum Action: Equatable {
        case startHold(command: Bool)      // Taste gedrückt → Aufnahme starten
        case stopAndProcess                 // Loslassen nach Halten / Tipp im freihändigen Modus
        case cancel                         // zu kurz, Esc, oder Taste diente als Modifier
        case switchToHandsFree              // Doppeltipp erkannt → Aufnahme läuft weiter
        case none
    }

    public enum State: Equatable { case idle, holding, waitingSecondTap, handsFree }

    public private(set) var state: State = .idle
    public var doubleTapEnabled = true
    /// Kürzer gehalten = Tipp
    public var tapMaxDuration: TimeInterval = 0.28
    /// Abstand zwischen zwei Tipps
    public var doubleTapWindow: TimeInterval = 0.38

    private var pressTime: TimeInterval = 0
    private var lastTapTime: TimeInterval = -10
    private var otherKeyWhileHeld = false

    public init() {}

    public func press(at t: TimeInterval, command: Bool) -> Action {
        switch state {
        case .idle:
            state = .holding
            pressTime = t
            otherKeyWhileHeld = false
            return .startHold(command: command)
        case .waitingSecondTap:
            if t - lastTapTime <= doubleTapWindow && doubleTapEnabled {
                state = .handsFree
                return .switchToHandsFree
            }
            state = .holding
            pressTime = t
            otherKeyWhileHeld = false
            return .startHold(command: command)
        case .handsFree:
            // Tipp beendet den freihändigen Modus beim Loslassen
            pressTime = t
            return .none
        case .holding:
            return .none
        }
    }

    public func release(at t: TimeInterval) -> Action {
        switch state {
        case .holding:
            let held = t - pressTime
            if otherKeyWhileHeld && held < 1.0 {
                state = .idle
                return .cancel
            }
            if held < tapMaxDuration {
                if doubleTapEnabled {
                    // Erster Tipp: Aufnahme läuft kurz weiter, falls ein zweiter Tipp kommt
                    state = .waitingSecondTap
                    lastTapTime = t
                    return .none
                }
                state = .idle
                return .cancel
            }
            state = .idle
            return .stopAndProcess
        case .handsFree:
            state = .idle
            return .stopAndProcess
        case .waitingSecondTap, .idle:
            return .none
        }
    }

    /// Kein zweiter Tipp innerhalb des Fensters → Aufnahme verwerfen.
    public func tick(at t: TimeInterval) -> Action {
        if state == .waitingSecondTap && t - lastTapTime > doubleTapWindow {
            state = .idle
            return .cancel
        }
        return .none
    }

    /// Andere Taste während gehaltener Diktat-Taste (z. B. Fn+Pfeil) → kein Diktat.
    public func otherKeyPressed() {
        if state == .holding { otherKeyWhileHeld = true }
    }

    public func reset() {
        state = .idle
        otherKeyWhileHeld = false
    }
}
