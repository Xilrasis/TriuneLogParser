using TriuneLogParser.Core.Parsing;

namespace TriuneLogParser.Core.Logging;

/// <summary>
/// Appends damage-shaped log lines the grammar couldn't parse to a local file, so
/// missing rules can be found and fixed. De-duplicates by line shape (digits collapsed)
/// — one real example of each distinct miss, not thousands of identical ticks.
/// Every write is best-effort: an IO failure is swallowed, never thrown at the parser.
/// </summary>
public sealed class UnparsedLogWriter
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly HashSet<string> _seenShapes = new(StringComparer.Ordinal);
    private bool _headerWritten;

    public UnparsedLogWriter(string path) => _path = path;

    public string Path => _path;

    /// <summary>Number of distinct shapes recorded this session.</summary>
    public int DistinctCount
    {
        get { lock (_gate) return _seenShapes.Count; }
    }

    /// <summary>Record one unparsed raw log line. Ignored if its shape was already seen.</summary>
    public void Append(string rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine))
            return;

        string shape = LineShape.Normalize(MessageOf(rawLine));

        lock (_gate)
        {
            if (!_seenShapes.Add(shape))
                return;

            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                if (!_headerWritten)
                {
                    File.AppendAllText(_path,
                        $"{Environment.NewLine}# --- session {DateTime.Now:yyyy-MM-dd HH:mm:ss} — unparsed damage-like lines ---{Environment.NewLine}");
                    _headerWritten = true;
                }

                File.AppendAllText(_path, rawLine + Environment.NewLine);
            }
            catch (Exception)
            {
                // Disk full, permissions, file locked — logging misses must never break parsing.
            }
        }
    }

    private static string MessageOf(string rawLine)
    {
        int i = rawLine.IndexOf("] ", StringComparison.Ordinal);
        return i >= 0 && i + 2 < rawLine.Length ? rawLine[(i + 2)..] : rawLine;
    }
}
