using TriuneLogParser.Core;
using TriuneLogParser.Core.Aggregation;
using TriuneLogParser.Core.Encounters;
using TriuneLogParser.Core.Model;
using Xunit;

namespace TriuneLogParser.Core.Tests;

public class EncounterTests
{
    private static IReadOnlyList<Encounter> Build(
        IEnumerable<string> lines, string character = "Xilaria",
        int idle = 45, bool splitOnKill = true, int reengage = 12)
    {
        var p = new CombatLogProcessor(character, new EncounterOptions
        {
            IdleTimeout = TimeSpan.FromSeconds(idle),
            SplitOnAllMobsDead = splitOnKill,
            ReengageWindow = TimeSpan.FromSeconds(reengage),
        });
        int n = 0;
        foreach (string l in lines)
            p.AddLine(l, ++n);
        return p.BuildBatch();
    }

    private static string L(string hhmmss, string msg) => $"[Thu Sep 03 {hhmmss} 2026] {msg}";

    [Fact]
    public void Fight_closes_on_kill()
    {
        var enc = Build(new[]
        {
            L("12:00:00", "You crush a doomfire soldier for 100 points of damage."),
            L("12:00:02", "You crush a doomfire soldier for 120 points of damage."),
            L("12:00:03", "You have slain a doomfire soldier!"),
            L("12:05:00", "You crush a doomfire guardian for 90 points of damage."),
            L("12:05:01", "You have slain a doomfire guardian!"),
        });

        Assert.Equal(2, enc.Count);
        Assert.Equal(EncounterEndReason.AllMobsDead, enc[0].EndReason);
        Assert.Contains("doomfire soldier", enc[0].Title);
    }

    [Fact]
    public void Rapid_chain_pulls_merge_into_one_encounter()
    {
        var enc = Build(new[]
        {
            L("12:00:00", "You crush a doomfire soldier for 100 points of damage."),
            L("12:00:03", "You have slain a doomfire soldier!"),
            L("12:00:10", "You crush a doomfire guardian for 90 points of damage."),
            L("12:00:13", "You have slain a doomfire guardian!"),
        });

        Assert.Single(enc);
        Assert.Equal(2, enc[0].NpcsKilled.Count);
    }

    [Fact]
    public void Pause_between_pulls_splits_them()
    {
        var enc = Build(new[]
        {
            L("12:00:00", "You crush a doomfire soldier for 100 points of damage."),
            L("12:00:03", "You have slain a doomfire soldier!"),
            L("12:00:40", "You crush a doomfire guardian for 90 points of damage."),
            L("12:00:43", "You have slain a doomfire guardian!"),
        });

        Assert.Equal(2, enc.Count);
    }

    [Fact]
    public void Strict_per_pull_when_reengage_window_is_zero()
    {
        var enc = Build(new[]
        {
            L("12:00:00", "You crush a doomfire soldier for 100 points of damage."),
            L("12:00:03", "You have slain a doomfire soldier!"),
            L("12:00:05", "You crush a doomfire guardian for 90 points of damage."),
            L("12:00:07", "You have slain a doomfire guardian!"),
        }, reengage: 0);

        Assert.Equal(2, enc.Count);
        Assert.Equal(EncounterEndReason.AllMobsDead, enc[0].EndReason);
    }

    [Fact]
    public void Multi_mob_pull_is_one_encounter()
    {
        var enc = Build(new[]
        {
            L("12:00:00", "You crush a doomfire soldier for 100 points of damage."),
            L("12:00:02", "A doomfire guardian hits YOU for 50 points of damage."),
            L("12:00:03", "You crush a doomfire guardian for 100 points of damage."),
            L("12:00:05", "You have slain a doomfire soldier!"),
            L("12:00:08", "You crush a doomfire guardian for 100 points of damage."),
            L("12:00:10", "You have slain a doomfire guardian!"),
        });

        Assert.Single(enc);
        Assert.Equal(2, enc[0].NpcsKilled.Count);
        Assert.Contains("+1", enc[0].Title);
    }

    [Fact]
    public void Idle_gap_splits_encounters()
    {
        var enc = Build(new[]
        {
            L("12:00:00", "You crush a doomfire soldier for 100 points of damage."),
            L("12:00:05", "You crush a doomfire soldier for 100 points of damage."),
            // no death, just a long quiet gap
            L("12:02:00", "You crush a doomfire guardian for 100 points of damage."),
            L("12:02:05", "You crush a doomfire guardian for 100 points of damage."),
        }, idle: 45);

        Assert.Equal(2, enc.Count);
        Assert.Equal(EncounterEndReason.Idle, enc[0].EndReason);
    }

    [Fact]
    public void Zone_change_closes_fight()
    {
        var enc = Build(new[]
        {
            L("12:00:00", "You crush a doomfire soldier for 100 points of damage."),
            L("12:00:03", "You crush a doomfire soldier for 100 points of damage."),
            L("12:00:06", "You crush a doomfire soldier for 100 points of damage."),
            L("12:00:08", "You have entered the Bazaar."),
            L("12:00:20", "You crush a training dummy for 100 points of damage."),
            L("12:00:24", "You crush a training dummy for 100 points of damage."),
        });

        Assert.Equal(2, enc.Count);
        Assert.Equal(EncounterEndReason.ZoneChange, enc[0].EndReason);
    }

    [Fact]
    public void Pet_damage_folds_into_owner()
    {
        var enc = Build(new[]
        {
            L("12:00:00", "Gnomies pierces a doomfire soldier for 500 points of damage."),
            L("12:00:01", "Nixalir (Owner: Gnomies) hit a doomfire soldier for 300 points of non-melee damage. (Frost Claw)"),
            L("12:00:02", "Nixalir crushes a doomfire soldier for 100 points of damage."),
            L("12:00:03", "You crush a doomfire soldier for 50 points of damage."),
            L("12:00:04", "You have slain a doomfire soldier!"),
        });

        EncounterReport r = EncounterAggregator.Report(enc[0]);
        FighterStats gnomies = Assert.Single(r.DamageDone, f => f.Name == "Gnomies");

        // 500 (own) + 300 (pet nuke) + 100 (untagged pet swing) — all under Gnomies.
        Assert.Equal(900, gnomies.DamageDone);
        Assert.Contains("Nixalir", gnomies.Pets);
        Assert.DoesNotContain(r.DamageDone, f => f.Name == "Nixalir");
        Assert.Contains(gnomies.DamageSources, b => b.PetName == "Nixalir" && b.Name == "Frost Claw");
    }

    [Fact]
    public void Ownerless_pet_stays_separate()
    {
        var enc = Build(new[]
        {
            L("12:00:00", "You crush a doomfire soldier for 100 points of damage."),
            L("12:00:01", "Grimfang crushes a doomfire soldier for 80 points of damage."),
            L("12:00:02", "You have slain a doomfire soldier!"),
        });

        EncounterReport r = EncounterAggregator.Report(enc[0]);
        Assert.Contains(r.DamageDone, f => f.Name == "Grimfang");
    }

    [Fact]
    public void Critical_marker_flags_matching_hit()
    {
        var enc = Build(new[]
        {
            L("12:00:00", "You kick a doomfire soldier for 4356 points of damage."),
            L("12:00:00", "Xilaria scores a critical hit! (4356)"),
            L("12:00:02", "You kick a doomfire soldier for 200 points of damage."),
            L("12:00:03", "You have slain a doomfire soldier!"),
        });

        EncounterReport r = EncounterAggregator.Report(enc[0]);
        FighterStats xil = Assert.Single(r.DamageDone, f => f.Name == "Xilaria");
        Assert.Equal(1, xil.CritHits);
        Assert.Equal(2, xil.SwingHits);
    }

    [Fact]
    public void Time_range_grouping_merges_encounters()
    {
        var enc = Build(new[]
        {
            L("12:00:00", "You crush a doomfire soldier for 100 points of damage."),
            L("12:00:01", "You have slain a doomfire soldier!"),
            L("12:10:00", "You crush a doomfire guardian for 300 points of damage."),
            L("12:10:01", "You have slain a doomfire guardian!"),
        });

        Assert.Equal(2, enc.Count);
        EncounterReport merged = EncounterAggregator.Report(enc);
        FighterStats xil = Assert.Single(merged.DamageDone);
        Assert.Equal(400, xil.DamageDone);
        Assert.Equal(2, merged.EncounterIds.Count);
    }

    [Fact]
    public void Damage_taken_and_deaths_tracked()
    {
        var enc = Build(new[]
        {
            L("12:00:00", "You crush a doomfire soldier for 100 points of damage."),
            L("12:00:01", "A doomfire soldier hits YOU for 999 points of damage."),
            L("12:00:02", "You have been slain by a doomfire soldier!"),
            L("12:00:03", "You have slain a doomfire soldier!"),
        });

        EncounterReport r = EncounterAggregator.Report(enc[0]);
        FighterStats xil = Assert.Single(r.DamageTaken, f => f.Name == "Xilaria");
        Assert.Equal(999, xil.DamageTaken);
        Assert.Contains(enc[0].PlayerDeaths, d => d == "Xilaria");
    }
}
