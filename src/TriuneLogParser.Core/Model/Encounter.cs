namespace TriuneLogParser.Core.Model;

/// <summary>
/// A single fight: a contiguous stretch of combat activity, possibly against several
/// mobs (a multi-mob pull is one encounter). Holds the raw events; per-player / per-source
/// numbers are produced on demand by the aggregation layer.
/// </summary>
public sealed class Encounter
{
    public int Id { get; init; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string? Zone { get; set; }

    /// <summary>Last time a genuine engage event (damage/miss vs. an NPC) landed.</summary>
    public DateTime LastActivity { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Why the fight ended.</summary>
    public EncounterEndReason EndReason { get; set; } = EncounterEndReason.None;

    public List<CombatEvent> Events { get; } = new();

    /// <summary>NPC names that took damage in this fight, and how much (for titling / adds).</summary>
    public Dictionary<string, long> NpcDamage { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> NpcsKilled { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Player names that died during the fight.</summary>
    public List<string> PlayerDeaths { get; } = new();

    public TimeSpan Duration => End - Start;

    /// <summary>Seconds of fight, floored at 1 to avoid divide-by-zero DPS.</summary>
    public double DurationSeconds => Math.Max(1.0, (End - Start).TotalSeconds);

    /// <summary>A readable name: the hardest-hit mob, plus "+N" when there were adds.</summary>
    public string Title
    {
        get
        {
            if (NpcDamage.Count == 0)
                return "Unknown";

            string primary = NpcDamage.OrderByDescending(kv => kv.Value).First().Key;
            int adds = NpcDamage.Count - 1;
            return adds > 0 ? $"{primary} +{adds}" : primary;
        }
    }

    public override string ToString() =>
        $"#{Id} {Start:HH:mm:ss} {Title} ({Duration.TotalSeconds:0}s)";
}

public enum EncounterEndReason
{
    None = 0,
    AllMobsDead,
    Idle,
    ZoneChange,
    TimeCap,
    LogEnd,

    /// <summary>The user pressed "split fight" (live), or a saved split marker for this moment.</summary>
    Manual,
}
