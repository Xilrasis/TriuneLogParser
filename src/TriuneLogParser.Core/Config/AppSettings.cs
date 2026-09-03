using System.Text.Json;
using System.Text.Json.Serialization;

namespace TriuneLogParser.Core.Config;

/// <summary>
/// Persisted user settings. Stored as JSON at
/// <c>%AppData%/TriuneLogParser/settings.json</c>. The parser core only needs the EQ
/// path and encounter tuning; the WPF app layers overlay preferences on top.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Root EverQuest install folder (contains a <c>logs</c> sub-directory).</summary>
    public string? EverQuestFolder { get; set; }

    /// <summary>Character log file names the user has chosen to follow.</summary>
    public List<string> FollowedLogs { get; set; } = new();

    /// <summary>Idle seconds before an open fight is closed.</summary>
    public int IdleTimeoutSeconds { get; set; } = 45;

    /// <summary>When following several characters, merge their output into one parse.</summary>
    public bool MergeFollowedCharacters { get; set; }

    /// <summary>Free-form bag for UI-layer settings (overlay position, opacity, ...).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = new();

    [JsonIgnore]
    public string? LogsFolder =>
        string.IsNullOrWhiteSpace(EverQuestFolder) ? null : Path.Combine(EverQuestFolder, "logs");

    public bool LooksValid() =>
        LogsFolder is { } l && Directory.Exists(l);

    // ---- persistence -----------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TriuneLogParser", "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            // Corrupt settings shouldn't stop the app starting.
        }

        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>Best-effort discovery of an EverQuest folder from common locations.</summary>
    public static IEnumerable<string> GuessEverQuestFolders()
    {
        string[] roots =
        {
            @"C:\Program Files\Sony\EverQuest",
            @"C:\Program Files (x86)\Sony\EverQuest",
            @"C:\EverQuest",
            @"C:\Program Files\EverQuest",
            @"C:\Program Files (x86)\EverQuest",
            @"C:\Program Files (x86)\Project Triune",
            @"C:\Project Triune",
        };

        foreach (string r in roots)
        {
            if (Directory.Exists(Path.Combine(r, "logs")))
                yield return r;
        }
    }
}
