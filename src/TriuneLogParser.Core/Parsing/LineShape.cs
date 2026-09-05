namespace TriuneLogParser.Core.Parsing;

/// <summary>Collapses the variable parts of a log line so near-identical lines group together.</summary>
public static class LineShape
{
    /// <summary>Replace every run of digits with a single <c>#</c> — "hits for 1234" and
    /// "hits for 57" become the same shape.</summary>
    public static string Normalize(string message)
    {
        Span<char> buf = message.Length <= 512 ? stackalloc char[message.Length] : new char[message.Length];
        int n = 0;
        bool prevHash = false;
        foreach (char c in message)
        {
            if (char.IsDigit(c))
            {
                if (!prevHash)
                    buf[n++] = '#';
                prevHash = true;
            }
            else
            {
                buf[n++] = c;
                prevHash = false;
            }
        }

        return new string(buf[..n]);
    }
}
