using TriuneLogParser.Core.Encounters;
using TriuneLogParser.Core.Logging;
using TriuneLogParser.Core.Model;
using TriuneLogParser.Core.Parsing;

namespace TriuneLogParser.Core;

/// <summary>
/// End-to-end pipeline: raw line → parsed event → crit association → encounter.
/// Use <see cref="AddLine"/> then <see cref="BuildBatch"/> for a whole file, or
/// <see cref="AddLine"/> + <see cref="Advance"/> continuously when following a live log
/// and read <see cref="Builder"/>.<c>Current</c> / <c>Completed</c>.
/// </summary>
public sealed class CombatLogProcessor
{
    private readonly CombatLogParser _parser;
    private readonly CriticalAssociator _crit = new();
    private readonly EncounterBuilder _builder;
    private readonly List<CombatEvent> _events = new();
    private readonly List<ZoneChange> _zones = new();
    private readonly bool _streaming;

    public CombatLogProcessor(string? characterName, EncounterOptions? options = null, bool streaming = false)
    {
        var names = new NameResolver(characterName);
        var pets = new PetRegistry();
        _parser = new CombatLogParser(names, pets);
        _builder = new EncounterBuilder(new RosterTracker(pets, characterName), options);
        _streaming = streaming;
    }

    public ParserMetrics Metrics { get; } = new();
    public EncounterBuilder Builder => _builder;
    public CombatLogParser Parser => _parser;
    public IReadOnlyList<CombatEvent> Events => _events;
    public IReadOnlyList<ZoneChange> ZoneChanges => _zones;

    /// <summary>Feed one raw log line.</summary>
    public void AddLine(string raw, int lineNumber)
    {
        Metrics.TotalLines++;
        if (!LogLine.TryParse(raw, lineNumber, out LogLine line))
            return;

        Metrics.TimestampedLines++;
        ParseOutcome outcome = _parser.Parse(line);

        if (outcome.Crit is { } crit)
        {
            Metrics.CritMarkers++;
            _crit.Observe(crit);
            return;
        }

        if (outcome.Zone is { } zone)
        {
            Metrics.ZoneChanges++;
            _zones.Add(zone);
            if (_streaming)
                _builder.HandleZone(zone);
            return;
        }

        if (outcome.UnparsedDamageLike)
        {
            Metrics.NoteUnparsed(line.Message);
            return;
        }

        if (outcome.Event is not { } evt)
            return;

        Metrics.EventsParsed++;
        _crit.Observe(evt);
        _events.Add(evt);

        if (_streaming)
        {
            _builder.Roster.Observe(evt);
            _builder.Advance(evt.Timestamp);
            _builder.Handle(evt);
        }
    }

    /// <summary>Streaming only: close fights that have gone idle as of <paramref name="now"/>.</summary>
    public void Advance(DateTime now) => _builder.Advance(now);

    /// <summary>Batch: build every encounter from the lines fed so far.</summary>
    public IReadOnlyList<Encounter> BuildBatch() => _builder.BuildAll(_events, _zones);
}
