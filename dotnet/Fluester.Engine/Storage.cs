using System.Text.Json;
using Fluester.Core;

namespace Fluester.Engine;

/// <summary>Wo Flüster seine Dateien ablegt.
/// Windows: %APPDATA%\Fluester (Einstellungen, Verlauf) und %LOCALAPPDATA%\Fluester\models.
/// Linux: ~/.config/fluester und ~/.local/share/fluester/models (XDG). FLUESTER_HOME überschreibt beides (Tests).</summary>
public static class Paths
{
    static string Ensure(string dir) { Directory.CreateDirectory(dir); return dir; }

    static string? Override => Environment.GetEnvironmentVariable("FLUESTER_HOME") is { Length: > 0 } h ? h : null;

    public static string Config => Ensure(Override ?? (OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Fluester")
        : Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "fluester")));

    public static string Data => Ensure(Override ?? (OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fluester")
        : Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "fluester")));

    public static string Models => Ensure(Path.Combine(Data, "models"));
    public static string SettingsFile => Path.Combine(Config, "settings.json");
    public static string HistoryFile => Path.Combine(Data, "history.json");
    public static string LogFile => Path.Combine(Data, "fluester.log");
}

/// <summary>Einfaches Protokoll (für Fehlersuche, max. ~1 MB).</summary>
public static class Log
{
    static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var f = Paths.LogFile;
                if (File.Exists(f) && new FileInfo(f).Length > 1_000_000) File.Delete(f);
                File.AppendAllText(f, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch { /* Protokoll darf nie stören */ }
    }
}

/// <summary>Einstellungen laden/speichern. Schreibt atomar (erst Temp-Datei, dann umbenennen).</summary>
public sealed class SettingsStore
{
    readonly string file;
    readonly object gate = new();
    Settings current;

    public event Action<Settings>? Changed;

    public SettingsStore(string? file = null)
    {
        this.file = file ?? Paths.SettingsFile;
        current = File.Exists(this.file) ? Settings.FromJson(File.ReadAllText(this.file)) : new Settings();
    }

    /// <summary>Datei neu einlesen (z. B. nach Bearbeitung von Hand).</summary>
    public void Reload()
    {
        Settings snapshot;
        lock (gate)
        {
            current = File.Exists(file) ? Settings.FromJson(File.ReadAllText(file)) : new Settings();
            snapshot = current.Clone();
        }
        Changed?.Invoke(snapshot);
    }

    public string FilePath => file;

    /// <summary>Eine Kopie der aktuellen Einstellungen (Änderungen über <see cref="Update"/>).</summary>
    public Settings Current { get { lock (gate) return current.Clone(); } }

    public void Update(Action<Settings> change)
    {
        Settings snapshot;
        lock (gate)
        {
            var copy = current.Clone();
            change(copy);
            current = copy;
            snapshot = copy.Clone();
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, current.ToJson());
            File.Move(tmp, file, overwrite: true);
        }
        Changed?.Invoke(snapshot);
    }
}

/// <summary>Verlauf der Diktate (lokal, max. 2000 Einträge).</summary>
public sealed class HistoryStore
{
    readonly string file;
    readonly object gate = new();
    List<HistoryItem> items;
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public event Action? Changed;

    public HistoryStore(string? file = null)
    {
        this.file = file ?? Paths.HistoryFile;
        try
        {
            items = File.Exists(this.file) ? JsonSerializer.Deserialize<List<HistoryItem>>(File.ReadAllText(this.file), Json) ?? new() : new();
        }
        catch (JsonException)
        {
            items = new();
        }
    }

    /// <summary>Neueste zuerst.</summary>
    public IReadOnlyList<HistoryItem> Items { get { lock (gate) return items.ToList(); } }

    public HistoryItem? Last { get { lock (gate) return items.FirstOrDefault(i => !i.IsCommand) ?? items.FirstOrDefault(); } }

    public StatsSummary Summary => Stats.Summary(Items);

    public void Add(HistoryItem item)
    {
        lock (gate)
        {
            items.Insert(0, item);
            if (items.Count > 2000) items.RemoveRange(2000, items.Count - 2000);
            Save();
        }
        Changed?.Invoke();
    }

    public void Remove(Guid id)
    {
        lock (gate) { items.RemoveAll(i => i.Id == id); Save(); }
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (gate) { items.Clear(); Save(); }
        Changed?.Invoke();
    }

    void Save()
    {
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(items, Json));
        File.Move(tmp, file, overwrite: true);
    }
}

/// <summary>API-Schlüssel je Anbieter. Windows: DPAPI-verschlüsselt; Linux: Datei nur für den Nutzer lesbar.
/// Umgebungsvariablen (FLUESTER_KEY_GROQ, FLUESTER_KEY_OPENAI …) haben Vorrang.</summary>
public interface ISecretStore
{
    string? Get(string providerId);
    void Set(string providerId, string? key);
}

public static class Secrets
{
    public static string? FromEnvironment(string providerId) =>
        Environment.GetEnvironmentVariable("FLUESTER_KEY_" + providerId.ToUpperInvariant()) is { Length: > 0 } k ? k
        : providerId == "groq" ? NonEmpty(Environment.GetEnvironmentVariable("GROQ_API_KEY"))
        : providerId == "openai" ? NonEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
        : null;

    static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

/// <summary>Schlüssel als JSON-Datei mit Rechten 600 (Linux) – Basis, die Windows mit DPAPI überschreibt.</summary>
public class FileSecretStore : ISecretStore
{
    protected readonly string File;
    readonly object gate = new();

    public FileSecretStore(string? file = null) => File = file ?? Path.Combine(Paths.Config, "keys.json");

    protected virtual byte[] Protect(byte[] data) => data;
    protected virtual byte[] Unprotect(byte[] data) => data;

    Dictionary<string, string> Load()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return new();
            var raw = Unprotect(System.IO.File.ReadAllBytes(File));
            return JsonSerializer.Deserialize<Dictionary<string, string>>(raw) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public string? Get(string providerId)
    {
        var env = Secrets.FromEnvironment(providerId);
        if (env != null) return env;
        lock (gate) return Load().TryGetValue(providerId, out var k) && k.Length > 0 ? k : null;
    }

    public void Set(string providerId, string? key)
    {
        lock (gate)
        {
            var all = Load();
            if (string.IsNullOrWhiteSpace(key)) all.Remove(providerId); else all[providerId] = key.Trim();
            var bytes = Protect(JsonSerializer.SerializeToUtf8Bytes(all));
            var tmp = File + ".tmp";
            System.IO.File.WriteAllBytes(tmp, bytes);
            if (!OperatingSystem.IsWindows()) System.IO.File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            System.IO.File.Move(tmp, File, overwrite: true);
        }
    }
}
