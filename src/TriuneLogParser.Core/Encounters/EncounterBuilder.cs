using TriuneLogParser.Core.Model;
using TriuneLogParser.Core.Parsing;

namespace TriuneLogParser.Core.Encounters;

public sealed class EncounterOptions
{
    /// <summary>Quiet time after which an open fight is closed. EQLogParser uses ~45 s.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>Drop fights shorter than this that killed nothing (stray post-death DoT ticks, lone damage shields).</summary>
    public TimeSpan MinDuration { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Hard cap on encounter length. A non-stop grind that never pauses long enough to
    /// split is chopped into chapters of at most this long so the list stays useful.
    /// </summary>
    public TimeSpan MaxDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// When true (default), a fight provisionally ends the moment every mob it has
    /// touched is dead (EQLogParser "per-pull" style). When false, only a quiet gap of
    /// <see cref="IdleTimeout"/> or a zone change ends it.
    /// </summary>
    public bool SplitOnAllMobsDead { get; set; } = true;

    /// <summary>
    /// If new combat starts within this long of a fight that ended on a kill (same zone),
    /// it re-opens that fight instead of starting a new one — so a rapid chain-pull is
    /// one encounter but a real pause splits. Set to zero for strict per-pull.
    /// </summary>
    public TimeSpan ReengageWindow { get; set; } = TimeSpan.FromSeconds(12);
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
    private int _nextId = 1;
    private string? _zone;

    public EncounterBuilder(RosterTracker roster, EncounterOptions? options = null)
    {
        _roster = roster;
        _opt = options ?? new EncounterOptions();
        _roster.PetOwnerLearned += RefoldOpenEncounter;
    }

    /// <summary>
    /// When a pet's owner becomes known mid-fight, re-attribute its earlier events in
    /// the still-open encounter so the live breakdown folds them into the owner.
    /// </summary>
    private void RefoldOpenEncounter(string pet, string owner)
    {
        if (_current == null)
            return;

        foreach (CombatEvent e in _current.Events)
        {
            if (e.AttackerOwner == null && string.Equals(e.Attacker, pet, StringComparison.OrdinalIgnoreCase))
            {
                e.AttackerKind = EntityKind.Pet;
                e.AttackerOwner = owner;
            }
        }
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

        if (now - _current.LastActivity > _opt.IdleTimeout)
        {
            Close(EncounterEndReason.Idle, _current.LastActivity);
            return;
        }

        if (_opt.MaxDuration > TimeSpan.Zero && now - _current.Start >= _opt.MaxDuration)
            Close(EncounterEndReason.TimeCap, now);
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

        _current ??= ReopenRecent(e.Timestamp) ?? StartEncounter(e.Timestamp);

        string? npc = targetNpc ? e.Target : attackerNpc ? e.Attacker : null;

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

            if (_opt.SplitOnAllMobsDead &&
                _current.NpcDamage.Keys.All(_current.NpcsKilled.Contains))
            {
                Close(EncounterEndReason.AllMobsDead, e.Timestamp);
            }
        }
        else if (e.TargetKind is EntityKind.Player or EntityKind.Pet && e.Target is { } who)
        {
            _current.PlayerDeaths.Add(who);
        }
    }

    /// <summary>Re-open the most recent kill-closed fight if we re-engaged almost immediately.</summary>
    private Encounter? ReopenRecent(DateTime now)
    {
        if (_opt.ReengageWindow <= TimeSpan.Zero || _completed.Count == 0)
            return null;

        Encounter last = _completed[^1];
        if (last.EndReason != EncounterEndReason.AllMobsDead ||
            last.Zone != _zone ||
            now - last.End > _opt.ReengageWindow)
        {
            return null;
        }

        _completed.RemoveAt(_completed.Count - 1);
        last.IsActive = true;
        last.EndReason = EncounterEndReason.None;
        return _current = last;
    }

    private Encounter StartEncounter(DateTime start)
    {
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

        enc.End = end < enc.Start ? enc.Start : end;
        enc.IsActive = false;
        enc.EndReason = reason;

        bool killedSomething = enc.NpcsKilled.Count > 0;
        if (!killedSomething && enc.Duration < _opt.MinDuration)
            return;

        _completed.Add(enc);
    }
}
