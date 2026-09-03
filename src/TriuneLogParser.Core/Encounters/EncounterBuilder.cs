using TriuneLogParser.Core.Model;
using TriuneLogParser.Core.Parsing;

namespace TriuneLogParser.Core.Encounters;

public sealed class EncounterOptions
{
    /// <summary>Quiet time after which an open fight is closed. EQLogParser uses ~45 s.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>
    /// After the last known mob dies, keep the fight open this long so a follow-up
    /// pull (adds already incoming) folds into the same encounter.
    /// </summary>
    public TimeSpan PostKillGrace { get; set; } = TimeSpan.FromSeconds(6);

    /// <summary>Ignore fights shorter than this with no kill (stray environmental ticks).</summary>
    public TimeSpan MinDuration { get; set; } = TimeSpan.Zero;
}

/// <summary>
/// EQLogParser-style fight detection. Feed it events in log order (call
/// <see cref="Advance"/> between events / on a timer so idle fights close), then
/// <see cref="Finish"/>. For batch input use <see cref="BuildAll"/>, which first does a
/// classification pass so named mobs aren't mistaken for players.
/// </summary>
public sealed class EncounterBuilder
{
    private readonly EncounterOptions _opt;
    private readonly RosterTracker _roster;
    private readonly List<Encounter> _completed = new();
    private Encounter? _current;
    private DateTime? _pendingCloseAt;
    private int _nextId = 1;
    private string? _zone;

    public EncounterBuilder(RosterTracker roster, EncounterOptions? options = null)
    {
        _roster = roster;
        _opt = options ?? new EncounterOptions();
    }

    public RosterTracker Roster => _roster;
    public IReadOnlyList<Encounter> Completed => _completed;
    public Encounter? Current => _current;

    /// <summary>One-shot build over a full event list (+ optional zone changes).</summary>
    public IReadOnlyList<Encounter> BuildAll(
        IEnumerable<CombatEvent> events,
        IEnumerable<ZoneChange>? zoneChanges = null)
    {
        var ordered = events.OrderBy(e => e.Timestamp).ThenBy(e => e.LineNumber).ToList();

        // Pass 1: stabilise entity classification.
        foreach (CombatEvent e in ordered)
            _roster.Observe(e);

        // Pass 2: walk the timeline.
        var zones = (zoneChanges ?? Enumerable.Empty<ZoneChange>())
            .OrderBy(z => z.Timestamp).ToList();
        int zi = 0;

        foreach (CombatEvent e in ordered)
        {
            while (zi < zones.Count && zones[zi].Timestamp <= e.Timestamp)
                HandleZone(zones[zi++]);

            Advance(e.Timestamp);
            Handle(e);
        }

        while (zi < zones.Count)
            HandleZone(zones[zi++]);

        Finish();
        return _completed;
    }

    public void HandleZone(ZoneChange zone)
    {
        _zone = zone.Zone;
        if (_current != null)
            Close(EncounterEndReason.ZoneChange, _current.LastActivity);
    }

    /// <summary>Close fights that have gone quiet as of <paramref name="now"/>.</summary>
    public void Advance(DateTime now)
    {
        if (_current == null)
            return;

        if (_pendingCloseAt is { } due && now >= due)
        {
            Close(EncounterEndReason.AllMobsDead, _current.LastActivity);
            return;
        }

        if (now - _current.LastActivity > _opt.IdleTimeout)
            Close(EncounterEndReason.Idle, _current.LastActivity);
    }

    public void Handle(CombatEvent e)
    {
        e.AttackerKind = _roster.Classify(e.Attacker);
        e.TargetKind = _roster.Classify(e.Target);

        // Untagged pet lines ("Nixalir crushes ...") carry no (Owner:) tag — fill the
        // owner in from the registry so aggregation folds them into the owner.
        if (e.AttackerOwner == null && e.AttackerKind == EntityKind.Pet && e.Attacker != null)
            e.AttackerOwner = _roster.OwnerOf(e.Attacker);

        switch (e.Action)
        {
            case CombatAction.Damage or CombatAction.Miss:
                HandleCombat(e);
                break;

            case CombatAction.Death:
                HandleDeath(e);
                break;

            case CombatAction.Heal:
                // Attach in-fight healing but don't let it start or sustain a fight.
                if (_current != null)
                {
                    _current.Events.Add(e);
                    if (e.Timestamp > _current.End)
                        _current.End = e.Timestamp;
                }
                break;
        }
    }

    private void HandleCombat(CombatEvent e)
    {
        bool attackerFriendly = e.AttackerKind is EntityKind.Player or EntityKind.Pet;
        bool targetFriendly = e.TargetKind is EntityKind.Player or EntityKind.Pet;
        bool attackerNpc = e.AttackerKind == EntityKind.Npc;
        bool targetNpc = e.TargetKind == EntityKind.Npc;

        // Damage shields have no attacker; credit the fight if the victim is an NPC.
        bool isEngage =
            (attackerFriendly && targetNpc) ||
            (attackerNpc && targetFriendly) ||
            (e.Mechanic == DamageMechanic.DamageShield && targetNpc);

        if (!isEngage)
            return;

        _current ??= StartEncounter(e.Timestamp);

        string? npc = targetNpc ? e.Target : attackerNpc ? e.Attacker : null;
        bool newMob = npc != null && !_current.NpcDamage.ContainsKey(npc);

        _current.Events.Add(e);
        _current.LastActivity = e.Timestamp;
        if (e.Timestamp > _current.End)
            _current.End = e.Timestamp;

        if (npc != null)
        {
            _current.NpcDamage.TryGetValue(npc, out long soFar);
            if (e.Action == CombatAction.Damage && targetNpc)
                _current.NpcDamage[npc] = soFar + e.Amount;
            else
                _current.NpcDamage.TryAdd(npc, 0);
        }

        // A fresh mob entering after a kill means the pull continues.
        if (newMob && _pendingCloseAt != null)
            _pendingCloseAt = null;
    }

    private void HandleDeath(CombatEvent e)
    {
        if (_current == null)
            return;

        _current.Events.Add(e);
        if (e.Timestamp > _current.End)
            _current.End = e.Timestamp;

        if (e.TargetKind == EntityKind.Npc && e.Target is { } mob)
        {
            _current.NpcsKilled.Add(mob);
            _current.NpcDamage.TryAdd(mob, 0);

            bool allDead = _current.NpcDamage.Keys.All(_current.NpcsKilled.Contains);
            if (allDead)
                _pendingCloseAt = e.Timestamp + _opt.PostKillGrace;
        }
        else if (e.TargetKind is EntityKind.Player or EntityKind.Pet && e.Target is { } who)
        {
            _current.PlayerDeaths.Add(who);
        }
    }

    private Encounter StartEncounter(DateTime start)
    {
        _pendingCloseAt = null;
        return _current = new Encounter
        {
            Id = _nextId++,
            Start = start,
            End = start,
            LastActivity = start,
            Zone = _zone,
        };
    }

    public void Finish()
    {
        if (_current != null)
            Close(EncounterEndReason.LogEnd, _current.LastActivity);
    }

    private void Close(EncounterEndReason reason, DateTime end)
    {
        if (_current == null)
            return;

        Encounter enc = _current;
        _current = null;
        _pendingCloseAt = null;

        enc.End = end < enc.Start ? enc.Start : end;
        enc.IsActive = false;
        enc.EndReason = reason;

        bool killedSomething = enc.NpcsKilled.Count > 0;
        if (!killedSomething && enc.Duration < _opt.MinDuration)
            return;

        _completed.Add(enc);
    }
}
