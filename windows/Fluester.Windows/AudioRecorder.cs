using Fluester.Core;
using Fluester.Engine;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Fluester.Windows;

/// <summary>Mikrofon über WASAPI (gemeinsamer Modus). Sammelt mono im Geräteformat und rechnet am Ende auf 16 kHz um.
/// Für die Wellenform kommt laufend ein Pegel 0…1. Fällt das Mikrofon mitten im Diktat weg (Kabel, USB-Hub, Treiber),
/// nimmt der Systemstandard weiter auf.</summary>
sealed class AudioRecorder : IDisposable
{
    public event Action<float>? Level;

    WasapiCapture? capture;
    // Ein Abschnitt je Gerät, jeder mit eigener Rate – nach einem Wechsel mitten im Diktat sind es zwei
    readonly List<(int Rate, List<float> Samples)> segments = new();
    readonly object gate = new();
    bool stopping, switched;
    public bool IsRecording { get; private set; }
    public double Elapsed => IsRecording ? (DateTime.UtcNow - started).TotalSeconds : 0;
    DateTime started;

    /// <summary>Das gewählte Mikrofon, so wie es gerade heißt – auch wenn es unter neuer Id wiedergefunden wurde.
    /// null = Systemstandard (so eingestellt oder weil das gewählte fehlt).</summary>
    public Microphone? Selected { get; private set; }
    /// <summary>Das gewählte Mikrofon fehlt oder streikt, es läuft der Systemstandard.</summary>
    public bool Missing { get; private set; }

    public static List<Microphone> Devices()
    {
        var list = new List<Microphone>();
        try
        {
            using var en = new MMDeviceEnumerator();
            var def = Default(en)?.ID;
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                list.Add(new Microphone(d.ID, d.FriendlyName, IsMuted(d), d.ID == def));
        }
        catch (Exception e)
        {
            Log.Write("Mikrofone: " + e.Message);
        }
        return list;
    }

    static bool IsMuted(MMDevice d)
    {
        try { return d.AudioEndpointVolume.Mute; } catch { return false; }
    }

    /// <summary>Standard-Eingang aus den Windows-Einstellungen („Konsole“), sonst der für Kommunikation.</summary>
    static MMDevice? Default(MMDeviceEnumerator en)
    {
        foreach (var role in new[] { Role.Console, Role.Communications })
            try { return en.GetDefaultAudioEndpoint(DataFlow.Capture, role); } catch { }
        return null;
    }

    /// <summary>Das gewählte Mikrofon – notfalls unter neuer Id wiedergefunden (anderer USB-Port oder Hub) –, sonst der Standard.</summary>
    MMDevice? Find(MMDeviceEnumerator en, string id, string name)
    {
        Selected = null;
        Missing = false;
        if (id.Length > 0)
        {
            // Abgesteckte Geräte liefert GetDevice weiterhin (Zustand „Unplugged“) – die zählen nicht
            try { var d = en.GetDevice(id); if (d.State == DeviceState.Active) { Selected = new Microphone(d.ID, d.FriendlyName); return d; } } catch { }
            if (MicrophoneParser.Find(Devices(), id, name) is { } moved)
            {
                try
                {
                    var d = en.GetDevice(moved.Id);
                    Log.Write($"Mikrofon „{moved.Name}“ unter neuer Id wiedergefunden (anderer USB-Port oder Hub)");
                    Selected = moved;
                    return d;
                }
                catch { }
            }
            Missing = true;
            Log.Write($"Gewähltes Mikrofon „{(name.Length > 0 ? name : id)}“ nicht angeschlossen – nehme den Standard");
        }
        return Default(en);
    }

    public void Start(string deviceId, string deviceName = "")
    {
        Stop();
        lock (gate) { segments.Clear(); stopping = false; switched = false; }
        using var en = new MMDeviceEnumerator();
        var device = Find(en, deviceId, deviceName) ?? throw new FluesterException("Kein Mikrofon gefunden – Einstellungen › Allgemein › Mikrofon");
        try
        {
            Open(device);
        }
        catch (FluesterException) when (Selected != null)
        {
            // Frisch eingesteckte oder gerade aufgewachte USB-Geräte sind manchmal noch nicht bereit
            Thread.Sleep(250);
            try
            {
                Open(device);
            }
            catch (FluesterException e) when (Default(en) is { } fallback && fallback.ID != device.ID)
            {
                Log.Write($"Mikrofon „{Selected.Name}“ lässt sich nicht öffnen ({e.Message}) – nehme den Standard");
                Selected = null;
                Missing = true;
                Open(fallback);
            }
        }
        started = DateTime.UtcNow;
        IsRecording = true;
    }

    void Open(MMDevice device)
    {
        WasapiCapture c;
        try
        {
            c = new WasapiCapture(device, useEventSync: true, audioBufferMillisecondsLength: 50);
        }
        catch (Exception e)
        {
            throw MicError(e);
        }
        var fmt = c.WaveFormat;
        int rate = fmt.SampleRate, channels = fmt.Channels, bits = fmt.BitsPerSample;
        bool isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat || (fmt is WaveFormatExtensible ext && ext.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);
        if (IsMuted(device)) Log.Write($"Mikrofon „{device.FriendlyName}“ ist in Windows stummgeschaltet");
        var segment = new List<float>();
        c.DataAvailable += (_, e) =>
        {
            int bytesPerSample = bits / 8;
            var raw = new float[e.BytesRecorded / (bytesPerSample * channels) * channels];
            for (int i = 0; i < raw.Length; i++)
            {
                int at = i * bytesPerSample;
                raw[i] = isFloat && bits == 32 ? BitConverter.ToSingle(e.Buffer, at)
                    : bits == 16 ? BitConverter.ToInt16(e.Buffer, at) / 32768f
                    : bits == 24 ? (e.Buffer[at] | e.Buffer[at + 1] << 8 | (sbyte)e.Buffer[at + 2] << 16) / 8388608f
                    : bits == 32 ? BitConverter.ToInt32(e.Buffer, at) / 2147483648f : 0;
            }
            var mono = AudioUtil.Downmix(raw, channels);
            lock (gate)
            {
                // Sicherheitsgrenze: 15 Minuten
                if (segment.Count < rate * 900) segment.AddRange(mono);
            }
            Level?.Invoke(AudioUtil.Level(mono));
        };
        c.RecordingStopped += (_, e) =>
        {
            if (e.Exception == null) return;
            Log.Write("Aufnahme gestoppt: " + e.Exception.Message);
            ThreadPool.QueueUserWorkItem(_ => Recover(c));
        };
        try
        {
            c.StartRecording();
        }
        catch (Exception e)
        {
            c.Dispose();
            throw MicError(e);
        }
        lock (gate)
        {
            segments.Add((rate, segment));
            capture = c;
        }
    }

    /// <summary>Mikrofon mitten im Diktat weg: einmal auf den Systemstandard wechseln und weiter aufnehmen,
    /// statt den Rest still zu verlieren.</summary>
    void Recover(WasapiCapture dead)
    {
        lock (gate)
        {
            if (stopping || switched || capture != dead) return;
            switched = true;
            capture = null;
        }
        try { dead.Dispose(); } catch { }
        try
        {
            using var en = new MMDeviceEnumerator();
            var d = Default(en) ?? throw new FluesterException("keins angeschlossen");
            Open(d);
            Selected = null;
            Missing = true;
            Log.Write($"Weiter mit „{d.FriendlyName}“");
            bool late;
            lock (gate) late = stopping;
            if (late) Dispose(TakeCapture());
        }
        catch (Exception e)
        {
            Log.Write("Kein Ersatz-Mikrofon: " + e.Message);
        }
    }

    static Exception MicError(Exception e) => e.HResult switch
    {
        unchecked((int)0x80070005) => new FluesterException("Mikrofon gesperrt – Windows-Einstellungen › Datenschutz › Mikrofon › „Desktop-Apps Zugriff erlauben“"),
        unchecked((int)0x8889000A) => new FluesterException("Mikrofon ist von einer anderen App exklusiv belegt – dort freigeben oder anderes Mikrofon wählen"),
        _ => new FluesterException("Mikrofon lässt sich nicht öffnen: " + e.Message),
    };

    WasapiCapture? TakeCapture()
    {
        lock (gate)
        {
            var c = capture;
            capture = null;
            return c;
        }
    }

    // Außerhalb von gate: Dispose wartet auf den Aufnahme-Thread, und der braucht gate für die Samples
    static void Dispose(WasapiCapture? c)
    {
        if (c == null) return;
        try { c.StopRecording(); } catch { }
        c.Dispose();
    }

    /// <summary>Beendet die Aufnahme und liefert 16 kHz mono.</summary>
    public float[] Stop()
    {
        lock (gate) stopping = true;
        Dispose(TakeCapture());
        IsRecording = false;
        lock (gate)
        {
            var all = segments.SelectMany(s => AudioUtil.Resample(s.Samples.ToArray(), s.Rate, AudioUtil.SampleRate)).ToArray();
            segments.Clear();
            return all;
        }
    }

    /// <summary>Kurz prüfen, ob das Mikrofon geöffnet werden darf (Einrichtung).</summary>
    public static string? Probe(string deviceId, string deviceName = "")
    {
        using var r = new AudioRecorder();
        try
        {
            r.Start(deviceId, deviceName);
            Thread.Sleep(150);
            r.Stop();
            return r.Missing ? "Gewähltes Mikrofon nicht angeschlossen – Windows-Standard funktioniert" : null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public void Dispose() => Stop();
}
