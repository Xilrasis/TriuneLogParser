using TriuneLogParser.Core.Model;

namespace TriuneLogParser.Core.Classes;

/// <summary>
/// Accumulates class evidence per player across a whole session by matching the
/// signature abilities / spells in <see cref="ClassCatalog"/>. Multiclass-aware:
/// reports every class a player has shown, ordered by how much evidence there is.
/// </summary>
public sealed class ClassTracker
{
    private readonly Dictionary<string, Dictionary<EqClass, int>> _evidence =
        new(StringComparer.OrdinalIgnoreCase);

    private const int MinHits = 2;

    public void Observe(CombatEvent e)
    {
        if (e.AttackerKind != EntityKind.Player || e.Attacker is not { } who)
            return;

        if (e.SpellName is { } spell && ClassCatalog.Signatures.TryGetValue(spell, out EqClass c1))
            Add(who, c1);

        if (e.Verb is { } verb && ClassCatalog.VerbSignatures.TryGetValue(verb, out EqClass c2))
            Add(who, c2);
    }

    private void Add(string who, EqClass c)
    {
        if (!_evidence.TryGetValue(who, out Dictionary<EqClass, int>? m))
            _evidence[who] = m = new Dictionary<EqClass, int>();
        m[c] = m.GetValueOrDefault(c) + 1;
    }

    /// <summary>Detected classes for a player, most evidence first (empty if none / not confident).</summary>
    public IReadOnlyList<EqClass> ClassesOf(string player)
    {
        if (!_evidence.TryGetValue(player, out Dictionary<EqClass, int>? m))
            return Array.Empty<EqClass>();

        return m.Where(kv => kv.Value >= MinHits)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();
    }

    /// <summary>"Monk / Rogue" style label, or "" when nothing is confident yet.</summary>
    public string LabelFor(string player)
    {
        IReadOnlyList<EqClass> classes = ClassesOf(player);
        return classes.Count == 0 ? "" : string.Join(" / ", classes.Take(3));
    }

    public IReadOnlyDictionary<string, string> AllLabels() =>
        _evidence.Keys.ToDictionary(k => k, LabelFor, StringComparer.OrdinalIgnoreCase);
}
