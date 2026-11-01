import AVFoundation
import CoreAudio
import AudioToolbox
import FluesterCore

/// Mikrofon-Aufnahme: AVAudioEngine → 16 kHz mono Float32 im Speicher, plus Pegel für die Wellenform.
/// USB-fest: findet das Mikrofon auch unter neuer UID (anderer Port oder Hub), nimmt alle Kanäle mit, übernimmt die
/// Abtastrate des Geräts und baut sich neu auf, wenn sich mitten im Diktat das Gerät ändert (abgesteckt, Hub trennt kurz).
final class AudioRecorder {
    static let sampleRate: Double = 16000

    private var engine: AVAudioEngine?
    private var configObserver: NSObjectProtocol?
    private let lock = NSLock()
    private var converter: AVAudioConverter?   // nur unter lock
    private var samples: [Float] = []          // nur unter lock
    private var startDate = Date()
    private var wantedUID = ""
    private var wantedName = ""
    private var restarts = 0
    private(set) var isRecording = false

    /// Das gewählte Mikrofon, so wie es gerade heißt – auch wenn es unter neuer UID wiedergefunden wurde.
    /// nil = Systemstandard (so eingestellt oder weil das gewählte fehlt).
    private(set) var selected: AudioDevices.Device?
    /// Das gewählte Mikrofon fehlt oder streikt, es läuft der Systemstandard.
    private(set) var usingFallback = false

    /// Wird auf dem Main-Thread mit dem aktuellen Pegel (0…1) aufgerufen.
    var onLevel: ((Float) -> Void)?

    func start(deviceUID: String, deviceName: String = "") throws {
        if isRecording { _ = stop() }
        lock.lock(); samples.removeAll(keepingCapacity: true); converter = nil; lock.unlock()
        wantedUID = deviceUID
        wantedName = deviceName
        restarts = 0
        try startEngine()
        isRecording = true
        startDate = Date()
    }

    /// Gerät auflösen und Engine starten. Frisch eingesteckte oder gerade aufgewachte USB-Geräte sind manchmal erst nach
    /// einem Moment bereit – dann noch einmal versuchen, zuletzt mit dem Systemstandard.
    private func startEngine() throws {
        let device = AudioDevices.resolve(uid: wantedUID, name: wantedName)
        selected = device
        usingFallback = !wantedUID.isEmpty && device == nil
        if usingFallback {
            NSLog("Flüster: gewähltes Mikrofon „%@“ nicht angeschlossen – nehme den Systemstandard", wantedName.isEmpty ? wantedUID : wantedName)
        }
        do {
            try runEngine(device: device)
        } catch {
            guard let device else { throw error }
            NSLog("Flüster: Mikrofon „%@“ startet nicht (%@) – neuer Versuch", device.name, error.localizedDescription)
            Thread.sleep(forTimeInterval: 0.25)
            do {
                try runEngine(device: AudioDevices.resolve(uid: wantedUID, name: wantedName) ?? device)
            } catch {
                NSLog("Flüster: Mikrofon „%@“ startet nicht (%@) – nehme den Systemstandard", device.name, error.localizedDescription)
                selected = nil
                usingFallback = true
                try runEngine(device: nil)
            }
        }
    }

    private func runEngine(device: AudioDevices.Device?) throws {
        let engine = AVAudioEngine()
        let input = engine.inputNode
        if let device {
            guard let unit = input.audioUnit else { throw FluesterError("Mikrofon „\(device.name)“ lässt sich nicht wählen") }
            var id = device.id
            let status = AudioUnitSetProperty(unit, kAudioOutputUnitProperty_CurrentDevice, kAudioUnitScope_Global, 0, &id, UInt32(MemoryLayout<AudioDeviceID>.size))
            guard status == noErr else { throw FluesterError("Mikrofon „\(device.name)“ lässt sich nicht wählen (Fehler \(status))") }
        }
        // Der Tap muss die Abtastrate der Hardware haben, sonst bricht AVAudioEngine mit einer Ausnahme ab (Absturz).
        // Nach dem Gerätewechsel kann das Ausgabeformat noch das des alten Geräts sein – dann aus der Hardware bauen.
        let hw = input.inputFormat(forBus: 0)
        guard hw.sampleRate > 0, hw.channelCount > 0 else { throw FluesterError("Kein Mikrofon gefunden") }
        let out = input.outputFormat(forBus: 0)
        let tapFormat: AVAudioFormat? = out.sampleRate == hw.sampleRate && out.channelCount > 0 ? out
            : AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: hw.sampleRate, channels: hw.channelCount, interleaved: false)
        guard let tapFormat else { throw FluesterError("Audioformat des Mikrofons wird nicht unterstützt") }
        input.installTap(onBus: 0, bufferSize: 4096, format: tapFormat) { [weak self] buffer, _ in
            self?.process(buffer)
        }
        engine.prepare()
        do {
            try engine.start()
        } catch {
            input.removeTap(onBus: 0)
            throw FluesterError("Mikrofon lässt sich nicht starten: \(error.localizedDescription)")
        }
        self.engine = engine
        configObserver = NotificationCenter.default.addObserver(forName: .AVAudioEngineConfigurationChange, object: engine, queue: .main) { [weak self] _ in
            self?.configurationChanged()
        }
    }

    /// Gerät mitten im Diktat abgesteckt, Hub hat kurz getrennt, Abtastrate geändert: AVAudioEngine hält dann still an.
    /// Neu aufbauen und weiter aufnehmen – das bisher Gesagte bleibt erhalten.
    private func configurationChanged() {
        guard isRecording, restarts < 5 else { return }
        restarts += 1
        teardown()
        do {
            try startEngine()
            NSLog("Flüster: Audio-Gerät geändert – Aufnahme läuft weiter mit %@", selected?.name ?? "dem Systemstandard")
        } catch {
            NSLog("Flüster: Aufnahme nach Gerätewechsel nicht fortsetzbar: %@", error.localizedDescription)
        }
    }

    private func teardown() {
        if let o = configObserver {
            NotificationCenter.default.removeObserver(o)
            configObserver = nil
        }
        guard let engine else { return }
        engine.inputNode.removeTap(onBus: 0)
        engine.stop()
        self.engine = nil
    }

    private func process(_ buffer: AVAudioPCMBuffer) {
        let frames = Int(buffer.frameLength)
        guard frames > 0, let data = buffer.floatChannelData else { return }
        let count = Int(buffer.format.channelCount)
        let interleaved = buffer.format.isInterleaved
        let stride = buffer.stride
        let channels: [[Float]] = (0..<count).map { c in
            interleaved ? (0..<frames).map { data[0][$0 * stride + c] } : Array(UnsafeBufferPointer(start: data[c], count: frames))
        }
        let mono = AudioUtil.downmix(channels)
        let lvl = mono.withUnsafeBufferPointer { AudioUtil.level($0) }
        DispatchQueue.main.async { [weak self] in self?.onLevel?(lvl) }

        let rate = buffer.format.sampleRate
        lock.lock()
        defer { lock.unlock() }
        if rate == Self.sampleRate {
            samples.append(contentsOf: mono)
            return
        }
        guard let src = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: rate, channels: 1, interleaved: false),
              let target = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: Self.sampleRate, channels: 1, interleaved: false) else { return }
        // Neuer Wandler, wenn sich die Rate ändert (anderes Gerät nach einem Wechsel mitten im Diktat)
        if converter?.inputFormat.sampleRate != rate { converter = AVAudioConverter(from: src, to: target) }
        guard let converter = converter,
              let inBuf = AVAudioPCMBuffer(pcmFormat: src, frameCapacity: AVAudioFrameCount(frames)),
              let dst = inBuf.floatChannelData,
              let out = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: AVAudioFrameCount(Double(frames) * Self.sampleRate / rate + 64)) else { return }
        inBuf.frameLength = AVAudioFrameCount(frames)
        mono.withUnsafeBufferPointer { dst[0].update(from: $0.baseAddress!, count: frames) }
        let fed = Flag()
        var error: NSError?
        let status = converter.convert(to: out, error: &error) { _, inputStatus in
            if fed.value {
                inputStatus.pointee = .noDataNow
                return nil
            }
            fed.value = true
            inputStatus.pointee = .haveData
            return inBuf
        }
        guard status != .error, let result = out.floatChannelData, out.frameLength > 0 else { return }
        samples.append(contentsOf: UnsafeBufferPointer(start: result[0], count: Int(out.frameLength)))
    }

    /// Stoppt und liefert die Aufnahme.
    func stop() -> (samples: [Float], seconds: Double) {
        guard isRecording else { return ([], 0) }
        teardown()
        isRecording = false
        lock.lock()
        let s = samples
        samples = []
        converter = nil
        lock.unlock()
        return (s, Date().timeIntervalSince(startDate))
    }

    var elapsed: Double { isRecording ? Date().timeIntervalSince(startDate) : 0 }
}

/// Veränderbarer Merker für Closures (statt eingefangener `var`).
final class Flag: @unchecked Sendable {
    var value = false
}

/// Eingabegeräte über CoreAudio.
enum AudioDevices {
    struct Device: Identifiable, Hashable {
        let id: AudioDeviceID
        let uid: String
        let name: String
    }

    /// Kommt auf dem Main-Thread, wenn Geräte an- oder abgesteckt werden oder sich der Standard-Eingang ändert.
    static let changed = Notification.Name("FluesterAudioDevicesChanged")

    /// Eingänge mit mindestens einem Kanal – ohne verborgene und gerade verschwindende Geräte.
    static func inputs() -> [Device] {
        startMonitoring()
        var addr = AudioObjectPropertyAddress(mSelector: kAudioHardwarePropertyDevices, mScope: kAudioObjectPropertyScopeGlobal,
                                              mElement: kAudioObjectPropertyElementMain)
        var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(AudioObjectID(kAudioObjectSystemObject), &addr, 0, nil, &size) == noErr, size > 0 else { return [] }
        var ids = [AudioDeviceID](repeating: 0, count: Int(size) / MemoryLayout<AudioDeviceID>.size)
        guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &addr, 0, nil, &size, &ids) == noErr else { return [] }
        return ids.compactMap { id in
            guard inputChannels(id) > 0,
                  (uint32Property(id, kAudioDevicePropertyDeviceIsAlive) ?? 1) != 0,
                  (uint32Property(id, kAudioDevicePropertyIsHidden) ?? 0) == 0,
                  let uid = stringProperty(id, kAudioDevicePropertyDeviceUID), let name = stringProperty(id, kAudioObjectPropertyName) else { return nil }
            return Device(id: id, uid: uid, name: name)
        }
    }

    /// Das gewählte Mikrofon – notfalls unter neuer UID wiedergefunden (anderer USB-Port oder Hub) –, sonst nil (Systemstandard).
    static func resolve(uid: String, name: String) -> Device? {
        guard !uid.isEmpty else { return nil }
        let all = inputs()
        guard let i = MicrophoneMatch.find(all.map { MicrophoneMatch.Candidate(uid: $0.uid, name: $0.name) }, uid: uid, name: name) else { return nil }
        if all[i].uid != uid { NSLog("Flüster: Mikrofon „%@“ unter neuer UID wiedergefunden (anderer USB-Port oder Hub)", all[i].name) }
        return all[i]
    }

    /// Aktueller Standard-Eingang des Systems.
    static var defaultInput: Device? {
        var addr = AudioObjectPropertyAddress(mSelector: kAudioHardwarePropertyDefaultInputDevice, mScope: kAudioObjectPropertyScopeGlobal,
                                              mElement: kAudioObjectPropertyElementMain)
        var id = AudioDeviceID(0)
        var size = UInt32(MemoryLayout<AudioDeviceID>.size)
        guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &addr, 0, nil, &size, &id) == noErr, id != 0 else { return nil }
        return inputs().first { $0.id == id }
    }

    private static var monitoring = false

    /// Einmalig: bei An-/Abstecken und neuem Standard-Eingang `changed` senden, damit die Auswahl aktuell bleibt.
    private static func startMonitoring() {
        guard !monitoring else { return }
        monitoring = true
        for selector in [kAudioHardwarePropertyDevices, kAudioHardwarePropertyDefaultInputDevice] {
            var addr = AudioObjectPropertyAddress(mSelector: selector, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
            AudioObjectAddPropertyListenerBlock(AudioObjectID(kAudioObjectSystemObject), &addr, DispatchQueue.main) { _, _ in
                NotificationCenter.default.post(name: changed, object: nil)
            }
        }
    }

    private static func inputChannels(_ id: AudioObjectID) -> Int {
        var addr = AudioObjectPropertyAddress(mSelector: kAudioDevicePropertyStreamConfiguration, mScope: kAudioDevicePropertyScopeInput,
                                              mElement: kAudioObjectPropertyElementMain)
        var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(id, &addr, 0, nil, &size) == noErr, size > 0 else { return 0 }
        let raw = UnsafeMutableRawPointer.allocate(byteCount: Int(size), alignment: MemoryLayout<AudioBufferList>.alignment)
        defer { raw.deallocate() }
        guard AudioObjectGetPropertyData(id, &addr, 0, nil, &size, raw) == noErr else { return 0 }
        let list = UnsafeMutableAudioBufferListPointer(raw.assumingMemoryBound(to: AudioBufferList.self))
        return list.reduce(0) { $0 + Int($1.mNumberChannels) }
    }

    private static func uint32Property(_ id: AudioObjectID, _ selector: AudioObjectPropertySelector) -> UInt32? {
        var addr = AudioObjectPropertyAddress(mSelector: selector, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
        var value: UInt32 = 0
        var size = UInt32(MemoryLayout<UInt32>.size)
        return AudioObjectGetPropertyData(id, &addr, 0, nil, &size, &value) == noErr ? value : nil
    }

    private static func stringProperty(_ id: AudioObjectID, _ selector: AudioObjectPropertySelector) -> String? {
        var addr = AudioObjectPropertyAddress(mSelector: selector, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
        var value: Unmanaged<CFString>?
        var size = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
        let err = withUnsafeMutablePointer(to: &value) { ptr in
            AudioObjectGetPropertyData(id, &addr, 0, nil, &size, ptr)
        }
        guard err == noErr, let v = value else { return nil }
        return v.takeRetainedValue() as String
    }
}

/// AVAudioPCMBuffer aus Samples bauen und ins gewünschte Format wandeln.
enum AudioBuffers {
    /// Audiodatei (AIFF, WAV, M4A …) als 16 kHz mono Float laden.
    static func loadFile(_ url: URL, sampleRate: Double = AudioRecorder.sampleRate) throws -> [Float] {
        let file = try AVAudioFile(forReading: url)
        let format = file.processingFormat
        guard let buf = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(file.length)) else {
            throw FluesterError("Datei zu groß")
        }
        try file.read(into: buf)
        guard let target = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: sampleRate, channels: 1, interleaved: false),
              let conv = AVAudioConverter(from: format, to: target),
              let out = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: AVAudioFrameCount(Double(buf.frameLength) * sampleRate / format.sampleRate + 4096)) else {
            throw FluesterError("Audioformat nicht wandelbar")
        }
        let fed = Flag()
        var error: NSError?
        let status = conv.convert(to: out, error: &error) { _, inputStatus in
            if fed.value {
                inputStatus.pointee = .endOfStream
                return nil
            }
            fed.value = true
            inputStatus.pointee = .haveData
            return buf
        }
        if status == .error { throw FluesterError(error?.localizedDescription ?? "Audio-Wandlung fehlgeschlagen") }
        guard let data = out.floatChannelData else { return [] }
        return Array(UnsafeBufferPointer(start: data[0], count: Int(out.frameLength)))
    }

    static func make(samples: [Float], sampleRate: Double, to target: AVAudioFormat?) throws -> AVAudioPCMBuffer {
        guard !samples.isEmpty,
              let src = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: sampleRate, channels: 1, interleaved: false),
              let buf = AVAudioPCMBuffer(pcmFormat: src, frameCapacity: AVAudioFrameCount(samples.count)),
              let dst = buf.floatChannelData else {
            throw FluesterError("Leere Aufnahme")
        }
        buf.frameLength = AVAudioFrameCount(samples.count)
        samples.withUnsafeBufferPointer { p in
            dst[0].update(from: p.baseAddress!, count: samples.count)
        }
        guard let target = target, !target.isEqual(src) else { return buf }
        guard let conv = AVAudioConverter(from: src, to: target) else { throw FluesterError("Audioformat nicht wandelbar") }
        let capacity = AVAudioFrameCount(Double(samples.count) * target.sampleRate / sampleRate + 4096)
        guard let out = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: capacity) else { throw FluesterError("Kein Speicher für Audio") }
        let fed = Flag()
        var error: NSError?
        let status = conv.convert(to: out, error: &error) { _, inputStatus in
            if fed.value {
                inputStatus.pointee = .endOfStream
                return nil
            }
            fed.value = true
            inputStatus.pointee = .haveData
            return buf
        }
        if status == .error { throw FluesterError(error?.localizedDescription ?? "Audio-Wandlung fehlgeschlagen") }
        return out
    }
}
