using TriuneLogParser.Core.Model;

namespace TriuneLogParser.Core.Parsing;

/// <summary>
/// EverQuest logs the "critical hit"/"critical blast" marker on its own line, adjacent
/// to (before or after) the hit it belongs to. This buffers recent hits and recent
/// markers and flags a hit as critical when a marker matches its amount within a short
/// window.
/// </summary>
public sealed class CriticalAssociator
{
    private readonly TimeSpan _window;
    private readonly LinkedList<CombatEvent> _recentHits = new();
    private readonly LinkedList<CritMarker> _pendingMarkers = new();

    public CriticalAssociator(TimeSpan? window = null) =>
        _window = window ?? TimeSpan.FromSeconds(2);

    /// <summary>Feed every damage event here in log order.</summary>
    public void Observe(CombatEvent e)
    {
        if (e.Action != CombatAction.Damage || e.Amount <= 0)
            return;

        Trim(e.Timestamp);

        for (LinkedListNode<CritMarker>? node = _pendingMarkers.First; node != null; node = node.Next)
        {
            if (Matches(node.Value, e))
            {
                e.IsCritical = true;
                _pendingMarkers.Remove(node);
                return;
            }
        }

        _recentHits.AddLast(e);
    }

    /// <summary>Feed every crit marker here in log order.</summary>
    public void Observe(CritMarker marker)
    {
        Trim(marker.Timestamp);

        for (LinkedListNode<CombatEvent>? node = _recentHits.Last; node != null; node = node.Previous)
        {
            if (Matches(marker, node.Value))
            {
                node.Value.IsCritical = true;
                _recentHits.Remove(node);
                return;
            }
        }

        _pendingMarkers.AddLast(marker);
    }

    private static bool Matches(CritMarker marker, CombatEvent hit)
    {
        if (hit.Amount != marker.Amount)
            return false;

        if (marker.SpellName is { Length: > 0 })
            return string.Equals(marker.SpellName, hit.SpellName, StringComparison.OrdinalIgnoreCase);

        // Bare "scores a critical hit!" → a melee swing.
        return hit.Mechanic is DamageMechanic.Melee or DamageMechanic.MeleeSpecial;
    }

    private void Trim(DateTime now)
    {
        while (_recentHits.First is { } h && now - h.Value.Timestamp > _window)
            _recentHits.RemoveFirst();
        while (_pendingMarkers.First is { } m && now - m.Value.Timestamp > _window)
            _pendingMarkers.RemoveFirst();
    }
}
