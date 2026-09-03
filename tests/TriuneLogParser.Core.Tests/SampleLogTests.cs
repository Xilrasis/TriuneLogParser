using TriuneLogParser.Core;
using TriuneLogParser.Core.Aggregation;
using TriuneLogParser.Core.Model;
using Xunit;

namespace TriuneLogParser.Core.Tests;

/// <summary>End-to-end check against the committed sample log.</summary>
public class SampleLogTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "eqlog_Sampleton_multiclass.txt");

    [Fact]
    public void Sample_log_parses_cleanly()
    {
        Assert.True(File.Exists(FixturePath), $"missing fixture: {FixturePath}");

        var p = new CombatLogProcessor("Sampleton");
        int n = 0;
        foreach (string line in File.ReadLines(FixturePath))
            p.AddLine(line, ++n);

        IReadOnlyList<Encounter> encounters = p.BuildBatch();

        Assert.Equal(0, p.Metrics.UnparsedDamageLike);
        Assert.Equal(2, encounters.Count);

        Encounter first = encounters[0];
        Assert.Equal(2, first.NpcsKilled.Count);
        Assert.Equal(EncounterEndReason.AllMobsDead, first.EndReason);

        EncounterReport r = EncounterAggregator.Report(first);
        FighterStats sampleton = Assert.Single(r.DamageDone);
        Assert.Equal("Sampleton", sampleton.Name);

        // Own melee + non-melee + folded pet (Grumble: 240 + 355 + 210).
        Assert.Equal(6211, sampleton.DamageDone);
        Assert.Contains("Grumble", sampleton.Pets);
        Assert.DoesNotContain(r.DamageDone, f => f.Name == "Grumble");

        // The 812 crush marked critical by the adjacent marker line.
        Assert.Equal(1, sampleton.CritHits);
        Assert.Equal(220, sampleton.HealingDone);
        Assert.Equal(401, sampleton.DamageTaken);
    }

    [Fact]
    public void Time_range_merge_across_sample_encounters()
    {
        var p = new CombatLogProcessor("Sampleton");
        int n = 0;
        foreach (string line in File.ReadLines(FixturePath))
            p.AddLine(line, ++n);

        IReadOnlyList<Encounter> encounters = p.BuildBatch();
        EncounterReport merged = EncounterAggregator.Report(encounters);

        Assert.Equal(2, merged.EncounterIds.Count);
        Assert.Equal(6211 + 1990, merged.DamageDone.Single().DamageDone);
    }
}
