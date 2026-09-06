using TriuneLogParser.Core;
using TriuneLogParser.Core.Aggregation;
using TriuneLogParser.Core.Model;
using Xunit;

namespace TriuneLogParser.Core.Tests;

public class DefenseTests
{
    private static EncounterReport Report(IEnumerable<string> lines, string character = "Xilaria")
    {
        var p = new CombatLogProcessor(character);
        int n = 0;
        foreach (string l in lines)
            p.AddLine(l, ++n);
        return EncounterAggregator.Report(p.BuildBatch());
    }

    private static string L(string hhmmss, string msg) => $"[Thu Sep 03 {hhmmss} 2026] {msg}";

    [Fact]
    public void Incoming_melee_hits_and_avoids_are_bucketed_by_type()
    {
        EncounterReport r = Report(new[]
        {
            L("12:00:00", "You crush a doomfire soldier for 50 points of damage."),
            L("12:00:00", "A doomfire soldier hits YOU for 100 points of damage."),
            L("12:00:01", "A doomfire soldier hits YOU for 300 points of damage."),
            L("12:00:02", "A doomfire soldier tries to hit YOU, but misses!"),
            L("12:00:02", "A doomfire soldier tries to hit YOU, but YOU parry!"),
            L("12:00:03", "A doomfire soldier tries to hit YOU, but YOU dodge!"),
            L("12:00:03", "A doomfire soldier tries to hit YOU, but YOU block!"),
            L("12:00:04", "A doomfire soldier tries to bash YOU, but YOU riposte!"),
            L("12:00:06", "You have slain a doomfire soldier!"),
        });

        DefenseStats d = Assert.Single(r.Defenses);
        Assert.Equal("Xilaria", d.Name);

        IncomingAttackStats hit = Assert.Single(d.Attacks, a => a.Type == "hit");
        Assert.Equal(2, hit.Hits);
        Assert.Equal(400, hit.Damage);
        Assert.Equal(100, hit.MinHit);
        Assert.Equal(300, hit.Max);
        Assert.Equal(200, hit.Average);
        Assert.Equal(1, hit.Misses);
        Assert.Equal(1, hit.Parries);
        Assert.Equal(1, hit.Dodges);
        Assert.Equal(1, hit.Blocks);
        Assert.Equal(0, hit.Ripostes);
        Assert.Equal(6, hit.Swings);

        IncomingAttackStats bash = Assert.Single(d.Attacks, a => a.Type == "bash");
        Assert.Equal(1, bash.Ripostes);
        Assert.Equal(0, bash.Hits);
        Assert.Equal("Melee Special", bash.Category);

        // Defender rollup: avoidance rate is over melee swings only.
        Assert.Equal(7, d.MeleeSwings);
        Assert.Equal(2, d.MeleeHits);
        Assert.Equal(5.0 / 7.0, d.AvoidRate, 3);
        Assert.Equal(1, d.Parries);
        Assert.Equal(1, d.Dodges);
        Assert.Equal(1, d.Blocks);
        Assert.Equal(1, d.Ripostes);
        Assert.Equal(1, d.Misses);
    }

    [Fact]
    public void Incoming_non_melee_is_its_own_type_with_no_avoidance()
    {
        EncounterReport r = Report(new[]
        {
            L("12:00:00", "You crush a doomfire adept for 10 points of damage."),
            L("12:00:01", "A doomfire adept hit YOU for 500 points of non-melee damage. (Frost Breath)"),
            L("12:00:03", "A doomfire adept hit YOU for 700 points of non-melee damage. (Frost Breath)"),
            L("12:00:05", "You have slain a doomfire adept!"),
        });

        DefenseStats d = Assert.Single(r.Defenses);
        IncomingAttackStats fb = Assert.Single(d.Attacks);
        Assert.Equal("non-melee: Frost Breath", fb.Type);
        Assert.False(fb.IsMelee);
        Assert.Equal(2, fb.Hits);
        Assert.Equal(2, fb.Swings);
        Assert.Equal(1200, fb.Damage);
        Assert.Equal(600, fb.Average);
        Assert.Equal(0, d.MeleeSwings);
        Assert.Equal(0, d.AvoidRate);
    }

    [Fact]
    public void Csv_export_has_a_defense_section()
    {
        string csv = EncounterExport.ToCsv(Report(new[]
        {
            L("12:00:00", "You crush a doomfire soldier for 50 points of damage."),
            L("12:00:01", "A doomfire soldier hits YOU for 100 points of damage."),
            L("12:00:02", "A doomfire soldier tries to hit YOU, but YOU parry!"),
            L("12:00:05", "You have slain a doomfire soldier!"),
        }));

        Assert.Contains("defender,attack_type,category,swings,hits,damage", csv);
        Assert.Contains("Xilaria,hit,Melee,2,1,100", csv);
    }
}
