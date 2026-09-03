using TriuneLogParser.Core.Model;
using TriuneLogParser.Core.Parsing;

namespace TriuneLogParser.Core.Encounters;

/// <summary>
/// Best-effort classification of names into player / pet / NPC. EverQuest logs don't
/// label entities, so we infer:
/// <list type="bullet">
///   <item>the logging character is a player (seed);</item>
///   <item>anything a verified player heals, or that heals a verified player, is a player;</item>
///   <item>anything a verified player damages, or that damages a verified player, is an NPC;</item>
///   <item>names in the pet registry are pets (owner is a player);</item>
///   <item>names beginning with "a "/"an "/"the " are NPCs;</item>
///   <item>leftover proper names default to player, leftover lowercase names to NPC.</item>
/// </list>
/// Classifications can be revised as more of the log is seen; batch callers should do
/// a classification pass before building encounters.
/// </summary>
public sealed class RosterTracker
{
    private readonly PetRegistry _pets;
    private readonly Dictionary<string, EntityKind> _kinds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _verifiedPlayers = new(StringComparer.OrdinalIgnoreCase);

    public RosterTracker(PetRegistry pets, string? characterName)
    {
        _pets = pets;
        if (!string.IsNullOrEmpty(characterName))
        {
            _verifiedPlayers.Add(characterName);
            _kinds[characterName] = EntityKind.Player;
        }

        _pets.OwnerLearned += (pet, owner) =>
        {
            _kinds[pet] = EntityKind.Pet;
            MarkPlayer(owner);
            PetOwnerLearned?.Invoke(pet, owner);
        };
    }

    /// <summary>Re-raised from the pet registry when a pet's owner is first established.</summary>
    public event Action<string /*pet*/, string /*owner*/>? PetOwnerLearned;

    public IReadOnlyCollection<string> VerifiedPlayers => _verifiedPlayers;

    /// <summary>Update classifications from one event. Call for every event, in order.</summary>
    public void Observe(CombatEvent e)
    {
        switch (e.Action)
        {
            case CombatAction.Heal:
                // Healer and target are almost always friendly.
                if (e.Attacker is { } h && LooksLikePlayerName(h)) MarkPlayer(h);
                if (e.Target is { } ht && LooksLikePlayerName(ht)) MarkPlayer(ht);
                if (e.AttackerOwner is { } ho) MarkPlayer(ho);
                break;

            case CombatAction.Damage or CombatAction.Miss:
                string? a = e.Attacker;
                string? t = e.Target;
                if (e.AttackerOwner is { } owner)
                {
                    MarkPlayer(owner);
                    if (a != null) _kinds.TryAdd(a, EntityKind.Pet);
                }

                bool aPlayer = a != null && IsKnownPlayer(a);
                bool tPlayer = t != null && IsKnownPlayer(t);

                if (aPlayer && t != null) MarkNpc(t);
                if (tPlayer && a != null && !IsPet(a)) MarkNpc(a);

                if (a != null && StartsWithArticle(a)) MarkNpc(a);
                if (t != null && StartsWithArticle(t)) MarkNpc(t);
                break;

            case CombatAction.Death:
                if (e.Attacker is { } k && LooksLikePlayerName(k) && !StartsWithArticle(k)) MarkPlayer(k);
                if (e.Target is { } v && StartsWithArticle(v)) MarkNpc(v);
                break;
        }
    }

    /// <summary>Final classification for a name.</summary>
    public EntityKind Classify(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return EntityKind.Unknown;

        if (_pets.IsPet(name))
            return EntityKind.Pet;
        if (_kinds.TryGetValue(name, out EntityKind kind))
            return kind;

        if (StartsWithArticle(name))
            return EntityKind.Npc;

        // Named NPCs on Triune are multi-word Title Case ("Doomfire Warlord");
        // player / pet names are a single token.
        if (name.Contains(' '))
            return EntityKind.Npc;

        // Unknown proper single-token name: assume a friendly actor (merc / box /
        // unlinked pet) rather than an NPC, so its output is still surfaced.
        return char.IsUpper(name[0]) ? EntityKind.Player : EntityKind.Npc;
    }

    public bool IsPet(string name) => _pets.IsPet(name);
    public string? OwnerOf(string name) => _pets.OwnerOf(name);

    private bool IsKnownPlayer(string name) =>
        _verifiedPlayers.Contains(name) ||
        (_kinds.TryGetValue(name, out EntityKind k) && k == EntityKind.Player);

    private void MarkPlayer(string name)
    {
        if (StartsWithArticle(name))
            return;
        _verifiedPlayers.Add(name);
        _kinds[name] = EntityKind.Player;
    }

    private void MarkNpc(string name)
    {
        if (_pets.IsPet(name) || _verifiedPlayers.Contains(name))
            return;
        _kinds[name] = EntityKind.Npc;
    }

    private static bool StartsWithArticle(string name) =>
        name.StartsWith("a ", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("an ", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("the ", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikePlayerName(string name) =>
        name.Length > 0 && char.IsUpper(name[0]) && !name.Contains(' ');
}
