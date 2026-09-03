namespace TriuneLogParser.Core.Classes;

public enum EqClass
{
    Warrior, Cleric, Paladin, Ranger, Shadowknight, Druid, Monk, Bard, Rogue,
    Shaman, Necromancer, Wizard, Magician, Enchanter, Beastlord, Berserker,
}

/// <summary>
/// Best-effort mapping of signature ability / spell names to the class that uses them.
/// Project Triune is multiclass, so a character can legitimately match several — the
/// tracker reports every class it has evidence for. Only reasonably unambiguous
/// signatures are listed; add more in <c>docs/log-format.md</c> as they're confirmed.
/// </summary>
public static class ClassCatalog
{
    public static readonly IReadOnlyDictionary<string, EqClass> Signatures =
        new Dictionary<string, EqClass>(StringComparer.OrdinalIgnoreCase)
        {
            // Shadowknight
            ["Harm Touch"] = EqClass.Shadowknight,
            ["Leech Touch"] = EqClass.Shadowknight,
            ["Touch of the Cursed"] = EqClass.Shadowknight,
            ["Explosion of Spite"] = EqClass.Shadowknight,
            ["Visions of Grandeur"] = EqClass.Shadowknight,

            // Paladin
            ["Lay on Hands"] = EqClass.Paladin,
            ["Lay Hands"] = EqClass.Paladin,
            ["Hand of Piety"] = EqClass.Paladin,

            // Cleric
            ["Divine Arbitration"] = EqClass.Cleric,
            ["Divine Intervention"] = EqClass.Cleric,
            ["Celestial Regeneration"] = EqClass.Cleric,
            ["Aura of Divinity"] = EqClass.Cleric,

            // Rogue
            ["Backstab"] = EqClass.Rogue,
            ["Envenomed Blades"] = EqClass.Rogue,
            ["Envenomed Blades Effect"] = EqClass.Rogue,
            ["Twisted Shank"] = EqClass.Rogue,
            ["Assassin's Terror"] = EqClass.Rogue,
            ["Assassinate"] = EqClass.Rogue,

            // Monk
            ["Flying Kick"] = EqClass.Monk,
            ["Eagle Strike"] = EqClass.Monk,
            ["Tiger Claw"] = EqClass.Monk,
            ["Dragon Punch"] = EqClass.Monk,
            ["Stunning Kick"] = EqClass.Monk,
            ["Mend"] = EqClass.Monk,
            ["Crane Stance"] = EqClass.Monk,
            ["Distant Strike"] = EqClass.Monk,
            ["Press the Attack"] = EqClass.Monk,
            ["Grappling Strike"] = EqClass.Monk,
            ["Gut Punch"] = EqClass.Monk,

            // Berserker
            ["Frenzy"] = EqClass.Berserker,
            ["Vicious Bite of Chaos"] = EqClass.Berserker,
            ["Vicious Bite of Chaos Recourse"] = EqClass.Berserker,
            ["Cleave"] = EqClass.Berserker,
            ["Blinding Fury"] = EqClass.Berserker,

            // Wizard
            ["Twincast"] = EqClass.Wizard,
            ["Improved Familiar"] = EqClass.Wizard,
            ["Prismatic Strike IV"] = EqClass.Wizard,
            ["Distant Conflagration"] = EqClass.Wizard,

            // Magician
            ["Elemental Mastery Blast"] = EqClass.Magician,
            ["Elemental Mastery Strike"] = EqClass.Magician,
            ["Elemental Union"] = EqClass.Magician,
            ["Host of the Elements"] = EqClass.Magician,

            // Necromancer
            ["Mortal Coil"] = EqClass.Necromancer,
            ["Dead Man Floating"] = EqClass.Necromancer,
            ["Wake the Dead"] = EqClass.Necromancer,
            ["Spear of Decay"] = EqClass.Necromancer,
            ["Pestilence Shock Strike"] = EqClass.Necromancer,

            // Enchanter
            ["Time Lapse"] = EqClass.Enchanter,
            ["Chromatic Haze"] = EqClass.Enchanter,
            ["Mana Draw"] = EqClass.Enchanter,

            // Druid
            ["Nature's Guardian"] = EqClass.Druid,
            ["Spirit of the Wood"] = EqClass.Druid,
            ["Frost Claw"] = EqClass.Druid,

            // Shaman
            ["Ancestral Guard"] = EqClass.Shaman,
            ["Rabid Bear"] = EqClass.Shaman,
            ["Spear of Fever"] = EqClass.Shaman,

            // Beastlord
            ["Bestial Alignment"] = EqClass.Beastlord,
            ["Feral Swipe"] = EqClass.Beastlord,

            // Ranger
            ["Auspice of the Hunter"] = EqClass.Ranger,
            ["Trueshot"] = EqClass.Ranger,
            ["Outrider's Accuracy"] = EqClass.Ranger,

            // Bard
            ["Fierce Eye"] = EqClass.Bard,
            ["Quick Time"] = EqClass.Bard,
            ["Dance of Blades"] = EqClass.Bard,
        };

    public static readonly IReadOnlyDictionary<string, EqClass> VerbSignatures =
        new Dictionary<string, EqClass>(StringComparer.OrdinalIgnoreCase)
        {
            ["backstab"] = EqClass.Rogue,
        };
}
