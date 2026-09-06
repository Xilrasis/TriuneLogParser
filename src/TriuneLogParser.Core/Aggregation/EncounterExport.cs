using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TriuneLogParser.Core.Aggregation;

/// <summary>Serialises an <see cref="EncounterReport"/> to CSV or JSON for saving / sharing.</summary>
public static class EncounterExport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    public static string ToJson(EncounterReport r)
    {
        var payload = new
        {
            r.EncounterIds,
            titles = r.Titles,
            start = r.Start,
            end = r.End,
            durationSeconds = r.DurationSeconds,
            r.TotalDamage,
            r.TotalHealing,
            damageDone = r.DamageDone.Select(f => Fighter(f, r.DurationSeconds, f.DamageDone, f.DamageSources)),
            damageTaken = r.DamageTaken.Select(f => Fighter(f, r.DurationSeconds, f.DamageTaken, f.DamageTakenSources)),
            healing = r.Healing.Select(f => Fighter(f, r.DurationSeconds, f.HealingDone, f.HealingSources)),
            mobs = r.Mobs.Select(m => new
            {
                m.Name,
                m.DamageTaken,
                m.Deaths,
                m.LastKiller,
                timeToKillSeconds = m.TimeToKillSeconds,
                byFighter = m.ByFighter.Select(x => new
                {
                    x.Fighter,
                    x.Damage,
                    sources = x.Sources.Select(b => new
                    {
                        b.Category, b.Name, b.PetName, b.Total, b.Hits, b.Crits, b.Max, average = b.Average,
                    }),
                }),
            }),
            defenses = r.Defenses.Select(d => new
            {
                d.Name,
                d.Deaths,
                d.Swings,
                d.Hits,
                d.Damage,
                avoidRate = d.AvoidRate,
                d.Misses, d.Parries, d.Dodges, d.Blocks, d.Ripostes, d.Absorbs,
                attacks = d.Attacks.Select(a => new
                {
                    a.Type, a.Category, a.Swings, a.Hits, a.Damage,
                    min = a.MinHit, a.Max, average = a.Average, a.Crits,
                    a.Misses, a.Parries, a.Dodges, a.Blocks, a.Ripostes, a.Absorbs, a.Invulnerables,
                }),
            }),
        };

        return JsonSerializer.Serialize(payload, JsonOpts);
    }

    private static object Fighter(FighterStats f, double seconds, long total, IEnumerable<SourceBucket> sources) => new
    {
        f.Name,
        kind = f.Kind.ToString(),
        f.Owner,
        total,
        perSecond = seconds > 0 ? total / seconds : 0,
        f.CritRate,
        f.Accuracy,
        f.Deaths,
        pets = f.Pets,
        sources = sources.Select(b => new
        {
            b.Category, b.Name, b.PetName, b.Total, b.Hits, b.Crits, b.Max, average = b.Average,
        }),
    };

    public static string ToCsv(EncounterReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("metric,fighter,category,source,pet,total,hits,crits,max,average");

        void Rows(string metric, IReadOnlyList<FighterStats> fighters, Func<FighterStats, long> total, Func<FighterStats, List<SourceBucket>> src)
        {
            foreach (FighterStats f in fighters)
            {
                sb.AppendLine(string.Join(',',
                    metric, Csv(f.Name), "TOTAL", "", "", total(f), f.SwingHits, f.CritHits, "", ""));
                foreach (SourceBucket b in src(f))
                {
                    sb.AppendLine(string.Join(',',
                        metric, Csv(f.Name), Csv(b.Category), Csv(b.Name), Csv(b.PetName ?? ""),
                        b.Total, b.Hits, b.Crits, b.Max, b.Average.ToString("0.##", Inv)));
                }
            }
        }

        Rows("damage_done", r.DamageDone, f => f.DamageDone, f => f.DamageSources);
        Rows("damage_taken", r.DamageTaken, f => f.DamageTaken, f => f.DamageTakenSources);
        Rows("healing", r.Healing, f => f.HealingDone, f => f.HealingSources);

        sb.AppendLine();
        sb.AppendLine("defender,attack_type,category,swings,hits,damage,min,max,average,crits,miss,parry,dodge,block,riposte,rune");
        foreach (DefenseStats d in r.Defenses)
        {
            foreach (IncomingAttackStats a in d.Attacks)
            {
                sb.AppendLine(string.Join(',',
                    Csv(d.Name), Csv(a.Type), Csv(a.Category), a.Swings, a.Hits, a.Damage,
                    a.MinHit, a.Max, a.Average.ToString("0.##", Inv), a.Crits,
                    a.Misses, a.Parries, a.Dodges, a.Blocks, a.Ripostes, a.Absorbs));
            }
        }

        sb.AppendLine();
        sb.AppendLine("mob,damage_taken,deaths,last_killer,time_to_kill_seconds");
        foreach (MobStats m in r.Mobs)
        {
            sb.AppendLine(string.Join(',',
                Csv(m.Name), m.DamageTaken, m.Deaths, Csv(m.LastKiller ?? ""),
                m.TimeToKillSeconds.ToString("0.##", Inv)));
        }

        return sb.ToString();
    }

    private static string Csv(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
}
