using System.Text.Json;
using TriuneLogParser.Core;
using TriuneLogParser.Core.Aggregation;
using TriuneLogParser.Core.Config;
using TriuneLogParser.Core.Encounters;
using TriuneLogParser.Core.Logging;
using TriuneLogParser.Core.Model;

if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
{
    Console.WriteLine(
        """
        triuneparse - Project Triune EverQuest log parser (CLI)

          triuneparse <logfile> [options]

        Options:
          --table            Human-readable per-encounter breakdown (default)
          --json             Machine-readable JSON
          --follow           Tail the file and print encounters as they close
          --idle <seconds>   Idle timeout before a fight closes (default 45)
          --rest <seconds>   Rest period (0-300); 0 = per-pull split, >0 = session/event
                             style. Overrides --idle when given.
          --top <n>          Show at most n source buckets per fighter (default 6)
          --unparsed         List distinct damage-like lines the grammar missed
          --markers <file>   Split-marker JSON to apply (default: the saved sidecar for
                             this character, if any)
          --no-markers       Ignore saved split markers
        """);
    return args.Length == 0 ? 1 : 0;
}

string path = args[0];
if (!File.Exists(path))
{
    Console.Error.WriteLine($"File not found: {path}");
    return 2;
}

bool json = args.Contains("--json");
bool follow = args.Contains("--follow");
bool showUnparsed = args.Contains("--unparsed");
int idle = OptInt("--idle", 45);
int rest = OptInt("--rest", -1);
int top = OptInt("--top", 6);

var options = rest >= 0
    ? EncounterOptions.ForRestPeriod(rest)
    : new EncounterOptions { IdleTimeout = TimeSpan.FromSeconds(idle) };
string? character = LogFileTailer.TryExtractCharacterName(path);

IReadOnlyList<EncounterMarker> markers = LoadMarkers();
if (markers.Count > 0)
    Console.Error.WriteLine($"Applying {markers.Count} saved split marker(s).");

if (follow)
{
    await FollowAsync(path, character, options, markers, json, top);
    return 0;
}

var processor = new CombatLogProcessor(character, options, markers: markers);
int lineNo = 0;
foreach (string line in File.ReadLines(path))
    processor.AddLine(line, ++lineNo);

IReadOnlyList<Encounter> encounters = processor.BuildBatch();

if (json)
    PrintJson(encounters, processor);
else
    PrintTables(encounters, processor, top);

if (showUnparsed)
    PrintUnparsed(processor);

return 0;

int OptInt(string name, int fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int v) ? v : fallback;
}

string? OptStr(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

IReadOnlyList<EncounterMarker> LoadMarkers()
{
    if (args.Contains("--no-markers"))
        return Array.Empty<EncounterMarker>();
    try
    {
        string? file = OptStr("--markers");
        return file is not null ? MarkerStore.Load(file).Markers : MarkerStore.LoadForLog(path);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Could not load split markers: {ex.Message}");
        return Array.Empty<EncounterMarker>();
    }
}

static async Task FollowAsync(
    string path, string? character, EncounterOptions options,
    IReadOnlyList<EncounterMarker> markers, bool json, int top)
{
    var processor = new CombatLogProcessor(character, options, streaming: true, markers);
    var tailer = new LogFileTailer(path);
    int printed = 0;

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    Console.Error.WriteLine($"Following {Path.GetFileName(path)} (character: {character ?? "?"}). Ctrl+C to stop.");

    var pump = tailer.FollowAsync((line, n) => processor.AddLine(line, n), cts.Token);

    while (!cts.IsCancellationRequested)
    {
        try { await Task.Delay(1000, cts.Token); }
        catch (OperationCanceledException) { break; }

        processor.Advance(DateTime.Now);
        for (; printed < processor.Builder.Completed.Count; printed++)
        {
            Encounter enc = processor.Builder.Completed[printed];
            if (json) PrintEncounterJson(enc);
            else PrintEncounterTable(EncounterAggregator.Report(enc), enc, top);
        }
    }

    try { await pump; } catch (OperationCanceledException) { }
}

static void PrintTables(IReadOnlyList<Encounter> encounters, CombatLogProcessor p, int top)
{
    Console.WriteLine($"Parsed {p.Metrics.EventsParsed:N0} events from {p.Metrics.TimestampedLines:N0} log lines "
        + $"-> {encounters.Count} encounter(s). Grammar coverage {p.Metrics.Coverage:P2}.");
    Console.WriteLine();

    foreach (Encounter enc in encounters)
        PrintEncounterTable(EncounterAggregator.Report(enc), enc, top);
}

static void PrintEncounterTable(EncounterReport r, Encounter enc, int top)
{
    string killed = enc.NpcsKilled.Count > 0 ? $" | killed {enc.NpcsKilled.Count}" : "";
    string deaths = enc.PlayerDeaths.Count > 0 ? $" | deaths: {string.Join(", ", enc.PlayerDeaths)}" : "";
    Console.WriteLine($"== #{enc.Id}  {enc.Start:HH:mm:ss}  {enc.Title}  "
        + $"[{r.DurationSeconds:0}s | {enc.EndReason}{killed}{deaths}]");

    if (r.DamageDone.Count == 0)
    {
        Console.WriteLine("   (no player damage)");
        Console.WriteLine();
        return;
    }

    Console.WriteLine($"   {"Player",-18} {"Damage",12} {"DPS",10} {"%",6} {"Crit%",7} {"Acc%",7}");
    foreach (FighterStats f in r.DamageDone)
    {
        double share = r.TotalDamage > 0 ? (double)f.DamageDone / r.TotalDamage : 0;
        string pets = f.Pets.Count > 0 ? $"  (+pet {string.Join(", ", f.Pets)})" : "";
        Console.WriteLine($"   {Trunc(f.Name, 18),-18} {f.DamageDone,12:N0} {f.DpsOver(r.DurationSeconds),10:N0} "
            + $"{share,6:P0} {f.CritRate,7:P0} {f.Accuracy,7:P0}{pets}");

        foreach (SourceBucket b in f.DamageSources.Take(top))
        {
            string label = b.PetName is { } pn ? $"pet {pn}: {b.Name}" : b.Name;
            Console.WriteLine($"      {Trunc(label, 26),-26} {b.Category,-14} {b.Total,12:N0} "
                + $"x{b.Hits,-5} avg {b.Average,8:N0}  max {b.Max,8:N0}");
        }
    }

    if (r.Healing.Count > 0)
    {
        Console.WriteLine($"   {"Healer",-18} {"Healing",12} {"HPS",10}");
        foreach (FighterStats f in r.Healing)
            Console.WriteLine($"   {Trunc(f.Name, 18),-18} {f.HealingDone,12:N0} {f.HpsOver(r.DurationSeconds),10:N0}");
    }

    if (r.DamageTaken.Count > 0)
    {
        Console.WriteLine($"   {"Took damage",-18} {"Damage",12}");
        foreach (FighterStats f in r.DamageTaken)
            Console.WriteLine($"   {Trunc(f.Name, 18),-18} {f.DamageTaken,12:N0}");
    }

    if (r.Defenses.Count > 0)
    {
        Console.WriteLine($"   {"Defender / attack",-26} {"Swings",8} {"Hit%",6} {"Avg",9} {"Max",9}  avoidance");
        foreach (DefenseStats d in r.Defenses.Take(8))
        {
            Console.WriteLine($"   {Trunc(d.Name, 26),-26} {d.Swings,8:N0} {d.HitRate,6:P0} {"",9} {d.Max,9:N0}  "
                + $"avoided {d.AvoidRate:P0} (miss {d.Misses}, parry {d.Parries}, dodge {d.Dodges}, block {d.Blocks}, riposte {d.Ripostes})");
            foreach (IncomingAttackStats a in d.Attacks.Take(top))
            {
                string av = a.IsMelee
                    ? $"m{a.Misses} p{a.Parries} d{a.Dodges} b{a.Blocks} r{a.Ripostes}"
                    : "-";
                Console.WriteLine($"      {Trunc(a.Type, 23),-23} {a.Swings,8:N0} {a.HitRate,6:P0} {a.Average,9:N0} {a.Max,9:N0}  {av}");
            }
        }
    }

    if (r.Mobs.Count > 0)
    {
        Console.WriteLine($"   {"Mob",-26} {"Damage",12} {"TTK",7}  killed by");
        foreach (MobStats m in r.Mobs.Take(12))
        {
            Console.WriteLine($"   {Trunc(m.Name, 26),-26} {m.DamageTaken,12:N0} {m.TimeToKillSeconds,6:0}s  {m.LastKiller ?? "-"}");
        }
    }

    Console.WriteLine();
}

static void PrintJson(IReadOnlyList<Encounter> encounters, CombatLogProcessor p)
{
    var payload = new
    {
        metrics = new
        {
            p.Metrics.TotalLines,
            p.Metrics.TimestampedLines,
            p.Metrics.EventsParsed,
            p.Metrics.UnparsedDamageLike,
            coverage = p.Metrics.Coverage,
        },
        encounters = encounters.Select(BuildEncounterDto),
    };
    Console.WriteLine(JsonSerializer.Serialize(payload, Json.Opts));
}

static void PrintEncounterJson(Encounter enc) =>
    Console.WriteLine(JsonSerializer.Serialize(BuildEncounterDto(enc), Json.Opts));

static object BuildEncounterDto(Encounter enc)
{
    EncounterReport r = EncounterAggregator.Report(enc);
    return new
    {
        enc.Id,
        title = enc.Title,
        start = enc.Start,
        end = enc.End,
        durationSeconds = r.DurationSeconds,
        endReason = enc.EndReason.ToString(),
        zone = enc.Zone,
        mobsKilled = enc.NpcsKilled,
        playerDeaths = enc.PlayerDeaths,
        totalDamage = r.TotalDamage,
        damage = r.DamageDone.Select(f => new
        {
            f.Name,
            kind = f.Kind.ToString(),
            f.Owner,
            f.DamageDone,
            dps = f.DpsOver(r.DurationSeconds),
            share = r.TotalDamage > 0 ? (double)f.DamageDone / r.TotalDamage : 0,
            critRate = f.CritRate,
            accuracy = f.Accuracy,
            pets = f.Pets,
            sources = f.DamageSources.Select(b => new
            {
                b.Category, b.Name, b.PetName, b.Total, b.Hits, b.Crits, b.Max,
                average = b.Average,
            }),
        }),
        healing = r.Healing.Select(f => new { f.Name, f.HealingDone, hps = f.HpsOver(r.DurationSeconds) }),
        damageTaken = r.DamageTaken.Select(f => new { f.Name, f.DamageTaken }),
    };
}

static void PrintUnparsed(CombatLogProcessor p)
{
    Console.WriteLine();
    Console.WriteLine($"Unparsed damage-like lines: {p.Metrics.UnparsedDamageLike:N0} "
        + $"({p.Metrics.UnparsedSamples.Count} distinct shapes)");
    foreach ((string shape, int count) in p.Metrics.UnparsedSamples.OrderByDescending(kv => kv.Value).Take(40))
        Console.WriteLine($"  {count,6:N0}  {shape}");
}

static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "...";

static class Json
{
    public static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
}
