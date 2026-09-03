namespace TriuneLogParser.Core.Parsing;

/// <summary>
/// Tracks which entities are pets and who owns them. Populated primarily from the
/// Triune-specific <c>Name (Owner: X)</c> tag, and secondarily from pet-command
/// responses tied to the logging character. Owner assignment is retroactive: callers
/// can re-attribute earlier events once <see cref="OwnerLearned"/> fires.
/// </summary>
public sealed class PetRegistry
{
    private readonly Dictionary<string, string> _petToOwner = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _knownPets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised the first time an owner is established for a pet name.</summary>
    public event Action<string /*pet*/, string /*owner*/>? OwnerLearned;

    public bool IsPet(string name) => _knownPets.Contains(name);

    public string? OwnerOf(string name) =>
        _petToOwner.TryGetValue(name, out string? owner) ? owner : null;

    public IReadOnlyDictionary<string, string> Owners => _petToOwner;

    /// <summary>Record a pet with a known owner (from an <c>(Owner: X)</c> tag).</summary>
    public void RegisterOwnedPet(string pet, string owner)
    {
        _knownPets.Add(pet);
        if (_petToOwner.TryGetValue(pet, out string? existing))
        {
            if (!string.Equals(existing, owner, StringComparison.OrdinalIgnoreCase))
                _petToOwner[pet] = owner; // trust the most recent explicit tag
            return;
        }

        _petToOwner[pet] = owner;
        OwnerLearned?.Invoke(pet, owner);
    }

    /// <summary>Record that a name is a pet, owner not yet known.</summary>
    public void RegisterUnownedPet(string pet) => _knownPets.Add(pet);
}
