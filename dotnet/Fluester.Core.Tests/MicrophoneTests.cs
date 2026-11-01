using Fluester.Engine;

namespace Fluester.Core.Tests;

/// <summary>Mikrofon-Listen unter Linux: Ausgaben von pactl, pw-dump und arecord, wie sie auf echten Systemen aussehen.</summary>
public class MicrophoneTests
{
    // LC_ALL=C pactl list sources – PipeWire (Fedora/Ubuntu): Monitor, eingebautes Mikro, USB-Mikro (stumm)
    const string PactlSources = """
        Source #55
        	State: SUSPENDED
        	Name: alsa_output.pci-0000_00_1f.3.analog-stereo.monitor
        	Description: Monitor of Built-in Audio Analog Stereo
        	Driver: PipeWire
        	Sample Specification: s32le 2ch 48000Hz
        	Channel Map: front-left,front-right
        	Owner Module: 4294967295
        	Mute: no
        	Volume: front-left: 65536 / 100% / 0.00 dB,   front-right: 65536 / 100% / 0.00 dB
        	        balance 0.00
        	Base Volume: 65536 / 100% / 0.00 dB
        	Monitor of Sink: alsa_output.pci-0000_00_1f.3.analog-stereo
        	Latency: 0 usec, configured 0 usec
        	Flags: DECIBEL_VOLUME LATENCY
        	Properties:
        		device.description = "Built-in Audio Analog Stereo"
        		device.class = "monitor"
        		node.name = "alsa_output.pci-0000_00_1f.3.analog-stereo"
        	Formats:
        		pcm

        Source #56
        	State: SUSPENDED
        	Name: alsa_input.pci-0000_00_1f.3.analog-stereo
        	Description: Built-in Audio Analog Stereo
        	Driver: PipeWire
        	Sample Specification: s32le 2ch 48000Hz
        	Mute: no
        	Monitor of Sink: n/a
        	Properties:
        		alsa.name = "ALC3246 Analog"
        		device.description = "Built-in Audio Analog Stereo"
        		device.class = "sound"
        	Ports:
        		analog-input-internal-mic: Internal Microphone (type: Mic, priority: 8900, availability unknown)
        	Active Port: analog-input-internal-mic
        	Formats:
        		pcm

        Source #71
        	State: RUNNING
        	Name: alsa_input.usb-Blue_Microphones_Yeti_Stereo_Microphone_REV8-00.analog-stereo
        	Description: Yeti Stereo Microphone Analog Stereo
        	Driver: PipeWire
        	Mute: yes
        	Monitor of Sink: n/a
        	Properties:
        		device.class = "sound"
        		device.description = "Yeti Stereo Microphone"
        """;

    const string PactlInfo = """
        Server String: /run/user/1000/pulse/native
        Library Protocol Version: 35
        Server Name: PulseAudio (on PipeWire 1.2.7)
        Default Sink: alsa_output.pci-0000_00_1f.3.analog-stereo
        Default Source: alsa_input.usb-Blue_Microphones_Yeti_Stereo_Microphone_REV8-00.analog-stereo
        Cookie: 1b2f:6c3a
        """;

    [Fact]
    public void PactlListsMicrophonesWithoutMonitors()
    {
        var def = MicrophoneParser.PactlDefault(PactlInfo);
        var mics = MicrophoneParser.Pactl(PactlSources.Replace("\n", "\r\n"), def);
        Assert.Equal(2, mics.Count);
        Assert.Equal(new Microphone("alsa_input.pci-0000_00_1f.3.analog-stereo", "Built-in Audio Analog Stereo"), mics[0]);
        Assert.Equal("Yeti Stereo Microphone Analog Stereo", mics[1].Name);
        Assert.True(mics[1].Muted);
        Assert.True(mics[1].IsDefault);
        Assert.False(mics[0].IsDefault);
    }

    [Fact]
    public void PactlFromPulseAudioWithoutDescriptionFallsBackToName()
    {
        var mics = MicrophoneParser.Pactl("Source #0\n\tName: fluester_sine\n\tMute: no\n\tMonitor of Sink: n/a\n\nSource #1\n\tName: stumm.monitor\n\tMonitor of Sink: n/a\n");
        Assert.Equal(new[] { new Microphone("fluester_sine", "fluester_sine") }, mics);
        Assert.Empty(MicrophoneParser.Pactl(""));
        Assert.Null(MicrophoneParser.PactlDefault("Server Name: x\nDefault Source: \n"));
    }

    // pw-dump (gekürzt): Standard-Metadaten, Mikrofon, virtuelle Rauschunterdrückung, Lautsprecher, App-Aufnahme
    const string PwDump = """
        [
          { "id": 0, "type": "PipeWire:Interface:Core", "info": { "name": "pipewire-0" } },
          { "id": 33, "type": "PipeWire:Interface:Metadata", "props": { "metadata.name": "default" },
            "metadata": [
              { "subject": 0, "key": "default.audio.sink", "type": "Spa:String:JSON", "value": { "name": "alsa_output.pci-0000_00_1f.3.analog-stereo" } },
              { "subject": 0, "key": "default.audio.source", "type": "Spa:String:JSON", "value": { "name": "alsa_input.usb-RODE_NT-USB-00.analog-stereo" } }
            ] },
          { "id": 48, "type": "PipeWire:Interface:Node", "info": { "props": {
              "media.class": "Audio/Source", "node.name": "alsa_input.pci-0000_00_1f.3.analog-stereo", "node.description": "Eingebautes Tonsystem Analog Stereo", "node.nick": "ALC3246 Analog" } } },
          { "id": 52, "type": "PipeWire:Interface:Node", "info": { "props": {
              "media.class": "Audio/Source", "node.name": "alsa_input.usb-RODE_NT-USB-00.analog-stereo", "node.nick": "RØDE NT-USB" } } },
          { "id": 60, "type": "PipeWire:Interface:Node", "info": { "props": {
              "media.class": "Audio/Source/Virtual", "node.name": "rnnoise_source", "node.description": "Rauschunterdrückung" } } },
          { "id": 49, "type": "PipeWire:Interface:Node", "info": { "props": {
              "media.class": "Audio/Sink", "node.name": "alsa_output.pci-0000_00_1f.3.analog-stereo", "node.description": "Lautsprecher" } } },
          { "id": 77, "type": "PipeWire:Interface:Node", "info": { "props": {
              "media.class": "Stream/Input/Audio", "node.name": "Firefox", "application.name": "Firefox" } } },
          { "id": 80, "type": "PipeWire:Interface:Node", "info": null }
        ]
        """;

    [Fact]
    public void PwDumpListsSourcesAndDefault()
    {
        var mics = MicrophoneParser.PwDump(PwDump);
        Assert.Equal(new[] { "alsa_input.pci-0000_00_1f.3.analog-stereo", "alsa_input.usb-RODE_NT-USB-00.analog-stereo", "rnnoise_source" }, mics.Select(m => m.Id));
        Assert.Equal("Eingebautes Tonsystem Analog Stereo", mics[0].Name);
        Assert.Equal("RØDE NT-USB", mics[1].Name);
        Assert.True(mics[1].IsDefault);
        Assert.Equal(1, mics.Count(m => m.IsDefault));
        Assert.Empty(MicrophoneParser.PwDump("kaputt"));
        Assert.Empty(MicrophoneParser.PwDump("{}"));
    }

    // LC_ALL=C arecord -L (Fedora mit PipeWire, eingebautes Mikro + USB-Headset)
    const string ArecordL = """
        null
            Discard all samples (playback) or generate zero samples (capture)
        pipewire
            PipeWire Sound Server
        default
            Default ALSA Output (currently PipeWire Media Server)
        sysdefault:CARD=PCH
            HDA Intel PCH, ALC3246 Analog
            Default Audio Device
        front:CARD=PCH,DEV=0
            HDA Intel PCH, ALC3246 Analog
            Front output / input
        hw:CARD=PCH,DEV=0
            HDA Intel PCH, ALC3246 Analog
            Direct hardware device without any conversions
        plughw:CARD=PCH,DEV=0
            HDA Intel PCH, ALC3246 Analog
            Hardware device with all software conversions
        dsnoop:CARD=Headset,DEV=0
            Logitech USB Headset, USB Audio
            Direct sample snooping device
        plughw:CARD=Headset,DEV=0
            Logitech USB Headset, USB Audio
            Hardware device with all software conversions
        """;

    [Fact]
    public void ArecordListsOnlyPlughwDevices()
    {
        var mics = MicrophoneParser.Arecord(ArecordL);
        Assert.Equal(new[]
        {
            new Microphone("plughw:CARD=PCH,DEV=0", "HDA Intel PCH, ALC3246 Analog"),
            new Microphone("plughw:CARD=Headset,DEV=0", "Logitech USB Headset, USB Audio"),
        }, mics);
    }

    [Fact]
    public void PcmStartSkipsWavHeaderOnly()
    {
        var pcm = new byte[] { 1, 0, 2, 0, 3, 0 };
        Assert.Equal(0, AudioUtil.PcmStart(pcm));
        Assert.Equal(0, AudioUtil.PcmStart(Array.Empty<byte>()));
        // Kopf mit zusätzlichem LIST-Chunk vor „data“
        var wav = new List<byte>();
        void Add(string s) => wav.AddRange(System.Text.Encoding.ASCII.GetBytes(s));
        void Int(int i) => wav.AddRange(BitConverter.GetBytes(i));
        Add("RIFF"); Int(-1); Add("WAVE");
        Add("fmt "); Int(16); wav.AddRange(new byte[16]);
        Add("LIST"); Int(5); wav.AddRange(new byte[6]);   // ungerade Größe + Füllbyte
        Add("data"); Int(-1);
        int start = wav.Count;
        wav.AddRange(pcm);
        Assert.Equal(start, AudioUtil.PcmStart(wav.ToArray()));
        // Kopf ohne „data“ in den ersten Bytes: Standard-Kopflänge
        Assert.Equal(44, AudioUtil.PcmStart(wav.Take(12).Concat(new byte[100]).ToArray()));
    }

    [Fact]
    public void MatchesByNumberIdOrNamePart()
    {
        var mics = MicrophoneParser.Pactl(PactlSources);
        Assert.Equal("Built-in Audio Analog Stereo", Assert.Single(MicrophoneParser.Matches(mics, "1")).Name);
        Assert.Equal("Yeti Stereo Microphone Analog Stereo", Assert.Single(MicrophoneParser.Matches(mics, " 2 ")).Name);
        Assert.Empty(MicrophoneParser.Matches(mics, "3"));
        Assert.Empty(MicrophoneParser.Matches(mics, "0"));
        Assert.Single(MicrophoneParser.Matches(mics, "yeti"));
        Assert.Single(MicrophoneParser.Matches(mics, "alsa_input.pci-0000_00_1f.3.analog-stereo"));
        Assert.Single(MicrophoneParser.Matches(mics, "Blue_Microphones"));
        Assert.Equal(2, MicrophoneParser.Matches(mics, "analog stereo").Count);
        Assert.Empty(MicrophoneParser.Matches(mics, "Rode"));
        Assert.Empty(MicrophoneParser.Matches(mics, ""));
    }

    // Windows-Endpunkte: USB-Mikro am Hub umgesteckt → neue Id, Windows nummeriert den Namen („2- “)
    static readonly List<Microphone> WindowsMics = new()
    {
        new("{0.0.1.00000000}.{aaaa}", "Mikrofonarray (Realtek(R) Audio)", IsDefault: true),
        new("{0.0.1.00000000}.{bbbb}", "Mikrofon (2- Yeti Stereo Microphone)"),
    };

    [Fact]
    public void FindPrefersExactId()
    {
        Assert.Equal("{0.0.1.00000000}.{bbbb}", MicrophoneParser.Find(WindowsMics, "{0.0.1.00000000}.{bbbb}", "irgendwas")!.Id);
        Assert.Null(MicrophoneParser.Find(WindowsMics, "", "Mikrofon (Yeti Stereo Microphone)"));
    }

    [Fact]
    public void FindsUsbMicrophoneOnAnotherPortByName()
    {
        // Gemerkt am alten Port ohne Nummer, jetzt mit „2- “ unter neuer Id
        var hit = MicrophoneParser.Find(WindowsMics, "{0.0.1.00000000}.{alt}", "Mikrofon (Yeti Stereo Microphone)");
        Assert.Equal("{0.0.1.00000000}.{bbbb}", hit!.Id);
        // Ohne gemerkten Namen (Einstellungen von vor dem Update) kein Raten
        Assert.Null(MicrophoneParser.Find(WindowsMics, "{0.0.1.00000000}.{alt}", ""));
        Assert.Null(MicrophoneParser.Find(WindowsMics, "{0.0.1.00000000}.{alt}", "Mikrofon (Rode NT-USB)"));
    }

    [Fact]
    public void FindRefusesWhenTwoIdenticalMicrophonesMatch()
    {
        var two = new List<Microphone>
        {
            new("{a}", "Mikrofon (USB Audio Device)"),
            new("{b}", "Mikrofon (2- USB Audio Device)"),
        };
        Assert.Null(MicrophoneParser.Find(two, "{alt}", "Mikrofon (USB Audio Device)"));
    }

    [Fact]
    public void FindsPipeWireSourceAfterProfileOrPortChange()
    {
        var mics = new List<Microphone>
        {
            new("alsa_input.pci-0000_00_1f.3.analog-stereo", "Built-in Audio Analog Stereo"),
            new("alsa_input.usb-Blue_Microphones_Yeti_Stereo_Microphone_REV8-00.mono-fallback", "Yeti Stereo Microphone Mono"),
            new("alsa_input.pci-0000_00_14.0-usb-0_3_1.0.analog-stereo", "USB PnP Audio Device Analog Stereo"),
        };
        // Profil gewechselt (Stereo → Mono): gleiche Id ohne Profil
        Assert.Equal(mics[1], MicrophoneParser.Find(mics, "alsa_input.usb-Blue_Microphones_Yeti_Stereo_Microphone_REV8-00.analog-stereo", ""));
        // Gerät ohne Seriennummer an anderem Hub-Port: Pfad neu, Name gleich
        Assert.Equal(mics[2], MicrophoneParser.Find(mics, "alsa_input.pci-0000_00_14.0-usb-0_2_4.0.analog-stereo", "USB PnP Audio Device Analog Stereo"));
        Assert.Null(MicrophoneParser.Find(mics, "alsa_input.usb-Elgato_Wave_3-00.analog-stereo", "Wave:3 Analog Stereo"));
    }

    [Theory]
    [InlineData("Mikrofon (2- Yeti Stereo Microphone)", "mikrofon (yeti stereo microphone)")]
    [InlineData("12- USB Audio Device", "usb audio device")]
    [InlineData("Yeti Stereo Microphone Analog Stereo", "yeti stereo microphone")]
    [InlineData("Yeti Stereo Microphone Mono", "yeti stereo microphone")]
    [InlineData("HD Pro Webcam C920 Digital Stereo (IEC958)", "hd pro webcam c920 digital stereo (iec958)")]
    [InlineData("  RØDE   NT-USB  ", "røde nt-usb")]
    [InlineData("Model 2-Channel Interface", "model 2-channel interface")]
    public void NormalizeNameIgnoresPortNumbersAndProfiles(string name, string expected) =>
        Assert.Equal(expected, MicrophoneParser.NormalizeName(name));

    [Fact]
    public void StableIdDropsOnlyThePipeWireProfile()
    {
        Assert.Equal("alsa_input.usb-RODE_NT-USB-00", MicrophoneParser.StableId("alsa_input.usb-RODE_NT-USB-00.analog-stereo"));
        Assert.Equal("{0.0.1.00000000}.{bbbb}", MicrophoneParser.StableId("{0.0.1.00000000}.{bbbb}"));
        Assert.Equal("plughw:CARD=Headset,DEV=0", MicrophoneParser.StableId("plughw:CARD=Headset,DEV=0"));
        Assert.Equal("alsa_input.", MicrophoneParser.StableId("alsa_input."));
    }

    [Fact]
    public void MicrophoneNameSurvivesSettingsRoundTripAndOldFiles()
    {
        var s = new Settings { MicrophoneId = "{b}", MicrophoneName = "Mikrofon (Yeti)" };
        Assert.Equal("Mikrofon (Yeti)", Settings.FromJson(s.ToJson()).MicrophoneName);
        Assert.Equal("", Settings.FromJson("""{ "microphoneId": "{b}" }""").MicrophoneName);
        Assert.Equal("", Settings.FromJson("""{ "microphoneName": null }""").MicrophoneName);
    }

    [Fact]
    public void DownmixKeepsVoiceOnASingleChannel()
    {
        // Audio-Interface: Mikro an Eingang 2, Eingang 1 leer → volle Lautstärke statt halber oder Stille
        var interleaved = new float[] { 0, 0.5f, 0, -0.5f, 0, 0.25f };
        Assert.Equal(new[] { 0.5f, -0.5f, 0.25f }, AudioUtil.Downmix(interleaved, 2));
    }

    [Fact]
    public void DownmixAveragesActiveChannels()
    {
        Assert.Equal(new[] { 0.3f, -0.2f }, AudioUtil.Downmix(new float[] { 0.2f, 0.4f, -0.2f, -0.2f }, 2));
        // Rauschen 30 dB unter der Stimme zählt nicht mit
        var mixed = AudioUtil.Downmix(new float[] { 0.5f, 0.01f, -0.5f, -0.01f }, 2);
        Assert.Equal(new[] { 0.5f, -0.5f }, mixed);
        Assert.Equal(new[] { 0f, 0f }, AudioUtil.Downmix(new float[4], 2));
        Assert.Equal(new[] { 0.1f, 0.2f }, AudioUtil.Downmix(new float[] { 0.1f, 0.2f }, 1));
        // Unvollständiger letzter Frame fällt weg; viele Kanäle (Interface mit 18 Eingängen)
        Assert.Single(AudioUtil.Downmix(new float[] { 0.1f, 0.1f, 0.1f }, 2));
        var wide = new float[18 * 2];
        wide[17] = 0.8f; wide[18 + 17] = -0.8f;
        Assert.Equal(new[] { 0.8f, -0.8f }, AudioUtil.Downmix(wide, 18));
    }
}
