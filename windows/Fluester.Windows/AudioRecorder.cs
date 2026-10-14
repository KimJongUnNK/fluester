using Fluester.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Fluester.Windows;

/// <summary>Mikrofon über WASAPI (gemeinsamer Modus). Sammelt mono im Geräteformat und rechnet am Ende auf 16 kHz um.
/// Für die Wellenform kommt laufend ein Pegel 0…1.</summary>
sealed class AudioRecorder : IDisposable
{
    public event Action<float>? Level;

    WasapiCapture? capture;
    readonly List<float> buffer = new();
    readonly object gate = new();
    int deviceRate = 48000;
    public bool IsRecording { get; private set; }
    public double Elapsed => IsRecording ? (DateTime.UtcNow - started).TotalSeconds : 0;
    DateTime started;

    public sealed record Device(string Id, string Name, bool IsDefault);

    public static List<Device> Devices()
    {
        var list = new List<Device>();
        try
        {
            using var en = new MMDeviceEnumerator();
            string? def = null;
            try { def = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications).ID; } catch { }
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                list.Add(new Device(d.ID, d.FriendlyName, d.ID == def));
        }
        catch (Exception e)
        {
            Fluester.Engine.Log.Write("Mikrofone: " + e.Message);
        }
        return list;
    }

    static MMDevice? Find(string id)
    {
        using var en = new MMDeviceEnumerator();
        if (!string.IsNullOrEmpty(id))
        {
            // Abgesteckte Geräte liefert GetDevice weiterhin (Zustand „Unplugged“) – dann den Standard nehmen
            try { var d = en.GetDevice(id); if (d.State == DeviceState.Active) return d; } catch { }
            Fluester.Engine.Log.Write("Gewähltes Mikrofon nicht angeschlossen – nehme den Standard");
        }
        try { return en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); }
        catch { return null; }
    }

    public void Start(string deviceId)
    {
        Stop();
        var device = Find(deviceId) ?? throw new FluesterException("Kein Mikrofon gefunden – Einstellungen › Allgemein › Mikrofon");
        lock (gate) buffer.Clear();
        try
        {
            capture = new WasapiCapture(device, useEventSync: true, audioBufferMillisecondsLength: 50);
        }
        catch (Exception e)
        {
            throw MicError(e);
        }
        var fmt = capture.WaveFormat;
        deviceRate = fmt.SampleRate;
        int channels = fmt.Channels;
        bool isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat || (fmt is WaveFormatExtensible ext && ext.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);
        int bits = fmt.BitsPerSample;
        capture.DataAvailable += (_, e) =>
        {
            int bytesPerSample = bits / 8;
            int frames = e.BytesRecorded / (bytesPerSample * channels);
            var mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int at = (f * channels + c) * bytesPerSample;
                    sum += isFloat && bits == 32 ? BitConverter.ToSingle(e.Buffer, at)
                        : bits == 16 ? BitConverter.ToInt16(e.Buffer, at) / 32768f
                        : bits == 24 ? (e.Buffer[at] | e.Buffer[at + 1] << 8 | (sbyte)e.Buffer[at + 2] << 16) / 8388608f
                        : bits == 32 ? BitConverter.ToInt32(e.Buffer, at) / 2147483648f : 0;
                }
                mono[f] = sum / channels;
            }
            lock (gate)
            {
                // Sicherheitsgrenze: 15 Minuten
                if (buffer.Count < deviceRate * 900) buffer.AddRange(mono);
            }
            Level?.Invoke(AudioUtil.Level(mono));
        };
        capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception != null) Fluester.Engine.Log.Write("Aufnahme gestoppt: " + e.Exception.Message);
        };
        try
        {
            capture.StartRecording();
        }
        catch (Exception e)
        {
            capture.Dispose();
            capture = null;
            throw MicError(e);
        }
        started = DateTime.UtcNow;
        IsRecording = true;
    }

    static Exception MicError(Exception e) =>
        e.HResult == unchecked((int)0x80070005)
            ? new FluesterException("Mikrofon gesperrt – Windows-Einstellungen › Datenschutz › Mikrofon › „Desktop-Apps Zugriff erlauben“")
            : new FluesterException("Mikrofon lässt sich nicht öffnen: " + e.Message);

    /// <summary>Beendet die Aufnahme und liefert 16 kHz mono.</summary>
    public float[] Stop()
    {
        if (capture != null)
        {
            try { capture.StopRecording(); } catch { }
            capture.Dispose();
            capture = null;
        }
        IsRecording = false;
        float[] raw;
        lock (gate) { raw = buffer.ToArray(); buffer.Clear(); }
        return AudioUtil.Resample(raw, deviceRate, AudioUtil.SampleRate);
    }

    /// <summary>Kurz prüfen, ob das Mikrofon geöffnet werden darf (Einrichtung).</summary>
    public static string? Probe(string deviceId)
    {
        using var r = new AudioRecorder();
        try
        {
            r.Start(deviceId);
            Thread.Sleep(150);
            r.Stop();
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public void Dispose() => Stop();
}
