using TriuneLogParser.Core.Model;

namespace TriuneLogParser.Core.Aggregation;

/// <summary>One damage/heal source for a fighter (a swing verb, a spell, a damage shield).</summary>
public sealed class SourceBucket
{
    public required string Category { get; init; }   // "Melee", "Melee Special", "Non-melee", "Damage Shield"
    public required string Name { get; init; }        // "crush", "Distant Strike", ...
    public string? PetName { get; init; }             // set when this bucket is a pet's contribution

    public long Total { get; set; }
    public long Hits { get; set; }
    public long Crits { get; set; }
    public long Min { get; set; } = long.MaxValue;
    public long Max { get; set; }

    public double Average => Hits > 0 ? (double)Total / Hits : 0;
    public double CritRate => Hits > 0 ? (double)Crits / Hits : 0;

    public void Add(long amount, bool crit)
    {
        Total += amount;
        Hits++;
        if (crit) Crits++;
        if (amount < Min) Min = amount;
        if (amount > Max) Max = amount;
    }
}

/// <summary>Rolled-up numbers for one fighter over one or more encounters.</summary>
public sealed class FighterStats
{
    public required string Name { get; init; }
    public EntityKind Kind { get; set; }
    public string? Owner { get; set; }

    public long DamageDone { get; set; }
    public long DamageTaken { get; set; }
    public long HealingDone { get; set; }
    public long HealingReceived { get; set; }
    public int Deaths { get; set; }

    public long SwingHits { get; set; }
    public long SwingMisses { get; set; }
    public long CritHits { get; set; }

    public List<SourceBucket> DamageSources { get; } = new();
    public List<SourceBucket> DamageTakenSources { get; } = new();
    public List<SourceBucket> HealingSources { get; } = new();

    /// <summary>Names of pets whose output was folded into this fighter.</summary>
    public SortedSet<string> Pets { get; } = new(StringComparer.OrdinalIgnoreCase);

    public double Accuracy => (SwingHits + SwingMisses) > 0
        ? (double)SwingHits / (SwingHits + SwingMisses)
        : 0;

    public double CritRate => SwingHits > 0 ? (double)CritHits / SwingHits : 0;

    public double DpsOver(double seconds) => seconds > 0 ? DamageDone / seconds : 0;
    public double HpsOver(double seconds) => seconds > 0 ? HealingDone / seconds : 0;
}

/// <summary>
/// One incoming attack type against a defender — a melee verb ("crush", "bash") or a
/// non-melee source ("non-melee", or "non-melee: &lt;spell&gt;") — and how the defender's
/// rolls against it resolved. Avoidance (parry / dodge / block / riposte) only happens on
/// melee; non-melee rows only ever carry landed hits.
/// </summary>
public sealed class IncomingAttackStats
{
    public required string Type { get; init; }
    public required string Category { get; init; }   // "Melee", "Melee Special", "Non-melee", "Damage Shield"

    public long Hits { get; set; }
    public long Misses { get; set; }
    public long Parries { get; set; }
    public long Dodges { get; set; }
    public long Blocks { get; set; }
    public long Ripostes { get; set; }
    public long Absorbs { get; set; }          // rune / "magical skin absorbs the blow"
    public long Invulnerables { get; set; }    // "but <you> are INVULNERABLE!"

    public long Damage { get; set; }
    public long Crits { get; set; }
    public long Min { get; set; } = long.MaxValue;
    public long Max { get; set; }

    /// <summary>True for anything that can be actively avoided (melee); non-melee can only land or be resisted.</summary>
    public bool IsMelee => Category is "Melee" or "Melee Special";

    /// <summary>Every swing the attacker took at the defender with this attack type.</summary>
    public long Swings => Hits + Misses + Parries + Dodges + Blocks + Ripostes + Absorbs + Invulnerables;
    public long Avoided => Swings - Hits;

    public double Average => Hits > 0 ? (double)Damage / Hits : 0;
    public long MinHit => Hits > 0 && Min != long.MaxValue ? Min : 0;

    /// <summary>Attacker's accuracy against this defender for this attack type.</summary>
    public double HitRate => Swings > 0 ? (double)Hits / Swings : 0;
    public double AvoidRate => Swings > 0 ? (double)Avoided / Swings : 0;
    public double CritRate => Hits > 0 ? (double)Crits / Hits : 0;

    public double MissRate => Swings > 0 ? (double)Misses / Swings : 0;
    public double ParryRate => Swings > 0 ? (double)Parries / Swings : 0;
    public double DodgeRate => Swings > 0 ? (double)Dodges / Swings : 0;
    public double BlockRate => Swings > 0 ? (double)Blocks / Swings : 0;
    public double RiposteRate => Swings > 0 ? (double)Ripostes / Swings : 0;

    public void AddHit(long amount, bool crit)
    {
        Hits++;
        Damage += amount;
        if (crit) Crits++;
        if (amount < Min) Min = amount;
        if (amount > Max) Max = amount;
    }

    public void AddAvoid(string? reason)
    {
        switch (reason)
        {
            case "parry": Parries++; break;
            case "dodge": Dodges++; break;
            case "block": Blocks++; break;
            case "riposte": Ripostes++; break;
            case "rune": Absorbs++; break;
            case "invulnerable": Invulnerables++; break;
            default: Misses++; break;
        }
    }
}

/// <summary>How one defender fared against incoming attacks across the reported encounters.</summary>
public sealed class DefenseStats
{
    public required string Name { get; init; }
    public int Deaths { get; set; }

    /// <summary>Per incoming attack type, biggest damage first.</summary>
    public List<IncomingAttackStats> Attacks { get; } = new();

    public long Swings => Attacks.Sum(a => a.Swings);
    public long Hits => Attacks.Sum(a => a.Hits);
    public long Damage => Attacks.Sum(a => a.Damage);
    public long Crits => Attacks.Sum(a => a.Crits);

    public long Misses => Attacks.Sum(a => a.Misses);
    public long Parries => Attacks.Sum(a => a.Parries);
    public long Dodges => Attacks.Sum(a => a.Dodges);
    public long Blocks => Attacks.Sum(a => a.Blocks);
    public long Ripostes => Attacks.Sum(a => a.Ripostes);
    public long Absorbs => Attacks.Sum(a => a.Absorbs);

    /// <summary>Melee swings only — the denominator for a meaningful avoidance rate.</summary>
    public long MeleeSwings => Attacks.Where(a => a.IsMelee).Sum(a => a.Swings);
    public long MeleeHits => Attacks.Where(a => a.IsMelee).Sum(a => a.Hits);
    public long MeleeAvoided => MeleeSwings - MeleeHits;

    public double AvoidRate => MeleeSwings > 0 ? (double)MeleeAvoided / MeleeSwings : 0;
    public double HitRate => MeleeSwings > 0 ? (double)MeleeHits / MeleeSwings : 0;
    public long Max => Attacks.Count > 0 ? Attacks.Max(a => a.Max) : 0;
}

/// <summary>The full breakdown for a set of encounters.</summary>
public sealed class EncounterReport
{
    public required IReadOnlyList<int> EncounterIds { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }

    /// <summary>Summed fight time (not wall-clock), used for DPS/HPS.</summary>
    public double DurationSeconds { get; init; }

    public long TotalDamage { get; init; }
    public long TotalHealing { get; init; }

    /// <summary>Fighters that dealt damage, highest first.</summary>
    public required IReadOnlyList<FighterStats> DamageDone { get; init; }

    /// <summary>Fighters that took damage, highest first.</summary>
    public required IReadOnlyList<FighterStats> DamageTaken { get; init; }

    /// <summary>Fighters that healed, highest first.</summary>
    public required IReadOnlyList<FighterStats> Healing { get; init; }

    public IReadOnlyList<string> Titles { get; init; } = Array.Empty<string>();

    /// <summary>Per-target-NPC breakdown ("who killed what"), highest damage first.</summary>
    public IReadOnlyList<MobStats> Mobs { get; init; } = Array.Empty<MobStats>();

    /// <summary>Per-defender incoming-attack / avoidance breakdown, most-attacked first.</summary>
    public IReadOnlyList<DefenseStats> Defenses { get; init; } = Array.Empty<DefenseStats>();
}

/// <summary>Damage dealt to one NPC across the reported encounters.</summary>
public sealed class MobStats
{
    public required string Name { get; init; }
    public long DamageTaken { get; set; }
    public int Deaths { get; set; }
    public string? LastKiller { get; set; }
    public DateTime FirstHit { get; set; } = DateTime.MaxValue;
    public DateTime LastHit { get; set; } = DateTime.MinValue;

    /// <summary>Damage into this mob per attacking fighter (pets folded into owner), highest first.</summary>
    public List<MobFighterDamage> ByFighter { get; } = new();

    public double TimeToKillSeconds =>
        LastHit > FirstHit ? (LastHit - FirstHit).TotalSeconds : 0;
}

/// <summary>One fighter's damage into one mob, broken down by source the same way
/// <see cref="FighterStats.DamageSources"/> is.</summary>
public sealed class MobFighterDamage
{
    public required string Fighter { get; init; }
    public long Damage { get; set; }
    public List<SourceBucket> Sources { get; } = new();
}
