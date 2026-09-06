using System.Collections.ObjectModel;
using TriuneLogParser.Core.Aggregation;

namespace TriuneLogParser.App.ViewModels;

public enum Metric { DamageDone, DamageTaken, Healing, Mobs, Defenses }

/// <summary>
/// One row in the damage-meter breakdown. Rows form a tree (fighter → group → leaf);
/// the view renders a flattened, expand-aware list of them.
/// </summary>
public sealed class BreakdownNode : ObservableObject
{
    private bool _isExpanded;

    public required string Label { get; init; }
    public string Sub { get; init; } = "";
    public int Depth { get; init; }

    public long Value { get; init; }
    public string ValueText { get; init; } = "";
    public string RateText { get; init; } = "";
    public string ShareText { get; init; } = "";
    public string DetailText { get; init; } = "";

    /// <summary>0..1 width of the bar, relative to the biggest row at this level.</summary>
    public double BarFraction { get; init; }

    /// <summary>Bar colour role: "top" (fighter), "group", "leaf".</summary>
    public string Kind { get; init; } = "leaf";

    public List<BreakdownNode> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (Set(ref _isExpanded, value)) { Raise(nameof(Glyph)); ExpandedChanged?.Invoke(); } }
    }

    public string Glyph => HasChildren ? (IsExpanded ? "▼" : "▶") : "";
    public System.Windows.Thickness IndentThickness => new(Depth * 22, 0, 0, 0);

    public event Action? ExpandedChanged;
}

public static class BreakdownTreeBuilder
{
    /// <summary>Build the fighter/group/leaf tree for one metric of a report.</summary>
    public static List<BreakdownNode> Build(
        EncounterReport report, Metric metric, double seconds, Func<string, string>? classLabel = null)
    {
        if (metric == Metric.Mobs)
            return BuildMobs(report);

        if (metric == Metric.Defenses)
            return BuildDefenses(report, seconds, classLabel);

        IReadOnlyList<FighterStats> fighters = metric switch
        {
            Metric.DamageDone => report.DamageDone,
            Metric.DamageTaken => report.DamageTaken,
            _ => report.Healing,
        };

        long Val(FighterStats f) => metric switch
        {
            Metric.DamageDone => f.DamageDone,
            Metric.DamageTaken => f.DamageTaken,
            _ => f.HealingDone,
        };

        IReadOnlyList<SourceBucket> Buckets(FighterStats f) => metric switch
        {
            Metric.DamageDone => f.DamageSources,
            Metric.DamageTaken => f.DamageTakenSources,
            _ => f.HealingSources,
        };

        long topValue = fighters.Count > 0 ? Val(fighters[0]) : 0;
        var nodes = new List<BreakdownNode>();

        foreach (FighterStats f in fighters)
        {
            long fv = Val(f);
            if (fv <= 0)
                continue;

            string classes = classLabel?.Invoke(f.Name) ?? "";
            string sub = metric == Metric.DamageDone ? $"crit {f.CritRate:P0} · acc {f.Accuracy:P0}" : "";
            if (classes.Length > 0)
                sub = sub.Length > 0 ? $"{classes} · {sub}" : classes;

            var fighter = new BreakdownNode
            {
                Label = f.Name,
                Sub = sub,
                Depth = 0,
                Kind = "top",
                Value = fv,
                ValueText = fv.ToString("N0"),
                RateText = seconds > 0 ? $"{fv / seconds:N0}/s" : "",
                ShareText = topValue > 0 ? ((double)fv / SumAll(fighters, Val)).ToString("P0") : "",
                DetailText = f.Deaths > 0 ? $"{f.Deaths} death(s)" : "",
                BarFraction = topValue > 0 ? (double)fv / topValue : 0,
            };

            foreach (BreakdownNode child in BuildChildren(Buckets(f), fv, childDepth: 1))
                fighter.Children.Add(child);

            nodes.Add(fighter);
        }

        return nodes;
    }

    private static List<BreakdownNode> BuildMobs(EncounterReport report)
    {
        long top = report.Mobs.Count > 0 ? report.Mobs[0].DamageTaken : 0;
        var nodes = new List<BreakdownNode>();

        foreach (MobStats m in report.Mobs)
        {
            string ttk = m.TimeToKillSeconds > 0
                ? $"{TimeSpan.FromSeconds(m.TimeToKillSeconds):m\\:ss} to kill"
                : "";
            string kill = m.LastKiller is { Length: > 0 } ? $"killed by {m.LastKiller}" : "not killed";

            var node = new BreakdownNode
            {
                Label = m.Name,
                Sub = m.Deaths > 1 ? $"×{m.Deaths}" : "",
                Depth = 0,
                Kind = "top",
                Value = m.DamageTaken,
                ValueText = m.DamageTaken.ToString("N0"),
                ShareText = "",
                DetailText = string.Join(" · ", new[] { kill, ttk }.Where(s => s.Length > 0)),
                BarFraction = top > 0 ? (double)m.DamageTaken / top : 0,
            };

            foreach (MobFighterDamage bf in m.ByFighter)
            {
                var fighterNode = new BreakdownNode
                {
                    Label = bf.Fighter,
                    Depth = 1,
                    Kind = "group",
                    Value = bf.Damage,
                    ValueText = bf.Damage.ToString("N0"),
                    ShareText = m.DamageTaken > 0 ? ((double)bf.Damage / m.DamageTaken).ToString("P0") : "",
                    BarFraction = m.DamageTaken > 0 ? (double)bf.Damage / m.DamageTaken : 0,
                };

                foreach (BreakdownNode child in BuildChildren(bf.Sources, bf.Damage, childDepth: 2))
                    fighterNode.Children.Add(child);

                node.Children.Add(fighterNode);
            }

            nodes.Add(node);
        }

        return nodes;
    }

    /// <summary>
    /// Per-defender incoming-attack / avoidance breakdown. Top row = defender (total damage
    /// taken, overall avoid rate); children = one row per incoming attack type with its
    /// min/avg/max hit and per-type miss / parry / dodge / block / riposte rates.
    /// </summary>
    private static List<BreakdownNode> BuildDefenses(
        EncounterReport report, double seconds, Func<string, string>? classLabel)
    {
        long top = report.Defenses.Count > 0 ? report.Defenses[0].Damage : 0;
        var nodes = new List<BreakdownNode>();

        foreach (DefenseStats d in report.Defenses)
        {
            string classes = classLabel?.Invoke(d.Name) ?? "";
            string avoidBits = AvoidBits(d.Misses, d.Parries, d.Dodges, d.Blocks, d.Ripostes, d.Absorbs);
            string deaths = d.Deaths > 0 ? $" · {d.Deaths} death(s)" : "";
            string nm = d.Swings > d.MeleeSwings ? $" · +{d.Swings - d.MeleeSwings:N0} non-melee hits" : "";

            var node = new BreakdownNode
            {
                Label = d.Name,
                Sub = classes,
                Depth = 0,
                Kind = "top",
                Value = d.Damage,
                ValueText = d.Damage.ToString("N0"),
                ShareText = d.MeleeSwings > 0 ? $"{d.AvoidRate:P0} av" : "",
                RateText = seconds > 0 ? $"{d.Damage / seconds:N0}/s" : "",
                DetailText = $"{d.MeleeSwings:N0} melee swings · {d.HitRate:P0} landed"
                             + (avoidBits.Length > 0 ? $" · {avoidBits}" : "") + nm + deaths,
                BarFraction = top > 0 ? (double)d.Damage / top : 0,
            };

            long typeTop = d.Attacks.Count > 0 ? d.Attacks.Max(a => a.Swings) : 0;
            foreach (IncomingAttackStats a in d.Attacks)
            {
                var typeNode = new BreakdownNode
                {
                    Label = a.Type,
                    Sub = a.Category,
                    Depth = 1,
                    Kind = "group",
                    Value = a.Damage,
                    ValueText = $"{a.Swings:N0} sw",
                    ShareText = $"{a.HitRate:P0} hit",
                    RateText = a.IsMelee ? $"{a.AvoidRate:P0} av" : "",
                    DetailText = a.Hits > 0
                        ? $"avg {a.Average:N0} · min {a.MinHit:N0} · max {a.Max:N0}"
                          + (a.Crits > 0 ? $" · crit {a.CritRate:P0}" : "")
                        : "no hits landed",
                    BarFraction = typeTop > 0 ? (double)a.Swings / typeTop : 0,
                };

                typeNode.Children.Add(AvoidLeaf("landed", a.Hits, a.Swings, a.HitRate,
                    a.Hits > 0 ? $"avg {a.Average:N0} · min {a.MinHit:N0} · max {a.Max:N0}"
                        + (a.Crits > 0 ? $" · crit {a.CritRate:P0} ({a.Crits:N0})" : "" ) : ""));
                if (a.Misses > 0)
                    typeNode.Children.Add(AvoidLeaf("missed", a.Misses, a.Swings, a.MissRate, ""));
                if (a.Parries > 0)
                    typeNode.Children.Add(AvoidLeaf("parried", a.Parries, a.Swings, a.ParryRate, ""));
                if (a.Dodges > 0)
                    typeNode.Children.Add(AvoidLeaf("dodged", a.Dodges, a.Swings, a.DodgeRate, ""));
                if (a.Blocks > 0)
                    typeNode.Children.Add(AvoidLeaf("blocked", a.Blocks, a.Swings, a.BlockRate, ""));
                if (a.Ripostes > 0)
                    typeNode.Children.Add(AvoidLeaf("riposted", a.Ripostes, a.Swings, a.RiposteRate, ""));
                if (a.Absorbs > 0)
                    typeNode.Children.Add(AvoidLeaf("rune / absorb", a.Absorbs, a.Swings,
                        a.Swings > 0 ? (double)a.Absorbs / a.Swings : 0, "mitigation amount not in the log"));
                if (a.Invulnerables > 0)
                    typeNode.Children.Add(AvoidLeaf("invulnerable", a.Invulnerables, a.Swings,
                        a.Swings > 0 ? (double)a.Invulnerables / a.Swings : 0, ""));

                node.Children.Add(typeNode);
            }

            nodes.Add(node);
        }

        return nodes;
    }

    private static BreakdownNode AvoidLeaf(string label, long count, long swings, double rate, string detail) => new()
    {
        Label = label,
        Depth = 2,
        Kind = "leaf",
        Value = count,
        ValueText = count.ToString("N0"),
        ShareText = swings > 0 ? rate.ToString("P0") : "",
        DetailText = detail,
        BarFraction = rate,
    };

    private static string AvoidBits(long miss, long parry, long dodge, long block, long riposte, long absorb)
    {
        var parts = new List<string>(6);
        if (miss > 0) parts.Add($"miss {miss:N0}");
        if (parry > 0) parts.Add($"parry {parry:N0}");
        if (dodge > 0) parts.Add($"dodge {dodge:N0}");
        if (block > 0) parts.Add($"block {block:N0}");
        if (riposte > 0) parts.Add($"riposte {riposte:N0}");
        if (absorb > 0) parts.Add($"rune {absorb:N0}");
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// The source subtree under a total (a fighter, or a fighter's damage into one mob).
    /// <paramref name="childDepth"/> is the tree depth of the group/leaf rows produced.
    /// </summary>
    private static IEnumerable<BreakdownNode> BuildChildren(
        IReadOnlyList<SourceBucket> buckets, long parentTotal, int childDepth)
    {
        // Split into: this fighter's own buckets vs. each pet's buckets.
        var own = buckets.Where(b => b.PetName is null).ToList();
        var byPet = buckets.Where(b => b.PetName is not null)
            .GroupBy(b => b.PetName!, StringComparer.OrdinalIgnoreCase);

        // Melee (white + specials) collapse into one expandable group.
        var melee = own.Where(IsMelee).ToList();
        var nonMelee = own.Where(b => !IsMelee(b)).ToList();

        var result = new List<BreakdownNode>();

        if (melee.Count > 0)
            result.Add(GroupNode("Melee", melee, parentTotal, depth: childDepth));

        foreach (SourceBucket b in nonMelee.OrderByDescending(b => b.Total))
            result.Add(LeafNode(b.Name, b.Category, b, parentTotal, depth: childDepth));

        foreach (var pet in byPet)
        {
            var items = pet.ToList();
            long petTotal = items.Sum(b => b.Total);
            var petNode = new BreakdownNode
            {
                Label = StripOwnerPrefix(pet.Key),
                Sub = "pet",
                Depth = childDepth,
                Kind = "group",
                Value = petTotal,
                ValueText = petTotal.ToString("N0"),
                ShareText = parentTotal > 0 ? ((double)petTotal / parentTotal).ToString("P0") : "",
                DetailText = $"{items.Sum(b => b.Hits):N0} hits",
                BarFraction = parentTotal > 0 ? (double)petTotal / parentTotal : 0,
            };

            var petMelee = items.Where(IsMelee).ToList();
            var petOther = items.Where(b => !IsMelee(b)).ToList();
            if (petMelee.Count > 0)
                petNode.Children.Add(GroupNode("Melee", petMelee, petTotal, depth: childDepth + 1));
            foreach (SourceBucket b in petOther.OrderByDescending(b => b.Total))
                petNode.Children.Add(LeafNode(b.Name, b.Category, b, petTotal, depth: childDepth + 1));

            result.Add(petNode);
        }

        return result.OrderByDescending(n => n.Value);
    }

    private static BreakdownNode GroupNode(string label, List<SourceBucket> items, long parentTotal, int depth)
    {
        long total = items.Sum(b => b.Total);
        long hits = items.Sum(b => b.Hits);
        long crits = items.Sum(b => b.Crits);
        var node = new BreakdownNode
        {
            Label = label,
            Depth = depth,
            Kind = "group",
            Value = total,
            ValueText = total.ToString("N0"),
            ShareText = parentTotal > 0 ? ((double)total / parentTotal).ToString("P0") : "",
            DetailText = hits > 0 ? $"{hits:N0} hits · crit {(double)crits / hits:P0}" : "",
            BarFraction = parentTotal > 0 ? (double)total / parentTotal : 0,
        };

        foreach (SourceBucket b in items.OrderByDescending(b => b.Total))
            node.Children.Add(LeafNode(b.Name, b.Category, b, total, depth + 1));

        return node;
    }

    private static BreakdownNode LeafNode(string label, string category, SourceBucket b, long parentTotal, int depth) => new()
    {
        Label = label,
        Sub = category,
        Depth = depth,
        Kind = "leaf",
        Value = b.Total,
        ValueText = b.Total.ToString("N0"),
        ShareText = parentTotal > 0 ? ((double)b.Total / parentTotal).ToString("P0") : "",
        DetailText = b.Hits > 0
            ? $"{b.Hits:N0} hits · avg {b.Average:N0} · max {b.Max:N0}" + (b.Crits > 0 ? $" · crit {b.CritRate:P0}" : "")
            : "",
        BarFraction = parentTotal > 0 ? (double)b.Total / parentTotal : 0,
    };

    // Only auto-attack "white" melee collapses into the Melee group. Skill attacks
    // (kick, strike, backstab, frenzy, bash, punch) stay as their own lines.
    private static bool IsMelee(SourceBucket b) => b.Category == "Melee";

    /// <summary>"Gnomies`s Animated Corpse" → "Animated Corpse" for a tidier pet label.</summary>
    private static string StripOwnerPrefix(string name)
    {
        int tick = name.IndexOf("`s ", StringComparison.Ordinal);
        return tick > 0 && tick < 20 ? name[(tick + 3)..] : name;
    }

    private static long SumAll(IReadOnlyList<FighterStats> fighters, Func<FighterStats, long> val) =>
        fighters.Sum(val);
}
