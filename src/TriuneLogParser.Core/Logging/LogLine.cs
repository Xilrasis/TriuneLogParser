using System.Globalization;
using System.Text.RegularExpressions;

namespace TriuneLogParser.Core.Logging;

/// <summary>
/// A single raw log line split into its timestamp and message. EverQuest RoF2 lines
/// look like: <c>[Thu Sep 03 12:04:51 2026] You kick a flame lordling for 4356 points of damage.</c>
/// </summary>
public readonly record struct LogLine(DateTime Timestamp, string Message, int LineNumber, string Raw)
{
    private static readonly Regex Pattern = new(
        @"^\[(?<ts>[A-Z][a-z]{2} [A-Z][a-z]{2} \d{2} \d{2}:\d{2}:\d{2} \d{4})\] (?<msg>.*)$",
        RegexOptions.Compiled);

    private const string TimeFormat = "ddd MMM dd HH:mm:ss yyyy";

    public static bool TryParse(string raw, int lineNumber, out LogLine line)
    {
        line = default;
        if (string.IsNullOrEmpty(raw))
            return false;

        Match m = Pattern.Match(raw);
        if (!m.Success)
            return false;

        if (!DateTime.TryParseExact(
                m.Groups["ts"].Value,
                TimeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out DateTime ts))
        {
            return false;
        }

        line = new LogLine(ts, m.Groups["msg"].Value, lineNumber, raw);
        return true;
    }
}
