using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickTranslate.Core.Settings;

/// <summary>Loads and saves settings as JSON under %AppData%\QuickTranslate (per user, nothing shared).</summary>
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
        DataDirectory = dataDirectory ?? Environment.GetEnvironmentVariable("QUICKTRANSLATE_DATA") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickTranslate");
        Directory.CreateDirectory(DataDirectory);
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
