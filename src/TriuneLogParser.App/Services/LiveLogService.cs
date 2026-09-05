using TriuneLogParser.Core;
using TriuneLogParser.Core.Aggregation;
using TriuneLogParser.Core.Config;
using TriuneLogParser.Core.Encounters;
using TriuneLogParser.Core.Logging;
using TriuneLogParser.Core.Model;
using TriuneLogParser.Core.Parsing;

namespace TriuneLogParser.App.Services;

/// <summary>A cheap, immutable view of one encounter for the list.</summary>
public sealed record EncounterSummary(
    int Id, DateTime Start, TimeSpan Duration, string Title, bool IsActive,
    EncounterEndReason EndReason, string TopFighter, double TopDps, long TotalDamage, int Deaths);

public sealed record LiveSnapshot(
    bool Loading, string? Character, string? Zone, double Coverage,
    IReadOnlyList<EncounterSummary> Encounters);

/// <summary>
/// Owns the streaming parse of one character's log: bulk-loads the existing file,
/// then follows it. All access to the parser state is serialised on a lock; the WPF
/// layer polls <see cref="Tick"/> / <see cref="GetSnapshot"/> from a dispatcher timer.
/// </summary>
public sealed class LiveLogService : IDisposable
{
    private readonly object _gate = new();
    private CombatLogProcessor? _processor;
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private EncounterOptions _options = new();
    private volatile bool _loading;
    private long _generation;
    private readonly Dictionary<int, EncounterSummary> _summaryCache = new();

    /// <summary>Records grammar misses to <c>%AppData%/TriuneLogParser/unparsed.log</c>.</summary>
    private readonly UnparsedLogWriter _unparsed = new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TriuneLogParser", "unparsed.log"));

    public string? Character { get; private set; }
    public string? LogPath { get; private set; }

    /// <summary>Where unparsed damage-like lines are being logged.</summary>
    public string UnparsedLogPath => _unparsed.Path;

    /// <summary>How far back to parse on start; 0 = only lines appended after start.</summary>
    public int RetroParseMinutes { get; set; } = 30;

    /// <summary>Set by <see cref="Start"/> for one run to override <see cref="RetroParseMinutes"/>.</summary>
    private int? _retroOverride;

    /// <summary>&gt;0 to archive the followed log when it passes this size (bytes).</summary>
    public long LogSplitBytes { get; set; }

    /// <summary>Raised (arbitrary thread) whenever new lines have been processed.</summary>
    public event Action? Changed;

    /// <summary>Raised (arbitrary thread) with a short status message (log split, errors).</summary>
    public event Action<string>? Notice;

    public void Configure(EncounterOptions options) => _options = options;

    /// <summary>
    /// Begin (or restart) following <paramref name="logPath"/>. Pass
    /// <paramref name="retroMinutesOverride"/> to override <see cref="RetroParseMinutes"/>
    /// for this run only — e.g. 0 to drop history and tail from here.
    /// </summary>
    public void Start(string logPath, string? character, int? retroMinutesOverride = null)
    {
        Stop();

        var cts = new CancellationTokenSource();
        long gen = Interlocked.Increment(ref _generation);

        IReadOnlyList<EncounterMarker> markers = SafeLoadMarkers(logPath);

        lock (_gate)
        {
            _retroOverride = retroMinutesOverride;
            _processor = new CombatLogProcessor(character, _options, streaming: true, markers, _unparsed.Append);
            _summaryCache.Clear();
            _loading = true;
            Character = character;
            LogPath = logPath;
            _cts = cts;
        }

        _worker = Task.Run(() => RunAsync(logPath, gen, cts.Token));
        Changed?.Invoke();
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _cts;
            _cts = null;
        }

        if (cts is null)
            return;

        try { cts.Cancel(); } catch { /* ignore */ }
        try { _worker?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        cts.Dispose();
    }

    private async Task RunAsync(string path, long generation, CancellationToken ct)
    {
        try
        {
            var tailer = new LogFileTailer(path, TimeSpan.FromMilliseconds(750));

            int retro;
            lock (_gate)
                retro = _retroOverride ?? RetroParseMinutes;
            if (retro <= 0)
            {
                // Active log only.
                tailer.SeekToEnd();
            }
            else
            {
                // Bulk-load, but skip lines older than the retro window.
                DateTime cutoff = DateTime.Now.AddMinutes(-retro);
                bool inWindow = false;
                foreach (string line in tailer.ReadNewLines())
                {
                    if (ct.IsCancellationRequested || Volatile.Read(ref _generation) != generation)
                        return;

                    if (!inWindow)
                    {
                        if (!LogLine.TryParse(line, 0, out LogLine parsed))
                            continue; // pre-window noise
                        if (parsed.Timestamp < cutoff)
                            continue;
                        inWindow = true;
                    }

                    lock (_gate)
                        _processor?.AddLine(line, tailer.LineNumber);
                }
            }

            _loading = false;
            Changed?.Invoke();

            DateTime lastSplitCheck = DateTime.MinValue;

            // Follow appends.
            await tailer.FollowAsync((line, n) =>
            {
                if (Volatile.Read(ref _generation) != generation)
                    return;
                lock (_gate)
                    _processor?.AddLine(line, n);
                Changed?.Invoke();

                if (LogSplitBytes > 0 && DateTime.UtcNow - lastSplitCheck > TimeSpan.FromSeconds(20))
                {
                    lastSplitCheck = DateTime.UtcNow;
                    LogArchiver.Result r = LogArchiver.TrySplit(path, LogSplitBytes);
                    if (r.Split)
                        Notice?.Invoke($"Log archived to {System.IO.Path.GetFileName(r.ArchivePath)}");
                    else if (r.Error != null)
                        Notice?.Invoke($"Log split failed: {r.Error}");
                }
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            _loading = false;
        }
    }

    /// <summary>Close fights that have gone idle as of <paramref name="now"/>. Call from the UI timer.</summary>
    public void Tick(DateTime now)
    {
        lock (_gate)
            _processor?.Advance(now);
    }

    /// <summary>True when there's a live parse with at least one event — a split can be placed.</summary>
    public bool CanSplit
    {
        get { lock (_gate) return _processor?.LastEventTimestamp is not null; }
    }

    /// <summary>
    /// End the current encounter now and persist a split marker beside the log, so
    /// re-parses reproduce the boundary. No-op if nothing has been parsed yet.
    /// </summary>
    public void ForceSplit()
    {
        DateTime? at;
        string? logPath;
        lock (_gate)
        {
            at = _processor?.ForceSplit();
            logPath = LogPath;
        }

        if (at is null || logPath is null)
            return;

        try { MarkerStore.AddSplit(logPath, at.Value); }
        catch (Exception ex) { Notice?.Invoke($"Split saved in memory only: {ex.Message}"); }

        Notice?.Invoke($"Encounter split at {at:HH:mm:ss}");
        Changed?.Invoke();
    }

    private static IReadOnlyList<EncounterMarker> SafeLoadMarkers(string logPath)
    {
        try { return MarkerStore.LoadForLog(logPath); }
        catch { return Array.Empty<EncounterMarker>(); }
    }

    /// <summary>
    /// Archive the log being monitored right now, regardless of size — the manual
    /// equivalent of the size trigger. The tailer picks up the fresh log the game
    /// writes next; the in-memory parse is untouched.
    /// </summary>
    public LogArchiver.Result ArchiveLogNow()
    {
        string? path;
        lock (_gate)
            path = LogPath;

        if (string.IsNullOrEmpty(path))
            return new LogArchiver.Result(false, null, "No log is being monitored.");

        LogArchiver.Result r = LogArchiver.TrySplit(path, maxBytes: 1);
        if (r.Split)
            Notice?.Invoke($"Log archived to {System.IO.Path.GetFileName(r.ArchivePath)}");
        else if (r.Error != null)
            Notice?.Invoke($"Archive failed: {r.Error}");
        return r;
    }

    public LiveSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            if (_processor is null)
                return new LiveSnapshot(_loading, Character, null, 1, Array.Empty<EncounterSummary>());

            var summaries = new List<EncounterSummary>();
            foreach (Encounter e in _processor.ClosedEncounters)
            {
                if (!_summaryCache.TryGetValue(e.Id, out EncounterSummary? s))
                {
                    s = Summarise(e);
                    _summaryCache[e.Id] = s;
                }

                summaries.Add(s);
            }

            if (_processor.CurrentEncounter is { } cur)
            {
                _summaryCache.Remove(cur.Id); // a re-opened fight must be re-summarised when it closes again
                summaries.Add(Summarise(cur));
            }

            summaries.Reverse(); // newest first
            return new LiveSnapshot(
                _loading, Character, _processor.CurrentEncounter?.Zone ?? LastZone(),
                _processor.Metrics.Coverage, summaries);
        }
    }

    /// <summary>Full breakdown for a set of encounter ids (merged when more than one).</summary>
    public EncounterReport? Report(IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0)
            return null;

        lock (_gate)
        {
            if (_processor is null)
                return null;

            var wanted = new List<Encounter>();
            foreach (Encounter e in _processor.ClosedEncounters)
                if (ids.Contains(e.Id))
                    wanted.Add(e);
            if (_processor.CurrentEncounter is { } cur && ids.Contains(cur.Id))
                wanted.Add(cur);

            return wanted.Count == 0 ? null : EncounterAggregator.Report(wanted);
        }
    }

    /// <summary>Best-guess class label for a player ("Monk / Rogue"), or "" if unknown.</summary>
    public string ClassLabel(string fighter)
    {
        lock (_gate)
            return _processor?.Classes.LabelFor(fighter) ?? "";
    }

    /// <summary>The live fight if one is active, otherwise the most recent completed fight.</summary>
    public (EncounterReport? report, string? title, double seconds, bool active) CurrentOrLatest()
    {
        lock (_gate)
        {
            if (_processor is null)
                return (null, null, 0, false);

            Encounter? enc = _processor.CurrentEncounter
                             ?? (_processor.ClosedEncounters.Count > 0 ? _processor.ClosedEncounters[^1] : null);
            if (enc is null)
                return (null, null, 0, false);

            EncounterReport r = EncounterAggregator.Report(enc);
            return (r, enc.Title, r.DurationSeconds, enc.IsActive);
        }
    }

    private string? LastZone()
    {
        IReadOnlyList<ZoneChange> zones = _processor!.ZoneChanges;
        return zones.Count > 0 ? zones[^1].Zone : null;
    }

    private static EncounterSummary Summarise(Encounter e)
    {
        EncounterReport r = EncounterAggregator.Report(e);
        FighterStats? top = r.DamageDone.Count > 0 ? r.DamageDone[0] : null;
        return new EncounterSummary(
            e.Id, e.Start, e.Duration, e.Title, e.IsActive, e.EndReason,
            top?.Name ?? "—",
            top?.DpsOver(r.DurationSeconds) ?? 0,
            r.TotalDamage,
            e.PlayerDeaths.Count);
    }

    public void Dispose() => Stop();
}
