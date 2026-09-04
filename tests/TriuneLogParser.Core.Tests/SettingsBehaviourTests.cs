using TriuneLogParser.Core;
using TriuneLogParser.Core.Encounters;
using TriuneLogParser.Core.Logging;
using TriuneLogParser.Core.Model;
using Xunit;

namespace TriuneLogParser.Core.Tests;

public class SettingsBehaviourTests
{
    private static string L(string hhmmss, string msg) => $"[Thu Sep 03 {hhmmss} 2026] {msg}";

    private static IReadOnlyList<Encounter> Build(IEnumerable<string> lines, EncounterOptions opt)
    {
        var p = new CombatLogProcessor("Xilaria", opt);
        int n = 0;
        foreach (string l in lines)
            p.AddLine(l, ++n);
        return p.BuildBatch();
    }

    [Fact]
    public void Rest_period_zero_splits_every_pull()
    {
        string[] lines =
        {
            L("12:00:00", "You crush a doomfire soldier for 100 points of damage."),
            L("12:00:02", "You have slain a doomfire soldier!"),
            L("12:00:06", "You crush a doomfire guardian for 100 points of damage."),
            L("12:00:08", "You have slain a doomfire guardian!"),
        };

        Assert.Equal(2, Build(lines, EncounterOptions.ForRestPeriod(0)).Count);
        Assert.Single(Build(lines, EncounterOptions.ForRestPeriod(60))); // 6s gap < 60s → merged
    }

    [Fact]
    public void For_rest_period_maps_windows()
    {
        EncounterOptions z = EncounterOptions.ForRestPeriod(0);
        Assert.True(z.SplitOnAllMobsDead);
        Assert.Equal(TimeSpan.Zero, z.ReengageWindow);

        EncounterOptions two = EncounterOptions.ForRestPeriod(120);
        Assert.False(two.SplitOnAllMobsDead);
        Assert.Equal(TimeSpan.FromSeconds(120), two.IdleTimeout);

        Assert.Equal(TimeSpan.FromSeconds(300), EncounterOptions.ForRestPeriod(9999).IdleTimeout);
    }

    [Fact]
    public void Corpse_run_does_not_fragment_a_raid_encounter()
    {
        string[] lines =
        {
            L("21:00:00", "Xilaria hit a collapsing star for 5000 points of non-melee damage. (Distant Strike)"),
            L("21:00:05", "A collapsing star hits YOU for 9000 points of damage."),
            L("21:00:06", "You have been slain by a collapsing star!"),
            L("21:00:10", "You have entered The Bazaar."),
            L("21:00:30", "You have entered The Plane of Time."),
            L("21:00:45", "Xilaria hit a collapsing star for 5000 points of non-melee damage. (Distant Strike)"),
            L("21:01:20", "You have slain a collapsing star!"),
        };

        // 15 s rest would normally split on the 39 s gap and the zone changes; the
        // death-recovery bridge keeps it as one fight.
        Assert.Single(Build(lines, EncounterOptions.ForRestPeriod(15)));
    }

    [Fact]
    public void Log_archiver_renames_with_timestamp()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tlp-arch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string log = Path.Combine(dir, "eqlog_Test_multiclass.txt");
            File.WriteAllText(log, new string('x', 2048));

            LogArchiver.Result r = LogArchiver.TrySplit(log, maxBytes: 1024);
            Assert.True(r.Split);
            Assert.False(File.Exists(log));
            Assert.NotNull(r.ArchivePath);
            Assert.Matches(@"eqlog_Test_multiclass\.\d{8}-\d{6}\.txt$", r.ArchivePath!);
            Assert.True(File.Exists(r.ArchivePath));

            // below threshold → no-op
            File.WriteAllText(log, "small");
            Assert.False(LogArchiver.TrySplit(log, maxBytes: 1024).Split);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
