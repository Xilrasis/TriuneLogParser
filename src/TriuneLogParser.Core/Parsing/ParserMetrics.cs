namespace TriuneLogParser.Core.Parsing;

/// <summary>Counters describing how well the grammar covered a log.</summary>
public sealed class ParserMetrics
{
    public long TotalLines { get; set; }
    public long TimestampedLines { get; set; }
    public long EventsParsed { get; set; }
    public long CritMarkers { get; set; }
    public long ZoneChanges { get; set; }

    /// <summary>Lines that looked damage-related but matched no rule.</summary>
    public long UnparsedDamageLike { get; set; }

    private readonly Dictionary<string, int> _unparsedSamples = new();

    /// <summary>Up to a handful of distinct examples of unparsed damage-like lines (normalised).</summary>
    public IReadOnlyDictionary<string, int> UnparsedSamples => _unparsedSamples;

    public void NoteUnparsed(string message)
    {
        UnparsedDamageLike++;
        string key = Normalize(message);
        if (_unparsedSamples.Count < 200 || _unparsedSamples.ContainsKey(key))
            _unparsedSamples[key] = _unparsedSamples.GetValueOrDefault(key) + 1;
    }

    public double Coverage => TimestampedLines > 0
        ? 1.0 - (double)UnparsedDamageLike / TimestampedLines
        : 1.0;

    private static string Normalize(string message)
    {
        Span<char> buf = message.Length <= 512 ? stackalloc char[message.Length] : new char[message.Length];
        int n = 0;
        foreach (char c in message)
            buf[n++] = char.IsDigit(c) ? '#' : c;
        return new string(buf[..n]).Replace("##", "#").Replace("##", "#");
    }
}
