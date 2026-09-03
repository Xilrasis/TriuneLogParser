using TriuneLogParser.Core.Model;

namespace TriuneLogParser.Core.Parsing;

/// <summary>A standalone "critical hit" / "critical blast" marker line.</summary>
public sealed record CritMarker(DateTime Timestamp, long Amount, string? SpellName, bool IsSelf, string RawLine);

/// <summary>A zone transition ("You have entered X").</summary>
public sealed record ZoneChange(DateTime Timestamp, string Zone);

/// <summary>Result of parsing a single log line.</summary>
public readonly struct ParseOutcome
{
    public CombatEvent? Event { get; private init; }
    public CritMarker? Crit { get; private init; }
    public ZoneChange? Zone { get; private init; }

    /// <summary>The line looked damage-related but no rule claimed it.</summary>
    public bool UnparsedDamageLike { get; private init; }

    public bool Handled => Event != null || Crit != null || Zone != null;

    public static ParseOutcome None => default;
    public static ParseOutcome FromEvent(CombatEvent e) => new() { Event = e };
    public static ParseOutcome FromCrit(CritMarker c) => new() { Crit = c };
    public static ParseOutcome FromZone(ZoneChange z) => new() { Zone = z };
    public static ParseOutcome Unparsed() => new() { UnparsedDamageLike = true };
}
