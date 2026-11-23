import AppKit
import Carbon.HIToolbox
import FluesterCore

/// Globale Diktat-Taste über einen CGEventTap (braucht Bedienungshilfen-Freigabe).
/// Meldet Drücken/Loslassen der gewählten Taste, Esc und andere Tasten während des Haltens.
final class HotkeyMonitor {
    var choice: HotkeyChoice = .fn
    /// Tastencode der eigenen Taste (`.custom`), kVK_…
    var customCode: Int = -1
    /// Vorübergehend nichts auswerten (Einstellungen nehmen gerade eine neue Taste auf)
    var suspended = false

    /// Taste gedrückt; Parameter: Command-Modus (⌃ gleichzeitig gehalten)
    var onPress: ((Bool) -> Void)?
    var onRelease: (() -> Void)?
    /// ⌃ kam nach der Diktat-Taste dazu → Command Mode
    var onCommandModifier: (() -> Void)?
    var onOtherKey: (() -> Void)?
    /// Esc: true = verschluckt (z. B. Aufnahme abgebrochen)
    var onEscape: (() -> Bool)?

    private var tap: CFMachPort?
    private var source: CFRunLoopSource?
    private var isDown = false

    var isRunning: Bool { tap != nil }

    @discardableResult
    func start() -> Bool {
        stop()
        let mask = (1 << CGEventType.flagsChanged.rawValue) | (1 << CGEventType.keyDown.rawValue) | (1 << CGEventType.keyUp.rawValue)
        let callback: CGEventTapCallBack = { _, type, event, refcon in
            guard let refcon = refcon else { return Unmanaged.passUnretained(event) }
            let monitor = Unmanaged<HotkeyMonitor>.fromOpaque(refcon).takeUnretainedValue()
            return monitor.handle(type: type, event: event)
        }
        guard let tap = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .headInsertEventTap, options: .defaultTap,
                                          eventsOfInterest: CGEventMask(mask), callback: callback,
                                          userInfo: Unmanaged.passUnretained(self).toOpaque()) else {
            return false
        }
        self.tap = tap
        let src = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        source = src
        CFRunLoopAddSource(CFRunLoopGetMain(), src, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
        return true
    }

    func stop() {
        if let tap = tap { CGEvent.tapEnable(tap: tap, enable: false) }
        if let src = source { CFRunLoopRemoveSource(CFRunLoopGetMain(), src, .commonModes) }
        tap = nil
        source = nil
        isDown = false
    }

    private var keyCode: Int64 {
        switch choice {
        case .fn: return Int64(kVK_Function)
        case .rightOption: return Int64(kVK_RightOption)
        case .rightCommand: return Int64(kVK_RightCommand)
        case .rightControl: return Int64(kVK_RightControl)
        case .custom: return Int64(customCode)
        }
    }

    /// Eigene Taste ist eine normale Taste (F13, Ziffernblock …) statt eines Modifiers → keyDown/keyUp statt flagsChanged.
    private var isPlainKey: Bool { choice == .custom && customCode >= 0 && !HotkeyCapture.modifierCodes.contains(customCode) }

    private var flag: CGEventFlags {
        switch choice {
        case .fn: return .maskSecondaryFn
        case .rightOption: return .maskAlternate
        case .rightCommand: return .maskCommand
        case .rightControl: return .maskControl
        case .custom:
            switch customCode {
            case kVK_Command, kVK_RightCommand: return .maskCommand
            case kVK_Option, kVK_RightOption: return .maskAlternate
            case kVK_Control, kVK_RightControl: return .maskControl
            case kVK_Function: return .maskSecondaryFn
            default: return []
            }
        }
    }

    /// Zusatztaste für Command Mode: ⌃ (bzw. ⌥, wenn die Diktat-Taste selbst ⌃ ist)
    private var controlIsHotkey: Bool { flag == .maskControl }
    private var commandFlag: CGEventFlags { controlIsHotkey ? .maskAlternate : .maskControl }
    private var commandKeyCodes: Set<Int64> {
        controlIsHotkey ? [Int64(kVK_Option), Int64(kVK_RightOption)] : [Int64(kVK_Control), Int64(kVK_RightControl)]
    }

    private func handle(type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            if let tap = tap { CGEvent.tapEnable(tap: tap, enable: true) }
            // Solange der Tap aus war, kam kein Loslassen an – sonst liefe die Aufnahme weiter, bis man nochmal drückt
            let stillDown = isPlainKey
                ? CGEventSource.keyState(.combinedSessionState, key: CGKeyCode(customCode))
                : CGEventSource.flagsState(.combinedSessionState).contains(flag)
            if isDown && !stillDown {
                isDown = false
                onRelease?()
            }
            return Unmanaged.passUnretained(event)
        }
        if suspended { return Unmanaged.passUnretained(event) }
        let code = event.getIntegerValueField(.keyboardEventKeycode)
        if isPlainKey && code == keyCode && (type == .keyDown || type == .keyUp) {
            // Eigene normale Taste: Drücken/Loslassen auswerten und schlucken (auch die Wiederholungen beim Halten)
            if type == .keyDown && !isDown && event.getIntegerValueField(.keyboardEventAutorepeat) == 0 {
                isDown = true
                onPress?(event.flags.contains(commandFlag))
            } else if type == .keyUp && isDown {
                isDown = false
                onRelease?()
            }
            return nil
        }
        if type == .flagsChanged {
            if !isPlainKey && code == keyCode {
                let down = event.flags.contains(flag)
                if down && !isDown {
                    isDown = true
                    onPress?(event.flags.contains(commandFlag))
                } else if !down && isDown {
                    isDown = false
                    onRelease?()
                }
            } else if isDown && commandKeyCodes.contains(code) && event.flags.contains(commandFlag) {
                onCommandModifier?()
            } else if isDown {
                // anderer Modifier (⌘, ⇧ …) während des Haltens: Diktat-Taste als Modifier benutzt
                if !event.flags.isDisjoint(with: [.maskCommand, .maskShift]) { onOtherKey?() }
            }
            return Unmanaged.passUnretained(event)
        }
        if type == .keyDown {
            if event.getIntegerValueField(.keyboardEventAutorepeat) != 0 { return Unmanaged.passUnretained(event) }
            if code == Int64(kVK_Escape), let esc = onEscape, esc() { return nil }
            if isDown { onOtherKey?() }
        }
        return Unmanaged.passUnretained(event)
    }
}
