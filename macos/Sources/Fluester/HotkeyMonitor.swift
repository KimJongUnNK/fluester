import AppKit
import Carbon.HIToolbox
import FluesterCore

/// Globale Diktat-Taste über einen CGEventTap (braucht Bedienungshilfen-Freigabe).
/// Meldet Drücken/Loslassen der gewählten Taste, Esc und andere Tasten während des Haltens.
final class HotkeyMonitor {
    var choice: HotkeyChoice = .fn

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
        let mask = (1 << CGEventType.flagsChanged.rawValue) | (1 << CGEventType.keyDown.rawValue)
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
        }
    }

    private var flag: CGEventFlags {
        switch choice {
        case .fn: return .maskSecondaryFn
        case .rightOption: return .maskAlternate
        case .rightCommand: return .maskCommand
        case .rightControl: return .maskControl
        }
    }

    /// Zusatztaste für Command Mode: ⌃ (bzw. ⌥, wenn die Diktat-Taste selbst ⌃ ist)
    private var commandFlag: CGEventFlags { choice == .rightControl ? .maskAlternate : .maskControl }
    private var commandKeyCodes: Set<Int64> {
        choice == .rightControl ? [Int64(kVK_Option), Int64(kVK_RightOption)] : [Int64(kVK_Control), Int64(kVK_RightControl)]
    }

    private func handle(type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            if let tap = tap { CGEvent.tapEnable(tap: tap, enable: true) }
            return Unmanaged.passUnretained(event)
        }
        let code = event.getIntegerValueField(.keyboardEventKeycode)
        if type == .flagsChanged {
            if code == keyCode {
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
            if code == Int64(kVK_Escape), let esc = onEscape, esc() { return nil }
            if isDown { onOtherKey?() }
        }
        return Unmanaged.passUnretained(event)
    }
}
