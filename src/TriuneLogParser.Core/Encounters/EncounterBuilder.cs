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

    /// <summary>
    /// When true, a fight is not closed by the idle timeout or a zone change during the
    /// <see cref="DeathRecoveryWindow"/> after the logging character dies — a corpse run
    /// back to the raid instance (release to bind → zone → run in → resume) stays one
    /// encounter instead of fragmenting on every death.
    /// </summary>
    public bool BridgeDeathRecovery { get; set; } = true;

    /// <summary>How long after a self-death the fight is held open for a corpse run.</summary>
    public TimeSpan DeathRecoveryWindow { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Build options from a single "rest period" (0–300 s).
    /// <para>
    /// 0 = aggressive per-pull: a fight ends the instant every mob it touched is dead,
    /// so trash grinds break apart cleanly (raids will fragment — that's the trade-off).
    /// </para>
    /// <para>
    /// &gt; 0 = session/event style: a fight ends only after this many seconds with no
    /// combat, or a zone change. A phased raid where bosses die at different times but
    /// combat never stops stays one encounter.
    /// </para>
    /// </summary>
    public static EncounterOptions ForRestPeriod(int seconds)
    {
        seconds = Math.Clamp(seconds, 0, 300);
        bool perPull = seconds == 0;
        return new EncounterOptions
        {
            SplitOnAllMobsDead = perPull,
            ReengageWindow = perPull ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds),
            IdleTimeout = TimeSpan.FromSeconds(perPull ? 6 : seconds),
            MaxDuration = perPull ? TimeSpan.FromMinutes(10) : TimeSpan.FromMinutes(20),
            BridgeDeathRecovery = !perPull,
        };
    }
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

    /// <summary>A fight parked mid-corpse-run: zoned away after a self-death, awaiting re-engage.</summary>
    private Encounter? _suspended;

    /// <summary>Fights are held open (idle / zone) until this time after the logging character dies.</summary>
    private DateTime _recoveryUntil = DateTime.MinValue;

    /// <summary>Set by <see cref="ForceSplit"/>: the next engage must start a brand-new fight.</summary>
    private bool _splitBarrier;

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

    /// <summary>One-shot build over a full event list (+ optional zone changes / split markers).</summary>
    public IReadOnlyList<Encounter> BuildAll(
        IEnumerable<CombatEvent> events,
        IEnumerable<ZoneChange>? zoneChanges = null,
        IEnumerable<DateTime>? splitPoints = null)
    {
        var ordered = events.OrderBy(e => e.Timestamp).ThenBy(e => e.LineNumber).ToList();

        // Pass 1: stabilise entity classification.
        foreach (CombatEvent e in ordered)
            _roster.Observe(e);

        // Pass 2: walk the timeline.
        var zones = (zoneChanges ?? Enumerable.Empty<ZoneChange>())
            .OrderBy(z => z.Timestamp).ToList();
        var splits = (splitPoints ?? Enumerable.Empty<DateTime>()).OrderBy(t => t).ToList();
        int zi = 0, si = 0;

        foreach (CombatEvent e in ordered)
        {
            while (zi < zones.Count && zones[zi].Timestamp <= e.Timestamp)
                HandleZone(zones[zi++]);

            // A split marker takes effect before the first event at or after its time.
            while (si < splits.Count && splits[si] <= e.Timestamp)
                ForceSplit(splits[si++]);

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

        if (_current == null)
            return;

        // Mid-corpse-run: keep the fight parked so it resumes when we run back in.
        if (_opt.BridgeDeathRecovery && zone.Timestamp <= _recoveryUntil)
        {
            _suspended = _current;
            _current = null;
            return;
        }

        Close(EncounterEndReason.ZoneChange, _current.LastActivity);
    }

    /// <summary>Close fights that have gone quiet as of <paramref name="now"/>.</summary>
    public void Advance(DateTime now)
    {
        // A parked fight that nobody came back to within the recovery window is done.
        if (_suspended != null && now - _suspended.LastActivity > _opt.DeathRecoveryWindow)
            FlushSuspended();

        if (_current == null)
            return;

        // During a corpse run the character is dead on the ground — don't let the
        // ordinary idle gap close the fight the raid is still fighting.
        TimeSpan idle = _opt.BridgeDeathRecovery && now <= _recoveryUntil
            ? (_opt.IdleTimeout > _opt.DeathRecoveryWindow ? _opt.IdleTimeout : _opt.DeathRecoveryWindow)
            : _opt.IdleTimeout;

        if (now - _current.LastActivity > idle)
        {
            Close(EncounterEndReason.Idle, _current.LastActivity);
            return;
        }

        if (_opt.MaxDuration > TimeSpan.Zero && now - _current.Start >= _opt.MaxDuration)
            Close(EncounterEndReason.TimeCap, now);
    }

    private void FlushSuspended()
    {
        if (_suspended == null)
            return;

        Encounter enc = _suspended;
        _suspended = null;
        enc.IsActive = false;
        enc.EndReason = EncounterEndReason.ZoneChange;

        if (enc.NpcsKilled.Count > 0 || enc.Duration >= _opt.MinDuration)
            _completed.Add(enc);
    }

    /// <summary>Revive a corpse-run fight when combat resumes within the recovery window.</summary>
    private Encounter? ResumeSuspended(DateTime now)
    {
        if (_suspended == null)
            return null;

        if (now - _suspended.LastActivity > _opt.DeathRecoveryWindow)
        {
            FlushSuspended();
            return null;
        }

        Encounter enc = _suspended;
        _suspended = null;
        enc.IsActive = true;
        enc.EndReason = EncounterEndReason.None;
        return _current = enc;
    }

    public void Handle(CombatEvent e)
    {
        e.AttackerKind = _roster.Classify(e.Attacker);
        e.TargetKind = _roster.Classify(e.Target);

        // Untagged pet lines ("Nixalir crushes ...") carry no (Owner:) tag — fill the
        // owner in from the registry so aggregation folds them into the owner.
        if (e.AttackerOwner == null && e.AttackerKind == EntityKind.Pet && e.Attacker != null)
            e.AttackerOwner = _roster.OwnerOf(e.Attacker);
        if (e.TargetOwner == null && e.TargetKind == EntityKind.Pet && e.Target != null)
            e.TargetOwner = _roster.OwnerOf(e.Target);

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

        if (_splitBarrier)
        {
            _splitBarrier = false;
            _current ??= StartEncounter(e.Timestamp);
        }

        _current ??= ResumeSuspended(e.Timestamp) ?? ReopenRecent(e.Timestamp) ?? StartEncounter(e.Timestamp);

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

            // Only mobs that friendlies actually damaged gate the close — a mob that
            // merely swung at us once shouldn't hold a per-pull encounter open.
            bool allDamagedDead = _current.NpcDamage
                .Where(kv => kv.Value > 0)
                .All(kv => _current.NpcsKilled.Contains(kv.Key));

            if (_opt.SplitOnAllMobsDead && allDamagedDead)
                Close(EncounterEndReason.AllMobsDead, e.Timestamp);
        }
        else if (e.TargetKind == EntityKind.Player && e.Target is { } who)
        {
            _current.PlayerDeaths.Add(who);

            if (_opt.BridgeDeathRecovery && _roster.Self is { } self &&
                who.Equals(self, StringComparison.OrdinalIgnoreCase))
            {
                _recoveryUntil = e.Timestamp + _opt.DeathRecoveryWindow;
            }
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

    /// <summary>
    /// End the fight in progress right now (user "split fight" button, or a saved split
    /// marker). The next engage always starts a fresh encounter — the re-engage and
    /// corpse-run bridges are cancelled so the boundary is exactly where asked.
    /// </summary>
    public void ForceSplit(DateTime at)
    {
        _recoveryUntil = DateTime.MinValue;
        FlushSuspended();

        if (_current != null)
        {
            DateTime end = at < _current.LastActivity ? _current.LastActivity : at;
            Close(EncounterEndReason.Manual, end);
        }

        _splitBarrier = true;
    }

    public void Finish()
    {
        FlushSuspended();
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
