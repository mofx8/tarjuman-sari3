using System.Text.Json;
using System.Text.Json.Serialization;

namespace TarjumanSari3.Core.Settings;

/// <summary>Loads and saves settings as JSON under %AppData%\TarjumanSari3 (per user, nothing shared).</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    public string DataDirectory { get; }
    public string FilePath => Path.Combine(DataDirectory, "settings.json");
    public AppSettings Current { get; private set; } = new();

    public event Action<AppSettings>? Changed;

    public SettingsStore(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Environment.GetEnvironmentVariable("TARJUMANSARI3_DATA") ?? DefaultDirectory();
        Directory.CreateDirectory(DataDirectory);
    }

    /// <summary>%AppData%\TarjumanSari3; data from the app's former folder name is carried over once.</summary>
    private static string DefaultDirectory() => ResolveDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public static string ResolveDirectory(string appData)
    {
        var path = Path.Combine(appData, "TarjumanSari3");
        var legacy = Path.Combine(appData, "QuickTranslate");
        if (!Directory.Exists(path) && Directory.Exists(legacy))
        {
            try { Directory.Move(legacy, path); }
            catch { try { CopyDirectory(legacy, path); } catch { /* start with a fresh folder */ } }
        }
        return path;
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(from)) CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    public AppSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(FilePath))
                    Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new();
            }
            catch
            {
                // Corrupt file: keep a backup and start from defaults rather than crash.
                try { File.Move(FilePath, FilePath + ".bad", true); } catch { }
                Current = new();
            }
            EnsureAllHotkeys(Current);
            return Current;
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
            File.Move(tmp, FilePath, true);
        }
        Changed?.Invoke(Current);
    }

    private static void EnsureAllHotkeys(AppSettings s)
    {
        foreach (var (action, def) in AppSettings.DefaultHotkeys())
            if (!s.Hotkeys.ContainsKey(action)) s.Hotkeys[action] = def;
    }
}
