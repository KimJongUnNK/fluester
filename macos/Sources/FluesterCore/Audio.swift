import Foundation

/// Audio-Hilfen ohne AVFoundation: WAV schreiben, Pegel, Stille erkennen.
public enum AudioUtil {

    /// 16-Bit-PCM-WAV (mono) aus Float-Samples im Bereich −1…1.
    public static func wav(samples: [Float], sampleRate: Int) -> Data {
        var data = Data()
        let byteRate = sampleRate * 2
        let dataSize = samples.count * 2
        func u32(_ v: Int) { var x = UInt32(v).littleEndian; withUnsafeBytes(of: &x) { data.append(contentsOf: $0) } }
        func u16(_ v: Int) { var x = UInt16(v).littleEndian; withUnsafeBytes(of: &x) { data.append(contentsOf: $0) } }
        data.append(contentsOf: Array("RIFF".utf8)); u32(36 + dataSize)
        data.append(contentsOf: Array("WAVE".utf8))
        data.append(contentsOf: Array("fmt ".utf8)); u32(16); u16(1); u16(1); u32(sampleRate); u32(byteRate); u16(2); u16(16)
        data.append(contentsOf: Array("data".utf8)); u32(dataSize)
        data.reserveCapacity(44 + dataSize)
        var pcm = [Int16](repeating: 0, count: samples.count)
        for (i, s) in samples.enumerated() {
            let c = max(-1, min(1, s))
            pcm[i] = Int16(c < 0 ? c * 32768 : c * 32767)
        }
        pcm.withUnsafeBufferPointer { buf in
            buf.baseAddress!.withMemoryRebound(to: UInt8.self, capacity: buf.count * 2) { p in
                data.append(p, count: buf.count * 2)
            }
        }
        return data
    }

    /// RMS-Pegel 0…1 (für die Wellenform-Anzeige, leicht logarithmisch gestaucht).
    public static func level(_ samples: UnsafeBufferPointer<Float>) -> Float {
        guard samples.count > 0 else { return 0 }
        var sum: Float = 0
        for s in samples { sum += s * s }
        let rms = sqrt(sum / Float(samples.count))
        let db = 20 * log10(max(rms, 1e-6))          // −120 … 0 dB
        return max(0, min(1, (db + 55) / 45))          // −55 dB → 0, −10 dB → 1
    }

    /// Hat die Aufnahme überhaupt Sprache? (Grobe Energie-Prüfung, verhindert Halluzinationen bei Stille.)
    public static func hasSpeech(_ samples: [Float], sampleRate: Int) -> Bool {
        let frame = max(1, sampleRate / 50)                   // 20 ms
        var loud = 0
        var i = 0
        while i + frame <= samples.count {
            var sum: Float = 0
            for j in i..<(i + frame) { sum += samples[j] * samples[j] }
            let rms = sqrt(sum / Float(frame))
            if rms > 0.012 { loud += 1 }
            i += frame
        }
        return loud >= 8                                       // mind. ~160 ms über der Schwelle
    }

    /// Stille am Anfang/Ende abschneiden (schneller, weniger Halluzination), mit etwas Rand.
    public static func trimSilence(_ samples: [Float], sampleRate: Int, threshold: Float = 0.008) -> [Float] {
        let frame = max(1, sampleRate / 100)
        func loud(_ start: Int) -> Bool {
            var m: Float = 0
            for j in start..<min(samples.count, start + frame) { m = max(m, abs(samples[j])) }
            return m > threshold * 3
        }
        var first = 0
        while first + frame < samples.count && !loud(first) { first += frame }
        var last = samples.count - frame
        while last > first && !loud(max(0, last)) { last -= frame }
        let pad = sampleRate / 5                               // 200 ms Rand
        let a = max(0, first - pad), b = min(samples.count, last + frame + pad)
        return a < b ? Array(samples[a..<b]) : samples
    }

    /// Lineares Resampling (reicht für Sprache, wird nur genutzt, wenn AVAudioConverter nicht greift).
    public static func resample(_ samples: [Float], from: Double, to: Double) -> [Float] {
        guard from != to, from > 0, to > 0, !samples.isEmpty else { return samples }
        let ratio = from / to
        let n = Int(Double(samples.count) / ratio)
        var out = [Float](repeating: 0, count: n)
        for i in 0..<n {
            let x = Double(i) * ratio
            let i0 = Int(x), i1 = min(i0 + 1, samples.count - 1)
            let f = Float(x - Double(i0))
            out[i] = samples[i0] * (1 - f) + samples[i1] * f
        }
        return out
    }
}
