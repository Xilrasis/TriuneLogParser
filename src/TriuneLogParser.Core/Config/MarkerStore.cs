using System.Text.Json;
using TriuneLogParser.Core.Encounters;
using TriuneLogParser.Core.Logging;

namespace TriuneLogParser.Core.Config;

/// <summary>
/// One character's saved split markers, stored at
/// <c>%AppData%/TriuneLogParser/markers/&lt;identity&gt;.json</c>. The identity is
/// <c>&lt;Character&gt;_&lt;server&gt;</c> (see <see cref="LogFileTailer.LogIdentity"/>),
/// so the same markers apply whether you re-parse the live log or an archived slice of it.
/// </summary>
public sealed class MarkerFile
{
    public string? Identity { get; set; }
    public List<EncounterMarker> Markers { get; set; } = new();
}

/// <summary>Load / append / remove split markers for a log, keyed by character identity.</summary>
public static class MarkerStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Where marker files live. Defaults to <c>%AppData%/TriuneLogParser/markers</c>;
    /// override before use (tests, portable mode).
    /// </summary>
    public static string DirectoryPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TriuneLogParser", "markers");

    public static string PathForIdentity(string identity) =>
        Path.Combine(DirectoryPath, Sanitize(identity) + ".json");

    public static string PathForLog(string logPath) =>
        PathForIdentity(LogFileTailer.LogIdentity(logPath));

    /// <summary>All split markers for the character whose log this is (empty if none / unreadable).</summary>
    public static IReadOnlyList<EncounterMarker> LoadForLog(string logPath) =>
        Load(PathForLog(logPath)).Markers;

    public static MarkerFile Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<MarkerFile>(File.ReadAllText(path)) ?? new MarkerFile();
        }
        catch (Exception)
        {
            // A corrupt marker file must not stop parsing.
        }

        return new MarkerFile();
    }

    /// <summary>Add a split marker for this log's character. De-duplicates by timestamp.</summary>
    public static void AddSplit(string logPath, DateTime at)
    {
        string identity = LogFileTailer.LogIdentity(logPath);
        string path = PathForIdentity(identity);
        MarkerFile file = Load(path);
        file.Identity = identity;

        if (file.Markers.Any(m => m.Kind == MarkerKind.Split && m.Timestamp == at))
            return;

        file.Markers.Add(new EncounterMarker(at, MarkerKind.Split));
        file.Markers.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        Save(path, file);
    }

    /// <summary>Remove the split marker at (or closest within a second to) <paramref name="at"/>.</summary>
    public static bool RemoveSplit(string logPath, DateTime at)
    {
        string path = PathForLog(logPath);
        MarkerFile file = Load(path);

        EncounterMarker? hit = file.Markers
            .Where(m => m.Kind == MarkerKind.Split && Math.Abs((m.Timestamp - at).TotalSeconds) < 1)
            .OrderBy(m => Math.Abs((m.Timestamp - at).TotalSeconds))
            .FirstOrDefault();

        if (hit is null || !file.Markers.Remove(hit))
            return false;

        Save(path, file);
        return true;
    }

    private static void Save(string path, MarkerFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(file, JsonOptions));
    }

    private static string Sanitize(string identity)
    {
        char[] bad = Path.GetInvalidFileNameChars();
        return new string(identity.Select(c => Array.IndexOf(bad, c) >= 0 ? '_' : c).ToArray());
    }
}
