using TriuneLogParser.Core;
using TriuneLogParser.Core.Config;
using TriuneLogParser.Core.Encounters;
using TriuneLogParser.Core.Logging;
using TriuneLogParser.Core.Model;
using TriuneLogParser.Core.Parsing;
using Xunit;

namespace TriuneLogParser.Core.Tests;

public class ForceSplitTests
{
    private static string L(string hhmmss, string msg) => $"[Thu Sep 03 {hhmmss} 2026] {msg}";

    private static IReadOnlyList<Encounter> Build(
        IEnumerable<string> lines, IEnumerable<EncounterMarker>? markers = null, int rest = 60)
    {
        var p = new CombatLogProcessor("Xilaria", EncounterOptions.ForRestPeriod(rest), markers: markers);
        int n = 0;
        foreach (string l in lines)
            p.AddLine(l, ++n);
        return p.BuildBatch();
    }

    [Fact]
    public void A_split_marker_cuts_one_fight_in_two()
    {
        string[] lines =
        {
            L("12:00:00", "You crush a doomfire soldier for 100 points of damage."),
            L("12:00:05", "You crush a doomfire soldier for 100 points of damage."),
            L("12:00:10", "You crush a doomfire soldier for 100 points of damage."),
            L("12:00:12", "You have slain a doomfire soldier!"),
        };

        // No marker: one continuous fight.
        Assert.Single(Build(lines));

        // Marker at 12:00:08 → events before it in #1, from it on in #2.
        var split = Build(lines, new[] { new EncounterMarker(new DateTime(2026, 9, 3, 12, 0, 8)) });
        Assert.Equal(2, split.Count);
        Assert.Equal(EncounterEndReason.Manual, split[0].EndReason);
        Assert.Contains("a doomfire soldier", split[1].NpcsKilled);
    }

    [Fact]
    public void Marker_between_pulls_forces_a_boundary_inside_the_reengage_window()
    {
        string[] lines =
        {
            L("12:00:00", "You crush a doomfire soldier for 100 points of damage."),
            L("12:00:02", "You have slain a doomfire soldier!"),
            // 3s later — well inside the 60s re-engage window, normally merged.
            L("12:00:05", "You crush a doomfire guardian for 100 points of damage."),
            L("12:00:07", "You have slain a doomfire guardian!"),
        };

        Assert.Single(Build(lines)); // merged without a marker

        var split = Build(lines, new[] { new EncounterMarker(new DateTime(2026, 9, 3, 12, 0, 3)) });
        Assert.Equal(2, split.Count);
    }

    [Fact]
    public void ForceSplit_between_pulls_bars_the_reengage_that_would_otherwise_merge()
    {
        var roster = new RosterTracker(new PetRegistry(), "Xilaria");
        // per-pull close, but a 60s window that would normally re-open the last kill.
        var b = new EncounterBuilder(roster, new EncounterOptions
        {
            SplitOnAllMobsDead = true,
            ReengageWindow = TimeSpan.FromSeconds(60),
            IdleTimeout = TimeSpan.FromSeconds(60),
        });

        void Feed(CombatEvent e) { roster.Observe(e); b.Advance(e.Timestamp); b.Handle(e); }
        CombatEvent Ev(int sec, CombatAction a, string target, long amt = 0) => new()
        {
            Timestamp = new DateTime(2026, 9, 3, 12, 0, sec), Action = a,
            Attacker = "Xilaria", Target = target, Amount = amt,
        };

        Feed(Ev(0, CombatAction.Damage, "a doomfire soldier", 100));
        Feed(Ev(2, CombatAction.Death, "a doomfire soldier"));   // fight 1 closes (kill)

        b.ForceSplit(new DateTime(2026, 9, 3, 12, 0, 3));        // no fight active — arm the barrier

        Feed(Ev(5, CombatAction.Damage, "a doomfire guardian", 100)); // 5s later — inside the window
        Feed(Ev(7, CombatAction.Death, "a doomfire guardian"));
        b.Finish();

        Assert.Equal(2, b.Completed.Count); // barred from re-opening fight 1
    }

    [Fact]
    public void Streaming_ignores_markers_that_predate_the_first_line_seen()
    {
        // Simulates "parse only new lines": the marker is in the past, the data isn't.
        var marker = new EncounterMarker(new DateTime(2026, 9, 3, 11, 0, 0));
        var p = new CombatLogProcessor(
            "Xilaria", EncounterOptions.ForRestPeriod(60), streaming: true, markers: new[] { marker });

        int n = 0;
        p.AddLine(L("12:00:00", "You crush a doomfire soldier for 100 points of damage."), ++n);
        p.AddLine(L("12:00:03", "You crush a doomfire soldier for 100 points of damage."), ++n);
        p.AddLine(L("12:00:05", "You have slain a doomfire soldier!"), ++n);
        IReadOnlyList<Encounter> encs = p.FinishStreaming(new DateTime(2026, 9, 3, 12, 5, 0));

        Assert.Single(encs); // the stale marker did not split anything
    }

    [Fact]
    public void Live_split_records_the_last_event_time()
    {
        var p = new CombatLogProcessor("Xilaria", EncounterOptions.ForRestPeriod(60), streaming: true);
        int n = 0;
        p.AddLine(L("12:00:00", "You crush a doomfire soldier for 100 points of damage."), ++n);
        p.AddLine(L("12:00:04", "You crush a doomfire soldier for 100 points of damage."), ++n);

        DateTime? at = p.ForceSplit();
        Assert.Equal(new DateTime(2026, 9, 3, 12, 0, 4), at);

        p.AddLine(L("12:00:09", "You crush a doomfire guardian for 100 points of damage."), ++n);
        p.AddLine(L("12:00:10", "You have slain a doomfire guardian!"), ++n);
        Assert.Equal(2, p.FinishStreaming(new DateTime(2026, 9, 3, 12, 1, 0)).Count);
    }

    [Theory]
    [InlineData("eqlog_Gnomies_multiclass.txt", "Gnomies_multiclass")]
    [InlineData("eqlog_Gnomies_multiclass.20260903-213005.txt", "Gnomies_multiclass")]
    [InlineData("eqlog_Gnomies_multiclass.20260903-213005-2.txt", "Gnomies_multiclass")]
    public void Log_identity_is_stable_across_archiving(string file, string expected)
    {
        Assert.Equal(expected, LogFileTailer.LogIdentity(@"C:\EverQuest\logs\" + file));
        Assert.Equal("multiclass", LogFileTailer.TryExtractServerName(file));
    }

    [Fact]
    public void Marker_store_round_trips_and_dedupes()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tlp-markers-" + Guid.NewGuid().ToString("N"));
        string savedDir = MarkerStore.DirectoryPath;
        MarkerStore.DirectoryPath = dir;
        try
        {
            const string log = @"C:\EQ\logs\eqlog_Gnomies_multiclass.txt";
            var t = new DateTime(2026, 9, 3, 21, 30, 5);

            MarkerStore.AddSplit(log, t);
            MarkerStore.AddSplit(log, t); // dedupe
            MarkerStore.AddSplit(log, t.AddMinutes(1));

            // An archived slice of the same character resolves to the same file.
            IReadOnlyList<EncounterMarker> loaded = MarkerStore.LoadForLog(
                @"C:\EQ\logs\eqlog_Gnomies_multiclass.20260903-999999.txt");
            Assert.Equal(2, loaded.Count);
            Assert.Equal(t, loaded[0].Timestamp);

            Assert.True(MarkerStore.RemoveSplit(log, t));
            Assert.Single(MarkerStore.LoadForLog(log));
        }
        finally
        {
            MarkerStore.DirectoryPath = savedDir;
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
