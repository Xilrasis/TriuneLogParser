using TriuneLogParser.Core.Classes;
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

    /// <summary>Saved split markers, ordered; <see cref="_markerIndex"/> is how many have fired.</summary>
    private readonly List<EncounterMarker> _markers;
    private int _markerIndex;
    private bool _firstEventSeen;

    public CombatLogProcessor(
        string? characterName,
        EncounterOptions? options = null,
        bool streaming = false,
        IEnumerable<EncounterMarker>? markers = null)
    {
        var names = new NameResolver(characterName);
        var pets = new PetRegistry();
        _parser = new CombatLogParser(names, pets);
        _builder = new EncounterBuilder(new RosterTracker(pets, characterName), options);
        _streaming = streaming;
        _markers = (markers ?? Enumerable.Empty<EncounterMarker>())
            .Where(m => m.Kind == MarkerKind.Split)
            .OrderBy(m => m.Timestamp)
            .ToList();
    }

    public ParserMetrics Metrics { get; } = new();
    public ClassTracker Classes { get; } = new();
    public EncounterBuilder Builder => _builder;
    public CombatLogParser Parser => _parser;
    public IReadOnlyList<CombatEvent> Events => _events;
    public IReadOnlyList<ZoneChange> ZoneChanges => _zones;

    /// <summary>Timestamp of the most recent parsed combat event, for aligning a live split.</summary>
    public DateTime? LastEventTimestamp { get; private set; }

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
        LastEventTimestamp = evt.Timestamp;

        if (_streaming)
        {
            _builder.Roster.Observe(evt);
            ApplyStreamingMarkers(evt.Timestamp);
            _builder.Advance(evt.Timestamp);
            _builder.Handle(evt);
            Classes.Observe(evt);
        }
    }

    /// <summary>
    /// Apply any saved split markers whose moment has arrived. On the very first event we
    /// discard markers that predate our data window (e.g. a "parse only new lines" start)
    /// — a marker with nothing before it can't split anything.
    /// </summary>
    private void ApplyStreamingMarkers(DateTime now)
    {
        if (!_firstEventSeen)
        {
            _firstEventSeen = true;
            while (_markerIndex < _markers.Count && _markers[_markerIndex].Timestamp < now)
                _markerIndex++;
        }

        while (_markerIndex < _markers.Count && _markers[_markerIndex].Timestamp <= now)
            _builder.ForceSplit(_markers[_markerIndex++].Timestamp);
    }

    /// <summary>Streaming only: close fights that have gone idle as of <paramref name="now"/>.</summary>
    public void Advance(DateTime now) => _builder.Advance(now);

    /// <summary>
    /// Streaming: end the fight in progress now (the "split fight" button). Records the
    /// split against <see cref="LastEventTimestamp"/> so the caller can persist a marker.
    /// Returns the moment used, or null if there is nothing to split yet.
    /// </summary>
    public DateTime? ForceSplit()
    {
        if (LastEventTimestamp is not { } at)
            return null;
        _builder.ForceSplit(at);
        return at;
    }

    /// <summary>Streaming: the fight in progress, if any.</summary>
    public Encounter? CurrentEncounter => _builder.Current;

    /// <summary>Streaming: fights closed so far.</summary>
    public IReadOnlyList<Encounter> ClosedEncounters => _builder.Completed;

    /// <summary>Streaming: flush the open fight (e.g. reached end of a static file).</summary>
    public IReadOnlyList<Encounter> FinishStreaming(DateTime now)
    {
        _builder.Advance(now);
        _builder.Finish();
        return _builder.Completed;
    }

    /// <summary>Batch: build every encounter from the lines fed so far.</summary>
    public IReadOnlyList<Encounter> BuildBatch()
    {
        IReadOnlyList<Encounter> result =
            _builder.BuildAll(_events, _zones, _markers.Select(m => m.Timestamp));
        foreach (CombatEvent e in _events) // kinds are set now
            Classes.Observe(e);
        return result;
    }
}
