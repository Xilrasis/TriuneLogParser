namespace TriuneLogParser.Core.Model;

/// <summary>What kind of entity produced or received an event.</summary>
public enum EntityKind
{
    Unknown = 0,
    Player,
    Pet,
    Npc,
}

/// <summary>The high-level thing a <see cref="CombatEvent"/> represents.</summary>
public enum CombatAction
{
    Unknown = 0,
    Damage,
    Miss,
    Heal,
    Death,
}

/// <summary>
/// How a hit was delivered. Fine-grained spell classification (DoT vs. direct nuke
/// vs. proc) needs a spell catalogue and is deferred; for now non-melee damage is
/// grouped by its spell name.
/// </summary>
public enum DamageMechanic
{
    Unknown = 0,

    /// <summary>White melee: hit / crush / slash / pierce / bite / claw / gore / sting.</summary>
    Melee,

    /// <summary>Skill attacks: kick / punch / bash / backstab / strike / frenzy / special kicks.</summary>
    MeleeSpecial,

    /// <summary>"points of non-melee damage" — nukes, DoT ticks, procs, discs.</summary>
    NonMelee,

    /// <summary>Damage shields / thorns / "hit by non-melee for".</summary>
    DamageShield,
}
