namespace TriuneLogParser.Core.Model;

/// <summary>
/// One parsed, meaningful line from the combat log. Produced by the parser, consumed
/// by the encounter builder and aggregation. Names are already resolved: "You" is
/// mapped to the logging character, and the pet <c>(Owner: X)</c> tag is stripped
/// into <see cref="AttackerOwner"/>.
/// </summary>
public sealed class CombatEvent
{
    public required DateTime Timestamp { get; init; }
    public required CombatAction Action { get; init; }

    /// <summary>Resolved attacker / healer / killer name. Null for e.g. environmental damage shields.</summary>
    public string? Attacker { get; init; }

    /// <summary>Resolved target / heal recipient / victim name.</summary>
    public string? Target { get; init; }

    /// <summary>
    /// Owner of <see cref="Attacker"/>. Seeded from a <c>(Owner: X)</c> tag at parse
    /// time; filled in from the pet registry by the encounter builder for untagged
    /// pet lines.
    /// </summary>
    public string? AttackerOwner { get; set; }

    /// <summary>Owner of <see cref="Target"/> when the target is a pet. Filled by the encounter builder.</summary>
    public string? TargetOwner { get; set; }

    /// <summary>Damage or heal amount. 0 for misses and (usually) deaths.</summary>
    public long Amount { get; init; }

    public DamageMechanic Mechanic { get; init; }

    /// <summary>The swing verb ("crush", "kick", "backstab") for melee, or "dot"/"dd" hints later.</summary>
    public string? Verb { get; init; }

    /// <summary>Parenthetical spell / ability name, e.g. "Distant Strike".</summary>
    public string? SpellName { get; init; }

    /// <summary>How a miss resolved: "miss", "parry", "dodge", "block", "riposte", "invulnerable", "rune".</summary>
    public string? MissReason { get; init; }

    /// <summary>Set by the critical-association pass when a nearby crit marker matches this hit.</summary>
    public bool IsCritical { get; set; }

    public int LineNumber { get; init; }
    public string RawLine { get; init; } = string.Empty;

    /// <summary>Best-effort classification of the attacker, filled by the encounter builder.</summary>
    public EntityKind AttackerKind { get; set; } = EntityKind.Unknown;

    /// <summary>Best-effort classification of the target, filled by the encounter builder.</summary>
    public EntityKind TargetKind { get; set; } = EntityKind.Unknown;

    public override string ToString() =>
        $"{Timestamp:HH:mm:ss} {Action} {Attacker}->{Target} {Amount} {Mechanic} {Verb ?? SpellName}";
}
