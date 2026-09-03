using TriuneLogParser.Core;
using TriuneLogParser.Core.Aggregation;
using TriuneLogParser.Core.Classes;
using TriuneLogParser.Core.Model;
using Xunit;

namespace TriuneLogParser.Core.Tests;

public class Phase4Tests
{
    private static IReadOnlyList<Encounter> Build(IEnumerable<string> lines, out CombatLogProcessor p, string character = "Xilaria")
    {
        p = new CombatLogProcessor(character);
        int n = 0;
        foreach (string l in lines)
            p.AddLine(l, ++n);
        return p.BuildBatch();
    }

    private static string L(string hhmmss, string msg) => $"[Thu Sep 03 {hhmmss} 2026] {msg}";

    [Fact]
    public void Mob_report_tracks_damage_killer_and_ttk()
    {
        var enc = Build(new[]
        {
            L("12:00:00", "You crush a doomfire soldier for 100 points of damage."),
            L("12:00:04", "Gnomies pierces a doomfire soldier for 300 points of damage."),
            L("12:00:06", "You crush a doomfire soldier for 100 points of damage."),
            L("12:00:07", "a doomfire soldier has been slain by Gnomies!"),
        }, out _);

        EncounterReport r = EncounterAggregator.Report(enc);
        MobStats mob = Assert.Single(r.Mobs);
        Assert.Equal("a doomfire soldier", mob.Name);
        Assert.Equal(500, mob.DamageTaken);
        Assert.Equal("Gnomies", mob.LastKiller);
        Assert.Equal(7, mob.TimeToKillSeconds, 1);
        Assert.Equal("Gnomies", mob.ByFighter[0].Fighter); // 300 > 200
        Assert.Equal(300, mob.ByFighter[0].Damage);
    }

    [Fact]
    public void Csv_export_has_rows_and_a_mob_section()
    {
        var enc = Build(new[]
        {
            L("12:00:00", "You kick a doomfire soldier for 500 points of damage."),
            L("12:00:02", "You have slain a doomfire soldier!"),
        }, out _);

        string csv = EncounterExport.ToCsv(EncounterAggregator.Report(enc));
        Assert.Contains("metric,fighter,category,source", csv);
        Assert.Contains("damage_done,Xilaria,TOTAL", csv);
        Assert.Contains("mob,damage_taken,deaths,last_killer", csv);
        Assert.Contains("a doomfire soldier,500", csv);
    }

    [Fact]
    public void Json_export_is_valid_json()
    {
        var enc = Build(new[]
        {
            L("12:00:00", "You kick a doomfire soldier for 500 points of damage."),
            L("12:00:02", "You have slain a doomfire soldier!"),
        }, out _);

        string json = EncounterExport.ToJson(EncounterAggregator.Report(enc));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("a doomfire soldier", doc.RootElement.GetProperty("mobs")[0].GetProperty("Name").GetString());
    }

    [Fact]
    public void Swarm_pet_damage_folds_into_owner()
    {
        var enc = Build(new[]
        {
            L("12:00:00", "Gnomies pierces a doomfire soldier for 500 points of damage."),
            L("12:00:01", "Gnomies`s Servant of Ro hits a doomfire soldier for 200 points of damage."),
            L("12:00:02", "Gnomies`s Host of the Elements hits a doomfire soldier for 150 points of damage."),
            L("12:00:03", "a doomfire soldier has been slain by Gnomies!"),
        }, out _);

        EncounterReport r = EncounterAggregator.Report(enc);
        FighterStats g = Assert.Single(r.DamageDone, f => f.Name == "Gnomies");
        Assert.Equal(850, g.DamageDone);
        Assert.Contains("Gnomies`s Servant of Ro", g.Pets);
        Assert.DoesNotContain(r.DamageDone, f => f.Name.Contains("Servant of Ro"));
    }

    [Fact]
    public void Class_tracker_infers_from_signature_abilities()
    {
        Build(new[]
        {
            L("12:00:00", "Xilaria hit a doomfire soldier for 100 points of non-melee damage. (Flying Kick)"),
            L("12:00:02", "Xilaria hit a doomfire soldier for 120 points of non-melee damage. (Flying Kick)"),
            L("12:00:03", "You backstab a doomfire soldier for 900 points of damage."),
            L("12:00:04", "You backstab a doomfire soldier for 800 points of damage."),
            L("12:00:05", "You have slain a doomfire soldier!"),
        }, out CombatLogProcessor p);

        IReadOnlyList<EqClass> classes = p.Classes.ClassesOf("Xilaria");
        Assert.Contains(EqClass.Monk, classes);
        Assert.Contains(EqClass.Rogue, classes);
    }
}
