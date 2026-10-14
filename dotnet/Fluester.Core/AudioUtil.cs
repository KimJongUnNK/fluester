namespace Fluester.Core;

/// <summary>Audio-Hilfen: WAV schreiben/lesen, Pegel, Stille erkennen. Samples sind Float −1…1, mono.</summary>
public static class AudioUtil
{
    public const int SampleRate = 16000;

    /// <summary>16-Bit-PCM-WAV (mono) aus Float-Samples.</summary>
    public static byte[] Wav(ReadOnlySpan<float> samples, int sampleRate)
    {
        int dataSize = samples.Length * 2;
        var d = new byte[44 + dataSize];
        void Ascii(int at, string s) { for (int i = 0; i < 4; i++) d[at + i] = (byte)s[i]; }
        void U32(int at, int v) => BitConverter.TryWriteBytes(d.AsSpan(at, 4), (uint)v);
        void U16(int at, int v) => BitConverter.TryWriteBytes(d.AsSpan(at, 2), (ushort)v);
        Ascii(0, "RIFF"); U32(4, 36 + dataSize); Ascii(8, "WAVE");
        Ascii(12, "fmt "); U32(16, 16); U16(20, 1); U16(22, 1); U32(24, sampleRate); U32(28, sampleRate * 2); U16(32, 2); U16(34, 16);
        Ascii(36, "data"); U32(40, dataSize);
        for (int i = 0; i < samples.Length; i++)
        {
            float c = Math.Clamp(samples[i], -1f, 1f);
            short v = (short)(c < 0 ? c * 32768 : c * 32767);
            BitConverter.TryWriteBytes(d.AsSpan(44 + i * 2, 2), v);
        }
        return d;
    }

    /// <summary>Liest eine PCM-WAV-Datei (8/16/24/32 Bit oder Float, beliebige Kanäle/Rate) als 16 kHz mono.</summary>
    public static float[] ReadWav(byte[] bytes)
    {
        if (bytes.Length < 44 || System.Text.Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF")
            throw new FluesterException("Keine WAV-Datei");
        int pos = 12, channels = 1, rate = 16000, bits = 16, format = 1;
        float[]? mono = null;
        while (pos + 8 <= bytes.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
            int size = BitConverter.ToInt32(bytes, pos + 4);
            int body = pos + 8;
            if (id == "fmt ")
            {
                format = BitConverter.ToUInt16(bytes, body);
                channels = BitConverter.ToUInt16(bytes, body + 2);
                rate = BitConverter.ToInt32(bytes, body + 4);
                bits = BitConverter.ToUInt16(bytes, body + 14);
                if (format == 0xFFFE && size >= 26) format = BitConverter.ToUInt16(bytes, body + 24);
            }
            else if (id == "data")
            {
                size = Math.Min(size, bytes.Length - body);
                int bytesPer = bits / 8;
                int frames = size / (bytesPer * channels);
                mono = new float[frames];
                for (int f = 0; f < frames; f++)
                {
                    float sum = 0;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        int at = body + (f * channels + ch) * bytesPer;
                        sum += (format, bits) switch
                        {
                            (3, 32) => BitConverter.ToSingle(bytes, at),
                            (_, 16) => BitConverter.ToInt16(bytes, at) / 32768f,
                            (_, 24) => ((bytes[at] | bytes[at + 1] << 8 | (sbyte)bytes[at + 2] << 16)) / 8388608f,
                            (_, 32) => BitConverter.ToInt32(bytes, at) / 2147483648f,
                            (_, 8) => (bytes[at] - 128) / 128f,
                            _ => throw new FluesterException($"WAV-Format nicht unterstützt ({bits} Bit)"),
                        };
                    }
                    mono[f] = sum / channels;
                }
            }
            pos = body + size + (size & 1);
        }
        if (mono == null) throw new FluesterException("WAV-Datei ohne Audiodaten");
        return Resample(mono, rate, SampleRate);
    }

    /// <summary>RMS-Pegel 0…1 für die Wellenform (logarithmisch gestaucht).</summary>
    public static float Level(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0) return 0;
        double sum = 0;
        foreach (var s in samples) sum += s * s;
        double rms = Math.Sqrt(sum / samples.Length);
        double db = 20 * Math.Log10(Math.Max(rms, 1e-6));
        return (float)Math.Clamp((db + 55) / 45, 0, 1);
    }

    /// <summary>Hat die Aufnahme überhaupt Sprache? Verhindert Halluzinationen bei Stille.</summary>
    public static bool HasSpeech(ReadOnlySpan<float> samples, int sampleRate)
    {
        int frame = Math.Max(1, sampleRate / 50);
        int loud = 0;
        for (int i = 0; i + frame <= samples.Length; i += frame)
        {
            double sum = 0;
            for (int j = i; j < i + frame; j++) sum += samples[j] * samples[j];
            if (Math.Sqrt(sum / frame) > 0.012) loud++;
        }
        return loud >= 8;
    }

    /// <summary>Stille am Anfang/Ende abschneiden, mit 200 ms Rand.</summary>
    public static float[] TrimSilence(float[] samples, int sampleRate, float threshold = 0.008f)
    {
        int frame = Math.Max(1, sampleRate / 100);
        bool Loud(int start)
        {
            float m = 0;
            for (int j = start; j < Math.Min(samples.Length, start + frame); j++) m = Math.Max(m, Math.Abs(samples[j]));
            return m > threshold * 3;
        }
        int first = 0;
        while (first + frame < samples.Length && !Loud(first)) first += frame;
        int last = samples.Length - frame;
        while (last > first && !Loud(Math.Max(0, last))) last -= frame;
        int pad = sampleRate / 5;
        int a = Math.Max(0, first - pad), b = Math.Min(samples.Length, last + frame + pad);
        return a < b ? samples[a..b] : samples;
    }

    /// <summary>Lineares Resampling (reicht für Sprache).</summary>
    public static float[] Resample(float[] samples, double from, double to)
    {
        if (from == to || from <= 0 || to <= 0 || samples.Length == 0) return samples;
        double ratio = from / to;
        int n = (int)(samples.Length / ratio);
        var output = new float[n];
        for (int i = 0; i < n; i++)
        {
            double x = i * ratio;
            int i0 = (int)x, i1 = Math.Min(i0 + 1, samples.Length - 1);
            float f = (float)(x - i0);
            output[i] = samples[i0] * (1 - f) + samples[i1] * f;
        }
        return output;
    }

    /// <summary>16-Bit-PCM (little endian) → Float.</summary>
    public static float[] FromPcm16(ReadOnlySpan<byte> pcm)
    {
        var output = new float[pcm.Length / 2];
        for (int i = 0; i < output.Length; i++) output[i] = BitConverter.ToInt16(pcm.Slice(i * 2, 2)) / 32768f;
        return output;
    }

    /// <summary>Wo im Datenstrom die Samples beginnen: hinter einem WAV-Kopf (RIFF … data), sonst 0 (rohes PCM).
    /// Manche Aufnahme-Werkzeuge schreiben je nach Version einen Kopf vor die Daten.</summary>
    public static int PcmStart(ReadOnlySpan<byte> stream)
    {
        if (stream.Length < 12 || !stream[..4].SequenceEqual("RIFF"u8) || !stream.Slice(8, 4).SequenceEqual("WAVE"u8)) return 0;
        int pos = 12;
        while (pos + 8 <= stream.Length)
        {
            int size = BitConverter.ToInt32(stream.Slice(pos + 4, 4));
            if (stream.Slice(pos, 4).SequenceEqual("data"u8)) return pos + 8;
            if (size < 0) break;
            pos += 8 + size + (size & 1);
        }
        return Math.Min(44, stream.Length);
    }
}
