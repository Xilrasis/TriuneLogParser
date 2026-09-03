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
    public List<(string Fighter, long Damage)> ByFighter { get; } = new();

    public double TimeToKillSeconds =>
        LastHit > FirstHit ? (LastHit - FirstHit).TotalSeconds : 0;
}
