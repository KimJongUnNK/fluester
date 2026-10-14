import AVFoundation
import CoreAudio
import AudioToolbox
import FluesterCore

/// Mikrofon-Aufnahme: AVAudioEngine → 16 kHz mono Float32 im Speicher, plus Pegel für die Wellenform.
final class AudioRecorder {
    static let sampleRate: Double = 16000

    private var engine: AVAudioEngine?
    private var converter: AVAudioConverter?
    private let lock = NSLock()
    private var samples: [Float] = []
    private var startDate = Date()
    private(set) var isRecording = false

    /// Wird auf dem Main-Thread mit dem aktuellen Pegel (0…1) aufgerufen.
    var onLevel: ((Float) -> Void)?

    func start(deviceUID: String) throws {
        if isRecording { _ = stop() }
        lock.lock(); samples.removeAll(keepingCapacity: true); lock.unlock()

        let engine = AVAudioEngine()
        let input = engine.inputNode
        if !deviceUID.isEmpty, let id = AudioDevices.deviceID(forUID: deviceUID), let unit = input.audioUnit {
            var dev = id
            AudioUnitSetProperty(unit, kAudioOutputUnitProperty_CurrentDevice, kAudioUnitScope_Global, 0, &dev, UInt32(MemoryLayout<AudioDeviceID>.size))
        }
        let inFormat = input.outputFormat(forBus: 0)
        guard inFormat.sampleRate > 0, inFormat.channelCount > 0 else {
            throw FluesterError("Kein Mikrofon gefunden")
        }
        guard let target = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: Self.sampleRate, channels: 1, interleaved: false),
              let conv = AVAudioConverter(from: inFormat, to: target) else {
            throw FluesterError("Audioformat wird nicht unterstützt")
        }
        converter = conv
        input.installTap(onBus: 0, bufferSize: 2048, format: inFormat) { [weak self] buffer, _ in
            self?.process(buffer, target: target)
        }
        engine.prepare()
        do {
            try engine.start()
        } catch {
            input.removeTap(onBus: 0)
            throw FluesterError("Mikrofon lässt sich nicht starten: \(error.localizedDescription)")
        }
        self.engine = engine
        isRecording = true
        startDate = Date()
    }

    private func process(_ buffer: AVAudioPCMBuffer, target: AVAudioFormat) {
        if let ch = buffer.floatChannelData {
            let lvl = AudioUtil.level(UnsafeBufferPointer(start: ch[0], count: Int(buffer.frameLength)))
            DispatchQueue.main.async { [weak self] in self?.onLevel?(lvl) }
        }
        guard let converter = converter else { return }
        let ratio = target.sampleRate / buffer.format.sampleRate
        let capacity = AVAudioFrameCount(Double(buffer.frameLength) * ratio + 64)
        guard let out = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: capacity) else { return }
        let fed = Flag()
        var error: NSError?
        let status = converter.convert(to: out, error: &error) { _, inputStatus in
            if fed.value {
                inputStatus.pointee = .noDataNow
                return nil
            }
            fed.value = true
            inputStatus.pointee = .haveData
            return buffer
        }
        guard status != .error, let data = out.floatChannelData, out.frameLength > 0 else { return }
        let chunk = UnsafeBufferPointer(start: data[0], count: Int(out.frameLength))
        lock.lock()
        samples.append(contentsOf: chunk)
        lock.unlock()
    }

    /// Stoppt und liefert die Aufnahme.
    func stop() -> (samples: [Float], seconds: Double) {
        guard isRecording, let engine = engine else { return ([], 0) }
        engine.inputNode.removeTap(onBus: 0)
        engine.stop()
        self.engine = nil
        converter = nil
        isRecording = false
        lock.lock()
        let s = samples
        samples = []
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

    static func inputs() -> [Device] {
        var addr = AudioObjectPropertyAddress(mSelector: kAudioHardwarePropertyDevices, mScope: kAudioObjectPropertyScopeGlobal,
                                              mElement: kAudioObjectPropertyElementMain)
        var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(AudioObjectID(kAudioObjectSystemObject), &addr, 0, nil, &size) == noErr, size > 0 else { return [] }
        var ids = [AudioDeviceID](repeating: 0, count: Int(size) / MemoryLayout<AudioDeviceID>.size)
        guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &addr, 0, nil, &size, &ids) == noErr else { return [] }
        return ids.compactMap { id in
            var streams = AudioObjectPropertyAddress(mSelector: kAudioDevicePropertyStreams, mScope: kAudioDevicePropertyScopeInput,
                                                     mElement: kAudioObjectPropertyElementMain)
            var sz: UInt32 = 0
            guard AudioObjectGetPropertyDataSize(id, &streams, 0, nil, &sz) == noErr, sz > 0 else { return nil }
            guard let uid = stringProperty(id, kAudioDevicePropertyDeviceUID), let name = stringProperty(id, kAudioObjectPropertyName) else { return nil }
            return Device(id: id, uid: uid, name: name)
        }
    }

    static func deviceID(forUID uid: String) -> AudioDeviceID? {
        inputs().first { $0.uid == uid }?.id
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
