using TriuneLogParser.Core.Model;

namespace TriuneLogParser.Core.Aggregation;

/// <summary>
/// Turns encounters into per-fighter, per-source breakdowns. Pet output is folded into
/// the owning player as labelled sub-buckets; pets with no known owner stand alone.
/// Passing more than one encounter produces a merged (time-range) report.
/// </summary>
public static class EncounterAggregator
{
    public static EncounterReport Report(Encounter encounter) => Report(new[] { encounter });

    public static EncounterReport Report(IEnumerable<Encounter> encounters)
    {
        var list = encounters.ToList();
        var fighters = new Dictionary<string, FighterStats>(StringComparer.OrdinalIgnoreCase);

        FighterStats Get(string name) =>
            fighters.TryGetValue(name, out FighterStats? f)
                ? f
                : fighters[name] = new FighterStats { Name = name };

        double duration = 0;
        long totalDamage = 0, totalHealing = 0;
        DateTime start = DateTime.MaxValue, end = DateTime.MinValue;

        foreach (Encounter enc in list)
        {
            duration += enc.DurationSeconds;
            if (enc.Start < start) start = enc.Start;
            if (enc.End > end) end = enc.End;

            foreach (CombatEvent e in enc.Events)
            {
                switch (e.Action)
                {
                    case CombatAction.Damage:
                        totalDamage += ApplyDamage(e, Get);
                        break;
                    case CombatAction.Miss:
                        ApplyMiss(e, Get);
                        break;
                    case CombatAction.Heal:
                        totalHealing += ApplyHeal(e, Get);
                        break;
                    case CombatAction.Death:
                        ApplyDeath(e, Get);
                        break;
                }
            }
        }

        foreach (FighterStats f in fighters.Values)
        {
            Sort(f.DamageSources);
            Sort(f.DamageTakenSources);
            Sort(f.HealingSources);
        }

        List<FighterStats> Rank(Func<FighterStats, long> key) =>
            fighters.Values.Where(f => key(f) > 0).OrderByDescending(key).ToList();

        return new EncounterReport
        {
            EncounterIds = list.Select(e => e.Id).ToArray(),
            Start = start == DateTime.MaxValue ? default : start,
            End = end == DateTime.MinValue ? default : end,
            DurationSeconds = duration,
            TotalDamage = totalDamage,
            TotalHealing = totalHealing,
            DamageDone = Rank(f => f.DamageDone),
            DamageTaken = Rank(f => f.DamageTaken),
            Healing = Rank(f => f.HealingDone),
            Titles = list.Select(e => e.Title).ToArray(),
        };
    }

    private static long ApplyDamage(CombatEvent e, Func<string, FighterStats> get)
    {
        long counted = 0;

        // Outgoing: a friendly hits an NPC.
        if (e.Attacker is { } atk &&
            e.AttackerKind is EntityKind.Player or EntityKind.Pet &&
            e.TargetKind == EntityKind.Npc)
        {
            (string fighterName, string? petName) = ResolveFighter(atk, e);
            FighterStats f = get(fighterName);
            if (petName != null) f.Pets.Add(petName);

            f.DamageDone += e.Amount;
            if (e.Mechanic is DamageMechanic.Melee or DamageMechanic.MeleeSpecial)
            {
                f.SwingHits++;
                if (e.IsCritical) f.CritHits++;
            }

            Bucket(f.DamageSources, Category(e.Mechanic), SourceName(e), petName).Add(e.Amount, e.IsCritical);
            counted = e.Amount;
        }

        // Incoming: an NPC (or damage shield) hits a friendly.
        if (e.Target is { } tgt &&
            e.TargetKind is EntityKind.Player or EntityKind.Pet &&
            (e.AttackerKind == EntityKind.Npc || e.Attacker is null))
        {
            (string fighterName, string? petName) = ResolveFighter(tgt, e, useOwnerOfTarget: true);
            FighterStats f = get(fighterName);
            if (petName != null) f.Pets.Add(petName);
            f.DamageTaken += e.Amount;

            string src = e.Attacker ?? (e.Mechanic == DamageMechanic.DamageShield ? "damage shield" : "unknown");
            Bucket(f.DamageTakenSources, Category(e.Mechanic), src, petName).Add(e.Amount, e.IsCritical);
        }

        return counted;
    }

    private static void ApplyMiss(CombatEvent e, Func<string, FighterStats> get)
    {
        if (e.Attacker is { } atk &&
            e.AttackerKind is EntityKind.Player or EntityKind.Pet &&
            e.TargetKind == EntityKind.Npc)
        {
            (string fighterName, _) = ResolveFighter(atk, e);
            get(fighterName).SwingMisses++;
        }
    }

    private static long ApplyHeal(CombatEvent e, Func<string, FighterStats> get)
    {
        long counted = 0;
        if (e.Attacker is { } healer && e.AttackerKind is EntityKind.Player or EntityKind.Pet)
        {
            (string fighterName, string? petName) = ResolveFighter(healer, e);
            FighterStats f = get(fighterName);
            if (petName != null) f.Pets.Add(petName);
            f.HealingDone += e.Amount;
            Bucket(f.HealingSources, "Heal", e.SpellName ?? "heal", petName).Add(e.Amount, false);
            counted = e.Amount;
        }

        if (e.Target is { } target && e.TargetKind is EntityKind.Player or EntityKind.Pet)
            get(target).HealingReceived += e.Amount;

        return counted;
    }

    private static void ApplyDeath(CombatEvent e, Func<string, FighterStats> get)
    {
        if (e.Target is { } who && e.TargetKind is EntityKind.Player or EntityKind.Pet)
            get(who).Deaths++;
    }

    /// <summary>Map a raw actor to the fighter it should be credited to (pets → owner).</summary>
    private static (string fighter, string? petName) ResolveFighter(
        string actor, CombatEvent e, bool useOwnerOfTarget = false)
    {
        string? owner = useOwnerOfTarget ? null : e.AttackerOwner;

        bool actorIsPet = useOwnerOfTarget
            ? e.TargetKind == EntityKind.Pet
            : e.AttackerKind == EntityKind.Pet;

        if (actorIsPet && owner != null && !owner.Equals(actor, StringComparison.OrdinalIgnoreCase))
            return (owner, actor);

        return (actor, null);
    }

    private static SourceBucket Bucket(List<SourceBucket> list, string category, string name, string? petName)
    {
        foreach (SourceBucket b in list)
        {
            if (b.Category == category && b.Name == name &&
                string.Equals(b.PetName, petName, StringComparison.OrdinalIgnoreCase))
            {
                return b;
            }
        }

        var created = new SourceBucket { Category = category, Name = name, PetName = petName };
        list.Add(created);
        return created;
    }

    private static string SourceName(CombatEvent e) =>
        e.SpellName ?? e.Verb ?? e.Mechanic.ToString().ToLowerInvariant();

    private static string Category(DamageMechanic m) => m switch
    {
        DamageMechanic.Melee => "Melee",
        DamageMechanic.MeleeSpecial => "Melee Special",
        DamageMechanic.NonMelee => "Non-melee",
        DamageMechanic.DamageShield => "Damage Shield",
        _ => "Other",
    };

    private static void Sort(List<SourceBucket> buckets) =>
        buckets.Sort((a, b) => b.Total.CompareTo(a.Total));
}
