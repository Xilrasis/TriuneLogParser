using TriuneLogParser.Core.Model;

namespace TriuneLogParser.Core.Parsing;

/// <summary>Melee swing-verb knowledge: third-person → base form, and mechanic classification.</summary>
public static class Verbs
{
    private static readonly Dictionary<string, string> ThirdPerson = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hits"] = "hit",
        ["crushes"] = "crush",
        ["slashes"] = "slash",
        ["pierces"] = "pierce",
        ["bites"] = "bite",
        ["claws"] = "claw",
        ["gores"] = "gore",
        ["stings"] = "sting",
        ["mauls"] = "maul",
        ["smashes"] = "smash",
        ["slams"] = "slam",
        ["rends"] = "rend",
        ["burns"] = "burn",
        ["freezes"] = "freeze",
        ["slices"] = "slice",
        ["kicks"] = "kick",
        ["punches"] = "punch",
        ["bashes"] = "bash",
        ["backstabs"] = "backstab",
        ["strikes"] = "strike",
        ["frenzies on"] = "frenzy",
    };

    /// <summary>Base melee verbs we accept in "&lt;Name&gt; &lt;verb&gt; &lt;target&gt; for N points of damage".</summary>
    public static readonly IReadOnlySet<string> ThirdPersonPattern = new HashSet<string>(ThirdPerson.Keys, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SpecialBase = new(StringComparer.OrdinalIgnoreCase)
    {
        "kick", "punch", "bash", "backstab", "frenzy", "strikethrough",
    };

    /// <summary>Normalise "crushes" → "crush"; leave already-base or unknown verbs untouched (lower-cased).</summary>
    public static string Normalize(string verb)
    {
        verb = verb.Trim();
        return ThirdPerson.TryGetValue(verb, out string? baseForm) ? baseForm : verb.ToLowerInvariant();
    }

    /// <summary>Classify a (base or raw) verb as white melee vs. a skill attack.</summary>
    public static DamageMechanic Classify(string verb)
    {
        string b = Normalize(verb);
        if (SpecialBase.Contains(b))
            return DamageMechanic.MeleeSpecial;
        // Compound skills like "flying kick", "eagle strike", "round kick".
        if (b.Contains(' '))
            return DamageMechanic.MeleeSpecial;
        return DamageMechanic.Melee;
    }
}
