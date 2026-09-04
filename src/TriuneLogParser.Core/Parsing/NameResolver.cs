namespace TriuneLogParser.Core.Parsing;

/// <summary>
/// Maps log pronouns ("You", "YOU", "yourself", "himself", "herself") to the logging
/// character, and does light cleanup of entity names (leading article, trailing
/// possessive, "'s corpse").
/// </summary>
public sealed class NameResolver
{
    private static readonly HashSet<string> SelfTokens = new(StringComparer.Ordinal)
    {
        "You", "YOU", "you", "Your", "your", "YOUR",
        "yourself", "Yourself", "himself", "Himself", "herself", "Herself", "itself",
    };

    public string? CharacterName { get; set; }

    public NameResolver(string? characterName = null) => CharacterName = characterName;

    /// <summary>Resolve a raw name token from the log to a canonical entity name.</summary>
    public string Resolve(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        raw = CollapseSpaces(raw);

        if (SelfTokens.Contains(raw))
            return CharacterName ?? "You";

        // "itself" / "himself" as a heal target with no character context.
        if (raw.Equals("itself", StringComparison.OrdinalIgnoreCase))
            return raw;

        raw = TrimCorpse(raw);

        if (SelfTokens.Contains(raw))
            return CharacterName ?? "You";

        raw = NormalizeArticle(raw);
        return raw;
    }

    private static string CollapseSpaces(string s)
    {
        s = s.Trim();
        return s.Contains("  ") ? string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries)) : s;
    }

    /// <summary>
    /// EverQuest capitalises the article at the start of a sentence ("A doomfire
    /// soldier hits YOU"). Lower-case it so the mob is one entity regardless of where
    /// it appeared. A following capital ("The Ancient One") marks a proper name and is
    /// left alone.
    /// </summary>
    private static string NormalizeArticle(string name)
    {
        int sp = name.IndexOf(' ');
        if (sp <= 0 || sp + 1 >= name.Length)
            return name;

        string article = name[..sp];
        bool isArticle = article is "A" or "An" or "The";
        if (isArticle && char.IsLower(name[sp + 1]))
            return char.ToLowerInvariant(name[0]) + name[1..];

        return name;
    }

    /// <summary>True when the resolved name is (or maps to) the logging character.</summary>
    public bool IsSelf(string? resolved) =>
        resolved != null && CharacterName != null &&
        resolved.Equals(CharacterName, StringComparison.OrdinalIgnoreCase);

    public static bool IsSelfToken(string raw) => SelfTokens.Contains(raw.Trim());

    /// <summary>
    /// Strip a trailing corpse marker so a dead entity's lingering DoT / effect is
    /// credited to the underlying player or mob rather than a phantom "Xscorpse" entity.
    /// EQ writes "Name's corpse", "Name`s corpse", and (mangled) "Namescorpse".
    /// </summary>
    private static string TrimCorpse(string name)
    {
        foreach (string suffix in Corpses)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name[..^suffix.Length];
        }

        // Mangled form with no separator, only seen for (single-token) player names:
        // "Gnomiesscorpse" -> "Gnomies".
        if (!name.Contains(' ') && name.Length > 8 &&
            name.EndsWith("scorpse", StringComparison.OrdinalIgnoreCase))
        {
            return name[..^"scorpse".Length];
        }

        return name;
    }

    private static readonly string[] Corpses = { "'s corpse", "`s corpse" };
}
