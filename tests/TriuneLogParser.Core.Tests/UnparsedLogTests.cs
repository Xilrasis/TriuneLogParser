using TriuneLogParser.Core.Config;
using TriuneLogParser.Core.Logging;
using TriuneLogParser.Core.Parsing;
using Xunit;

namespace TriuneLogParser.Core.Tests;

public class UnparsedLogTests
{
    [Fact]
    public void Default_rest_period_is_per_pull()
    {
        Assert.Equal(0, new AppSettings().RestPeriodSeconds);
    }

    [Theory]
    [InlineData("You hit a rat for 1234 points of damage.", "You hit a rat for # points of damage.")]
    [InlineData("A B C 12 34-56", "A B C # #-#")]
    public void Line_shape_collapses_digit_runs(string input, string expected)
    {
        Assert.Equal(expected, LineShape.Normalize(input));
    }

    [Fact]
    public void Unparsed_writer_dedupes_by_shape_and_keeps_the_raw_line()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tlp-unparsed-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "nested", "unparsed.log");
        try
        {
            var w = new UnparsedLogWriter(path);
            w.Append("[Thu Sep 03 12:00:00 2026] Frobnicator zorches a rat for 5 quatloos.");
            w.Append("[Thu Sep 03 12:00:05 2026] Frobnicator zorches a rat for 4210 quatloos."); // same shape
            w.Append("[Thu Sep 03 12:00:09 2026] Frobnicator zorches a cat for 9 quatloos.");    // different shape

            Assert.Equal(2, w.DistinctCount);

            string[] lines = File.ReadAllLines(path);
            Assert.Contains(lines, l => l.EndsWith("zorches a rat for 5 quatloos."));
            Assert.Contains(lines, l => l.EndsWith("zorches a cat for 9 quatloos."));
            Assert.DoesNotContain(lines, l => l.Contains("4210"));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Unparsed_writer_swallows_io_errors()
    {
        // A path whose directory can't be created (a file stands where a dir would go).
        string file = Path.Combine(Path.GetTempPath(), "tlp-notadir-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "x");
        try
        {
            var w = new UnparsedLogWriter(Path.Combine(file, "unparsed.log"));
            w.Append("[Thu Sep 03 12:00:00 2026] whatever for 1 point.");
            Assert.Equal(1, w.DistinctCount); // recorded in memory, write failed silently
        }
        finally
        {
            File.Delete(file);
        }
    }
}
