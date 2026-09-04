namespace TriuneLogParser.Core.Encounters;

/// <summary>What a marker does to the timeline.</summary>
public enum MarkerKind
{
    /// <summary>End the encounter in progress here; the next combat starts a fresh one.</summary>
    Split = 0,
}

/// <summary>
/// A user-placed instruction pinned to a moment in the log timeline. Placed live with
/// the "split fight" button and persisted beside the log (see
/// <see cref="Config.MarkerStore"/>) so re-parses — retro parse, app restart, the CLI —
/// reproduce the same encounter boundaries.
/// </summary>
public sealed record EncounterMarker(DateTime Timestamp, MarkerKind Kind = MarkerKind.Split);
