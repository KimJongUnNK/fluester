using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Fluester.Core;
using Fluester.Engine;

namespace Fluester.Linux;

/// <summary>Anbindung an den Desktop über Standard-Werkzeuge: Zwischenablage (wl-copy/xclip/xsel), aktives Fenster
/// (xdotool, hyprctl, swaymsg), Benachrichtigungen (notify-send), Töne (pw-play/paplay/aplay).</summary>
static class Desktop
{
    public static bool Wayland => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    public static bool X11 => !Wayland && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));
    public static string Session => Wayland ? "Wayland" : X11 ? "X11" : "keine grafische Sitzung";
    public static string DesktopName => Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "?";

    /// <summary>Unter systemd/Autostart fehlt manchmal die Grafik-Umgebung – aus /run/user ableiten.</summary>
    public static void RepairEnvironment()
    {
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? $"/run/user/{Libc.geteuid()}";
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")) && Directory.Exists(runtime))
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", runtime);
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            var wl = Directory.Exists(runtime) ? Directory.GetFiles(runtime, "wayland-*").FirstOrDefault(f => !f.EndsWith(".lock")) : null;
            if (wl != null) Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", Path.GetFileName(wl));
            else if (Directory.Exists("/tmp/.X11-unix") && Directory.GetFileSystemEntries("/tmp/.X11-unix").Length > 0) Environment.SetEnvironmentVariable("DISPLAY", ":0");
        }
    }

    // ───────────── Werkzeuge

    static readonly Dictionary<string, string?> found = new();

    /// <summary>Nach einer Paket-Installation neu suchen.</summary>
    public static void Rescan() { lock (found) found.Clear(); }

    public static string? Which(string tool)
    {
        lock (found)
        {
            if (found.TryGetValue(tool, out var p)) return p;
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(':').Append("/usr/bin").Append("/usr/local/bin"))
            {
                var f = Path.Combine(dir, tool);
                if (File.Exists(f)) return found[tool] = f;
            }
            return found[tool] = null;
        }
    }

    public static (int Code, string Out, string Err) Run(string file, IEnumerable<string> args, string? stdin = null, int timeoutMs = 4000,
        bool englishOutput = false)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdin != null, UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8, StandardInputEncoding = stdin != null ? new UTF8Encoding(false) : null,
        };
        // Für Ausgaben, die Flüster auswertet: unübersetzte Feldnamen („Description:“ statt „Beschreibung:“)
        if (englishOutput) { psi.Environment["LC_ALL"] = "C.UTF-8"; psi.Environment.Remove("LANGUAGE"); }
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        if (stdin != null) { p.StandardInput.Write(stdin); p.StandardInput.Close(); }
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (-1, "", "Zeitüberschreitung"); }
        // Hintergrund-Kinder (z. B. von wl-paste/xclip) können die Ausgabe offen halten – nie endlos darauf warten
        if (!Task.WaitAll(new Task[] { outTask, errTask }, Math.Max(300, timeoutMs / 2)))
        {
            Log.Write($"{Path.GetFileName(file)}: Ausgabe blieb offen – übersprungen");
            return (p.ExitCode, outTask.IsCompletedSuccessfully ? outTask.Result : "", errTask.IsCompletedSuccessfully ? errTask.Result : "");
        }
        return (p.ExitCode, outTask.Result, errTask.Result);
    }

    /// <summary>Startet ein Programm im Hintergrund (wl-copy bleibt laufen, bis die Zwischenablage ersetzt wird).</summary>
    static void Spawn(string file, IEnumerable<string> args, string stdin)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardInput = true, UseShellExecute = false, StandardInputEncoding = new UTF8Encoding(false),
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        var p = Process.Start(psi)!;
        p.StandardInput.Write(stdin);
        p.StandardInput.Close();
        p.WaitForExit(800);
    }

    // ───────────── Zwischenablage

    public static string ClipboardTool => Wayland ? (Which("wl-copy") != null ? "wl-clipboard" : "fehlt (wl-clipboard)")
        : Which("xclip") != null ? "xclip" : Which("xsel") != null ? "xsel" : "fehlt (xclip)";

    public static bool ClipboardAvailable => !ClipboardTool.StartsWith("fehlt");

    public static void SetClipboard(string text, bool primary = false)
    {
        if (Wayland && Which("wl-copy") is { } wl)
            Spawn(wl, primary ? new[] { "--primary", "--type", "text/plain;charset=utf-8" } : new[] { "--type", "text/plain;charset=utf-8" }, text);
        else if (Which("xclip") is { } xc)
            Spawn(xc, new[] { "-selection", primary ? "primary" : "clipboard", "-in" }, text);
        else if (Which("xsel") is { } xs)
            Spawn(xs, new[] { primary ? "--primary" : "--clipboard", "--input" }, text);
        else
            throw new FluesterException("Kein Zwischenablage-Werkzeug – " + (Wayland ? "wl-clipboard" : "xclip") + " installieren");
    }

    public static string? GetClipboard(bool primary = false)
    {
        try
        {
            if (Wayland && Which("wl-paste") is { } wp)
                return Ok(Run(wp, primary ? new[] { "--primary", "--no-newline" } : new[] { "--no-newline" }, timeoutMs: 1500));
            if (Which("xclip") is { } xc) return Ok(Run(xc, new[] { "-selection", primary ? "primary" : "clipboard", "-out" }, timeoutMs: 1500));
            if (Which("xsel") is { } xs) return Ok(Run(xs, new[] { primary ? "--primary" : "--clipboard", "--output" }, timeoutMs: 1500));
        }
        catch { }
        return null;
        static string? Ok((int Code, string Out, string Err) r) => r.Code == 0 ? r.Out : null;
    }

    // ───────────── Aktives Fenster (für Stil und Terminal-Erkennung)

    public static TargetApp ActiveWindow()
    {
        try
        {
            if (X11 && Which("xdotool") is { } xd)
            {
                var cls = Run(xd, new[] { "getactivewindow", "getwindowclassname" }, timeoutMs: 800).Out.Trim();
                var title = Run(xd, new[] { "getactivewindow", "getwindowname" }, timeoutMs: 800).Out.Trim();
                return new TargetApp(cls, cls.ToLowerInvariant(), title);
            }
            if (Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE") != null && Which("hyprctl") is { } hc)
            {
                using var doc = JsonDocument.Parse(Run(hc, new[] { "activewindow", "-j" }, timeoutMs: 800).Out);
                var cls = doc.RootElement.GetProperty("class").GetString() ?? "";
                return new TargetApp(cls, cls.ToLowerInvariant(), doc.RootElement.GetProperty("title").GetString());
            }
            if (Environment.GetEnvironmentVariable("SWAYSOCK") != null && Which("swaymsg") is { } sw)
            {
                using var doc = JsonDocument.Parse(Run(sw, new[] { "-t", "get_tree" }, timeoutMs: 800).Out);
                var focused = FindFocused(doc.RootElement);
                if (focused is { } f)
                {
                    var app = f.TryGetProperty("app_id", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString()
                        : f.TryGetProperty("window_properties", out var wp) && wp.TryGetProperty("class", out var c) ? c.GetString() : "";
                    return new TargetApp(app ?? "", (app ?? "").ToLowerInvariant(), f.TryGetProperty("name", out var n) ? n.GetString() : null);
                }
            }
        }
        catch (Exception e)
        {
            Log.Write("Aktives Fenster: " + e.Message);
        }
        return TargetApp.Unknown;
    }

    static JsonElement? FindFocused(JsonElement node)
    {
        if (node.TryGetProperty("focused", out var f) && f.ValueKind == JsonValueKind.True) return node;
        foreach (var key in new[] { "nodes", "floating_nodes" })
            if (node.TryGetProperty(key, out var arr))
                foreach (var child in arr.EnumerateArray())
                    if (FindFocused(child) is { } hit) return hit;
        return null;
    }

    static readonly string[] Terminals =
    {
        "gnome-terminal", "gnome-terminal-server", "org.gnome.terminal", "org.gnome.ptyxis", "ptyxis", "konsole", "org.kde.konsole", "kitty",
        "alacritty", "wezterm", "org.wezfurlong.wezterm", "foot", "xterm", "uxterm", "tilix", "terminator", "xfce4-terminal", "lxterminal",
        "mate-terminal", "terminology", "ghostty", "com.mitchellh.ghostty", "st", "urxvt", "rxvt", "blackbox", "com.raggesilver.blackbox", "warp",
    };

    public static bool IsTerminal(TargetApp app) => Terminals.Contains(app.Id) || app.Id.Contains("terminal");

    // ───────────── Benachrichtigungen und Töne

    static string? notifyId;

    public static void Notify(string title, string body, bool urgent = false)
    {
        if (Which("notify-send") is not { } ns) return;
        try
        {
            var args = new List<string> { "--app-name=Flüster", "--icon=audio-input-microphone", "--print-id", "-h", "string:x-canonical-private-synchronous:fluester" };
            if (notifyId != null) { args.Add("--replace-id=" + notifyId); }
            if (urgent) args.Add("--urgency=critical");
            args.Add(title);
            if (body.Length > 0) args.Add(body);
            var r = Run(ns, args, timeoutMs: 1500);
            if (r.Code != 0 && notifyId != null)
            {
                // Ältere notify-send ohne --replace-id
                args.RemoveAll(a => a.StartsWith("--replace-id") || a == "--print-id");
                r = Run(ns, args, timeoutMs: 1500);
            }
            var id = r.Out.Trim();
            if (id.Length > 0 && id.All(char.IsDigit)) notifyId = id;
        }
        catch { }
    }

    static readonly Lazy<Dictionary<string, string>> SoundFiles = new(() =>
    {
        var dir = Path.Combine(Paths.Data, "sounds");
        Directory.CreateDirectory(dir);
        var files = new Dictionary<string, string>
        {
            ["start"] = Tone(dir, "start", new[] { (660.0, 0.05), (880.0, 0.06) }, 0.18),
            ["stop"] = Tone(dir, "stop", new[] { (880.0, 0.05), (660.0, 0.06) }, 0.16),
            ["error"] = Tone(dir, "error", new[] { (330.0, 0.09), (262.0, 0.12) }, 0.2),
        };
        return files;
    });

    static string Tone(string dir, string name, (double Hz, double Sec)[] parts, double volume)
    {
        var path = Path.Combine(dir, name + ".wav");
        if (File.Exists(path)) return path;
        const int rate = 44100;
        var samples = new List<float>();
        foreach (var (hz, sec) in parts)
        {
            int n = (int)(rate * sec);
            for (int i = 0; i < n; i++)
            {
                double env = Math.Min(1, Math.Min(i / (rate * 0.006), (n - i) / (rate * 0.02)));
                samples.Add((float)(Math.Sin(2 * Math.PI * hz * i / rate) * volume * env));
            }
        }
        File.WriteAllBytes(path, AudioUtil.Wav(samples.ToArray(), rate));
        return path;
    }

    public static void Sound(string name)
    {
        var player = Which("pw-play") ?? Which("paplay") ?? Which("aplay");
        if (player == null) return;
        try
        {
            var psi = new ProcessStartInfo(player) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            if (player.EndsWith("aplay")) psi.ArgumentList.Add("-q");
            psi.ArgumentList.Add(SoundFiles.Value[name]);
            Process.Start(psi);
        }
        catch { }
    }
}

/// <summary>Mikrofon über parecord (PulseAudio/PipeWire), pw-record (PipeWire) oder arecord (ALSA) – roh, 16 kHz, mono, 16 Bit.</summary>
sealed class Recorder : IDisposable
{
    Process? proc;
    readonly List<byte> pcm = new();
    Thread? reader;
    public event Action<float>? Level;
    public DateTime Started { get; private set; }
    public bool IsRecording => proc != null;
    /// <summary>Fehlermeldung, wenn das Aufnahme-Werkzeug von selbst abgebrochen ist (sonst null).</summary>
    public string? LastError { get; private set; }

    static bool? pwRaw;
    static bool PwRecordKnowsRaw => pwRaw ??= Desktop.Run(Desktop.Which("pw-record")!, new[] { "--help" }) is var r && (r.Out + r.Err).Contains("--raw");

    static bool PipeWireRunning => Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } r && File.Exists(Path.Combine(r, "pipewire-0"));

    /// <summary>parecord, wenn da (läuft auch über PipeWire); sonst pw-record bei laufendem PipeWire, sonst arecord.
    /// FLUESTER_RECORDER=parecord|pw-record|arecord erzwingt ein Werkzeug.</summary>
    public static string? Tool
    {
        get
        {
            if (Environment.GetEnvironmentVariable("FLUESTER_RECORDER") is ("parecord" or "pw-record" or "arecord") and { } forced && Desktop.Which(forced) != null) return forced;
            return Desktop.Which("parecord") != null ? "parecord"
                : Desktop.Which("pw-record") != null && PipeWireRunning ? "pw-record"
                : Desktop.Which("arecord") != null ? "arecord"
                : Desktop.Which("pw-record") != null ? "pw-record" : null;
        }
    }

    /// <summary>Mikrofone, die das aktuelle Aufnahme-Werkzeug ansprechen kann (ohne Lautsprecher-Monitore).</summary>
    public static List<Microphone> Devices()
    {
        try
        {
            switch (Tool)
            {
                case "parecord" when Desktop.Which("pactl") is { } pactl:
                    var def = MicrophoneParser.PactlDefault(Desktop.Run(pactl, new[] { "info" }, englishOutput: true).Out);
                    return MicrophoneParser.Pactl(Desktop.Run(pactl, new[] { "list", "sources" }, englishOutput: true).Out, def);
                case "pw-record" when Desktop.Which("pw-dump") is { } dump:
                    return MicrophoneParser.PwDump(Desktop.Run(dump, Array.Empty<string>(), timeoutMs: 6000).Out);
                case "arecord":
                    return MicrophoneParser.Arecord(Desktop.Run(Desktop.Which("arecord")!, new[] { "-L" }, englishOutput: true).Out);
            }
        }
        catch (Exception e) { Log.Write("Mikrofone auflisten: " + e.Message); }
        return new();
    }

    /// <summary>Gewähltes Mikrofon, falls es gerade angeschlossen ist – sonst "" (Systemstandard). Dauert wenige Millisekunden.</summary>
    public static string Resolve(string deviceId, out bool missing)
    {
        missing = false;
        if (deviceId.Length == 0) return "";
        try
        {
            switch (Tool)
            {
                case "parecord" when Desktop.Which("pactl") is { } pactl:
                    var r = Desktop.Run(pactl, new[] { "list", "short", "sources" }, timeoutMs: 1500, englishOutput: true);
                    if (r.Code == 0) missing = !r.Out.Split('\n').Any(l => l.Split('\t') is { Length: > 1 } p && p[1] == deviceId);
                    break;
                case "arecord":
                    var a = Desktop.Run(Desktop.Which("arecord")!, new[] { "-L" }, timeoutMs: 1500, englishOutput: true);
                    if (a.Code == 0) missing = !a.Out.Split('\n').Any(l => l.TrimEnd() == deviceId);
                    break;
                case "pw-record" when Desktop.Which("pw-dump") is { } dump:
                    var d = Desktop.Run(dump, Array.Empty<string>(), timeoutMs: 1500);
                    if (d.Code == 0 && MicrophoneParser.PwDump(d.Out) is { Count: > 0 } nodes) missing = nodes.All(m => m.Id != deviceId);
                    break;
            }
        }
        catch (Exception e) { Log.Write("Mikrofon prüfen: " + e.Message); }
        return missing ? "" : deviceId;
    }

    /// <summary>Lesbarer Name des eingestellten Mikrofons, z. B. „Systemstandard (Yeti Stereo)“.</summary>
    public static string Describe(string deviceId, List<Microphone>? mics = null)
    {
        mics ??= Devices();
        if (deviceId.Length == 0)
            return mics.FirstOrDefault(m => m.IsDefault) is { } d ? $"Systemstandard ({d.Name})" : "Systemstandard";
        return mics.FirstOrDefault(m => m.Id == deviceId) is { } m ? m.Name + (m.Muted ? " (stummgeschaltet)" : "")
            : $"{deviceId} – nicht angeschlossen, solange gilt der Systemstandard";
    }

    public void Start(string deviceId)
    {
        Stop();
        LastError = null;
        var tool = Tool ?? throw new FluesterException("Kein Aufnahme-Werkzeug – pulseaudio-utils (parecord), pipewire-utils (pw-record) oder alsa-utils (arecord) installieren");
        var psi = new ProcessStartInfo(Desktop.Which(tool)!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        switch (tool)
        {
            case "parecord":
                foreach (var a in new[] { "--raw", "--rate=16000", "--channels=1", "--format=s16le", "--latency-msec=40" }) psi.ArgumentList.Add(a);
                if (deviceId.Length > 0) psi.ArgumentList.Add("--device=" + deviceId);
                break;
            case "arecord":
                foreach (var a in new[] { "-q", "-f", "S16_LE", "-r", "16000", "-c", "1", "-t", "raw" }) psi.ArgumentList.Add(a);
                if (deviceId.Length > 0) { psi.ArgumentList.Add("-D"); psi.ArgumentList.Add(deviceId); }
                break;
            default:
                // --raw gibt es erst ab PipeWire 1.2; ältere Versionen schreiben auf „-“ ohnehin rohes PCM
                if (PwRecordKnowsRaw) psi.ArgumentList.Add("--raw");
                foreach (var a in new[] { "--rate", "16000", "--channels", "1", "--format", "s16" }) psi.ArgumentList.Add(a);
                if (deviceId.Length > 0) { psi.ArgumentList.Add("--target"); psi.ArgumentList.Add(deviceId); }
                psi.ArgumentList.Add("-");
                break;
        }
        lock (pcm) pcm.Clear();
        proc = Process.Start(psi) ?? throw new FluesterException("Aufnahme startet nicht");
        Started = DateTime.UtcNow;
        var stream = proc.StandardOutput.BaseStream;
        reader = new Thread(() =>
        {
            var buf = new byte[3200];
            List<byte>? head = new();
            void Take(ReadOnlySpan<byte> chunk)
            {
                lock (pcm) if (pcm.Count < 16000 * 2 * 900) pcm.AddRange(chunk.ToArray());
                Level?.Invoke(AudioUtil.Level(AudioUtil.FromPcm16(chunk[..(chunk.Length - chunk.Length % 2)])));
            }
            try
            {
                int n;
                while ((n = stream.Read(buf, 0, buf.Length)) > 0)
                {
                    if (head == null) { Take(buf.AsSpan(0, n)); continue; }
                    // Anfang sammeln, bis klar ist, ob ein WAV-Kopf davor steht
                    head.AddRange(buf.AsSpan(0, n).ToArray());
                    if (head.Count < 256) continue;
                    var first = head.ToArray();
                    head = null;
                    Take(first.AsSpan(AudioUtil.PcmStart(first)));
                }
                if (head is { Count: > 0 }) { var first = head.ToArray(); Take(first.AsSpan(AudioUtil.PcmStart(first))); }
            }
            catch { }
        }) { IsBackground = true, Name = "Aufnahme" };
        reader.Start();
    }

    public float[] Stop()
    {
        if (proc != null)
        {
            bool died = proc.HasExited && proc.ExitCode != 0;
            try { proc.Kill(); } catch { }
            reader?.Join(1000);
            var errTask = proc.StandardError.ReadToEndAsync();
            var err = errTask.Wait(1000) ? errTask.Result.Trim() : "";
            if (err.Length > 0) Log.Write("Aufnahme: " + err);
            if (died) LastError = err.Length > 0 ? err.Split('\n')[0] : $"Aufnahme-Werkzeug beendet (Code {proc.ExitCode})";
            proc.Dispose();
            proc = null;
        }
        byte[] bytes;
        lock (pcm) { bytes = pcm.ToArray(); pcm.Clear(); }
        return AudioUtil.FromPcm16(bytes.AsSpan(0, bytes.Length - bytes.Length % 2));
    }

    public void Dispose() => Stop();
}
