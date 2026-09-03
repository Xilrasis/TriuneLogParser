using TriuneLogParser.Core;
using TriuneLogParser.Core.Logging;
using TriuneLogParser.Core.Model;
using TriuneLogParser.Core.Parsing;
using Xunit;

namespace TriuneLogParser.Core.Tests;

public class ParserTests
{
    private static (CombatLogParser parser, CombatEvent evt) ParseOne(string message, string? character = "Xilaria")
    {
        var parser = new CombatLogParser(new NameResolver(character), new PetRegistry());
        string raw = $"[Thu Sep 03 12:04:51 2026] {message}";
        Assert.True(LogLine.TryParse(raw, 1, out LogLine line));
        ParseOutcome outcome = parser.Parse(line);
        Assert.NotNull(outcome.Event);
        return (parser, outcome.Event!);
    }

    [Fact]
    public void Parses_timestamp()
    {
        Assert.True(LogLine.TryParse("[Thu Sep 03 12:04:51 2026] hi", 1, out LogLine line));
        Assert.Equal(new DateTime(2026, 9, 3, 12, 4, 51), line.Timestamp);
        Assert.Equal("hi", line.Message);
    }

    [Fact]
    public void Rejects_non_timestamped_line()
    {
        Assert.False(LogLine.TryParse("no timestamp here", 1, out _));
    }

    [Fact]
    public void Self_melee_hit()
    {
        var (_, e) = ParseOne("You kick a flame lordling for 4356 points of damage.");
        Assert.Equal(CombatAction.Damage, e.Action);
        Assert.Equal("Xilaria", e.Attacker);
        Assert.Equal("a flame lordling", e.Target);
        Assert.Equal(4356, e.Amount);
        Assert.Equal("kick", e.Verb);
        Assert.Equal(DamageMechanic.MeleeSpecial, e.Mechanic);
    }

    [Fact]
    public void Self_white_melee_is_plain_melee()
    {
        var (_, e) = ParseOne("You crush a flame lordling for 1649 points of damage.");
        Assert.Equal(DamageMechanic.Melee, e.Mechanic);
        Assert.Equal("crush", e.Verb);
    }

    [Fact]
    public void Self_melee_miss()
    {
        var (_, e) = ParseOne("You try to kick a flame lordling, but miss!");
        Assert.Equal(CombatAction.Miss, e.Action);
        Assert.Equal("miss", e.MissReason);
        Assert.Equal("Xilaria", e.Attacker);
    }

    [Fact]
    public void Other_melee_hit_normalises_verb()
    {
        var (_, e) = ParseOne("Nixalir crushes a doomfire guardian for 270 points of damage.");
        Assert.Equal("Nixalir", e.Attacker);
        Assert.Equal("crush", e.Verb);
        Assert.Equal(270, e.Amount);
    }

    [Fact]
    public void Non_melee_with_spell_source()
    {
        var (_, e) = ParseOne("Xilaria hit a flame lordling for 2344 points of non-melee damage. (Distant Strike)");
        Assert.Equal(DamageMechanic.NonMelee, e.Mechanic);
        Assert.Equal("Distant Strike", e.SpellName);
        Assert.Equal(2344, e.Amount);
    }

    [Fact]
    public void Incoming_melee_to_player()
    {
        var (_, e) = ParseOne("A doomfire soldier hits YOU for 233 points of damage.");
        Assert.Equal("a doomfire soldier", e.Attacker);
        Assert.Equal("Xilaria", e.Target);
        Assert.Equal(233, e.Amount);
    }

    [Fact]
    public void Damage_shield_line()
    {
        var (_, e) = ParseOne("a doomfire guardian was hit by non-melee for 43 points of damage.");
        Assert.Equal(DamageMechanic.DamageShield, e.Mechanic);
        Assert.Equal("a doomfire guardian", e.Target);
        Assert.Null(e.Attacker);
    }

    [Fact]
    public void Pet_owner_tag_is_stripped_and_registered()
    {
        var (parser, e) = ParseOne("Nixalir (Owner: Gnomies) hit a doomfire protector for 969 points of non-melee damage. (Hand of Retribution)");
        Assert.Equal("Nixalir", e.Attacker);
        Assert.Equal("Gnomies", e.AttackerOwner);
        Assert.True(parser.Pets.IsPet("Nixalir"));
        Assert.Equal("Gnomies", parser.Pets.OwnerOf("Nixalir"));
    }

    [Fact]
    public void Pet_owner_tag_on_heal()
    {
        var (_, e) = ParseOne("Karnalath (Owner: Gnomies) has healed Xilaria for 516 points of damage. (Hand of Retribution Recourse)");
        Assert.Equal(CombatAction.Heal, e.Action);
        Assert.Equal("Karnalath", e.Attacker);
        Assert.Equal("Gnomies", e.AttackerOwner);
        Assert.Equal("Xilaria", e.Target);
        Assert.Equal(516, e.Amount);
    }

    [Fact]
    public void Self_heal_resolves_pronoun()
    {
        var (_, e) = ParseOne("Xilaria has healed herself for 240 points of damage. (Killing Spree)");
        Assert.Equal(CombatAction.Heal, e.Action);
        Assert.Equal("Xilaria", e.Attacker);
        Assert.Equal("Xilaria", e.Target);
        Assert.Equal("Killing Spree", e.SpellName);
    }

    [Fact]
    public void Death_by_killer()
    {
        var (_, e) = ParseOne("a doomfire chaplain has been slain by Nixalir!");
        Assert.Equal(CombatAction.Death, e.Action);
        Assert.Equal("a doomfire chaplain", e.Target);
        Assert.Equal("Nixalir", e.Attacker);
    }

    [Fact]
    public void You_have_slain()
    {
        var (_, e) = ParseOne("You have slain a doomfire soldier!");
        Assert.Equal(CombatAction.Death, e.Action);
        Assert.Equal("a doomfire soldier", e.Target);
        Assert.Equal("Xilaria", e.Attacker);
    }

    [Fact]
    public void Crit_marker_is_not_an_event()
    {
        var parser = new CombatLogParser(new NameResolver("Xilaria"), new PetRegistry());
        Assert.True(LogLine.TryParse("[Thu Sep 03 12:04:51 2026] Xilaria scores a critical hit! (4356)", 1, out LogLine line));
        ParseOutcome outcome = parser.Parse(line);
        Assert.Null(outcome.Event);
        Assert.NotNull(outcome.Crit);
        Assert.Equal(4356, outcome.Crit!.Amount);
        Assert.True(outcome.Crit.IsSelf);
    }

    [Fact]
    public void Zone_change_detected()
    {
        var parser = new CombatLogParser(new NameResolver("Xilaria"), new PetRegistry());
        Assert.True(LogLine.TryParse("[Thu Sep 03 12:04:51 2026] You have entered the Bazaar.", 1, out LogLine line));
        ParseOutcome outcome = parser.Parse(line);
        Assert.NotNull(outcome.Zone);
        Assert.Equal("the Bazaar", outcome.Zone!.Zone);
    }

    [Theory]
    [InlineData("eqlog_Xilaria_multiclass.txt", "Xilaria", "multiclass")]
    [InlineData("C:/EverQuest/logs/eqlog_Gnomies_project_triune.txt", "Gnomies", "project_triune")]
    public void Extracts_character_from_filename(string path, string character, string server)
    {
        Assert.Equal(character, LogFileTailer.TryExtractCharacterName(path));
        Assert.Equal(server, LogFileTailer.TryExtractServerName(path));
    }
}
