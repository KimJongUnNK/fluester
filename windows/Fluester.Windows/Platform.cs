using System.Media;
using System.Security.Cryptography;
using Fluester.Core;
using Fluester.Engine;
using Microsoft.Win32;

namespace Fluester.Windows;

/// <summary>API-Schlüssel mit DPAPI verschlüsselt – nur dieses Windows-Konto kann sie lesen.</summary>
sealed class WinSecretStore : FileSecretStore
{
    static readonly byte[] Entropy = "Fluester.v1"u8.ToArray();

    public WinSecretStore() : base(Path.Combine(Paths.Config, "keys.dat")) { }

    protected override byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
    protected override byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}

/// <summary>Mit Windows starten (HKCU\…\Run).</summary>
static class Autostart
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "Fluester";

    public static bool IsEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue(Name) is string;
        }
    }

    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key);
        if (on) k.SetValue(Name, $"\"{Environment.ProcessPath}\" --background");
        else k.DeleteValue(Name, false);
    }
}

/// <summary>Kurze, leise Töne für Start/Ende/Fehler (selbst erzeugt, keine Systemklänge).</summary>
static class Sounds
{
    static readonly byte[] StartWav = Tone(new[] { (660.0, 0.05), (880.0, 0.06) }, 0.16);
    static readonly byte[] StopWav = Tone(new[] { (880.0, 0.05), (660.0, 0.06) }, 0.14);
    static readonly byte[] ErrorWav = Tone(new[] { (330.0, 0.09), (262.0, 0.12) }, 0.18);

    public static bool Enabled { get; set; } = true;

    public static void Start() => Play(StartWav);
    public static void Stop() => Play(StopWav);
    public static void Error() => Play(ErrorWav);

    static void Play(byte[] wav)
    {
        if (!Enabled) return;
        try
        {
            var p = new SoundPlayer(new MemoryStream(wav));
            p.Play();
        }
        catch { /* kein Audiogerät – egal */ }
    }

    static byte[] Tone((double Hz, double Sec)[] parts, double volume)
    {
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
        return AudioUtil.Wav(samples.ToArray(), rate);
    }
}
