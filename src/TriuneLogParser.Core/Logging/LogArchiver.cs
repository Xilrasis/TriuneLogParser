namespace TriuneLogParser.Core.Logging;

/// <summary>
/// Optional "prune the log" helper: once the active log file passes a size limit,
/// rename it aside with a timestamp suffix so the game starts a fresh one.
/// </summary>
public static class LogArchiver
{
    public sealed record Result(bool Split, string? ArchivePath, string? Error);

    /// <summary>
    /// Archive <paramref name="logPath"/> if it is at least <paramref name="maxBytes"/>.
    /// The rename can fail if the game holds the file without delete-sharing — that's
    /// reported, not thrown.
    /// </summary>
    public static Result TrySplit(string logPath, long maxBytes)
    {
        try
        {
            var fi = new FileInfo(logPath);
            if (!fi.Exists || fi.Length < maxBytes)
                return new Result(false, null, null);

            string dir = fi.DirectoryName ?? ".";
            string stem = Path.GetFileNameWithoutExtension(fi.Name);
            string ext = fi.Extension;
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string archive = Path.Combine(dir, $"{stem}.{stamp}{ext}");

            // Avoid clobbering if two splits land in the same second.
            int n = 1;
            while (File.Exists(archive))
                archive = Path.Combine(dir, $"{stem}.{stamp}-{n++}{ext}");

            File.Move(logPath, archive);
            return new Result(true, archive, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result(false, null, ex.Message);
        }
    }
}
