using System.IO;
using TriuneLogParser.Core.Logging;

namespace TriuneLogParser.App.Services;

public sealed record LogFileInfo(string Path, string Character, string? Server, DateTime LastWritten, long Bytes)
{
    public string Display => Server is { Length: > 0 } ? $"{Character}  ·  {Server}" : Character;
}

public static class LogDiscovery
{
    /// <summary>Enumerate <c>eqlog_*.txt</c> files in an EverQuest <c>logs</c> folder, newest first.</summary>
    public static IReadOnlyList<LogFileInfo> Find(string? everQuestFolder)
    {
        if (string.IsNullOrWhiteSpace(everQuestFolder))
            return Array.Empty<LogFileInfo>();

        string logs = Path.Combine(everQuestFolder, "logs");
        if (!Directory.Exists(logs))
            return Array.Empty<LogFileInfo>();

        var result = new List<LogFileInfo>();
        foreach (string path in Directory.EnumerateFiles(logs, "eqlog_*.txt"))
        {
            string fileName = Path.GetFileNameWithoutExtension(path);
            if (fileName.Contains("backup", StringComparison.OrdinalIgnoreCase))
                continue;

            string? character = LogFileTailer.TryExtractCharacterName(path);
            if (character is null)
                continue;

            var fi = new FileInfo(path);
            result.Add(new LogFileInfo(
                path, character, LogFileTailer.TryExtractServerName(path), fi.LastWriteTime, fi.Length));
        }

        result.Sort((a, b) => b.LastWritten.CompareTo(a.LastWritten));
        return result;
    }
}
