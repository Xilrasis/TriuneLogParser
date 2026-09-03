using System.Collections.ObjectModel;
using System.Windows.Media;
using TriuneLogParser.Core.Aggregation;
using TriuneLogParser.Core.Config;

namespace TriuneLogParser.App.ViewModels;

public sealed class OverlayBar : ObservableObject
{
    private long _value;
    private double _perSecond;
    private double _fraction;
    private string _valueText = "";
    private string _rateText = "";
    private string _shareText = "";

    public required string Name { get; init; }
    public Brush Color { get; init; } = Brushes.SteelBlue;

    public long Value { get => _value; set => Set(ref _value, value); }
    public double PerSecond { get => _perSecond; set => Set(ref _perSecond, value); }
    public double Fraction { get => _fraction; set => Set(ref _fraction, value); }

    public string ValueText { get => _valueText; set => Set(ref _valueText, value); }
    public string RateText { get => _rateText; set => Set(ref _rateText, value); }
    public string ShareText { get => _shareText; set => Set(ref _shareText, value); }
}

/// <summary>Drives the always-on-top overlay: a short ranked list of damage-meter bars.</summary>
public sealed class OverlayViewModel : ObservableObject
{
    private static readonly Brush[] Palette =
    {
        Brush("#4c8dff"), Brush("#5bbf6a"), Brush("#e0a13c"), Brush("#c26bd8"),
        Brush("#e5636b"), Brush("#3fb6c9"), Brush("#9aa7f2"), Brush("#c9a24b"),
        Brush("#7bd88f"), Brush("#d87ba6"),
    };

    private string _title = "Waiting for combat…";
    private string _subtitle = "";
    private OverlaySettings _settings = new();

    public ObservableCollection<OverlayBar> Bars { get; } = new();

    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

    public OverlaySettings Settings
    {
        get => _settings;
        set { _settings = value; RaiseSettings(); }
    }

    public double Scale => _settings.Scale;
    public int MetricIndex => (int)_settings.Metric;
    public string MetricLabel => _settings.Metric switch
    {
        OverlayMetric.Dps => "DPS",
        OverlayMetric.Damage => "Damage",
        OverlayMetric.DamagePlusHealing => "Damage + heals",
        OverlayMetric.DamageTaken => "Damage taken",
        _ => "Healing",
    };

    public void RaiseSettings()
    {
        Raise(nameof(Scale));
        Raise(nameof(MetricIndex));
        Raise(nameof(MetricLabel));
    }

    public void CycleMetric(int delta)
    {
        int n = Enum.GetValues<OverlayMetric>().Length;
        _settings.Metric = (OverlayMetric)(((int)_settings.Metric + delta % n + n) % n);
        RaiseSettings();
    }

    /// <summary>Refresh bars from the current encounter report (null → clear).</summary>
    public void Update(EncounterReport? report, string? encTitle, double durationSeconds, bool active)
    {
        if (report is null || report.DamageDone.Count == 0 && report.Healing.Count == 0 && report.DamageTaken.Count == 0)
        {
            Bars.Clear();
            Title = "Waiting for combat…";
            Subtitle = "";
            return;
        }

        Title = encTitle ?? "Encounter";
        string dur = TimeSpan.FromSeconds(durationSeconds).ToString(durationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
        Subtitle = $"{(active ? "● " : "")}{dur} · {MetricLabel}";

        List<(string name, long value, double perSec)> rows = BuildRows(report, durationSeconds);
        long top = rows.Count > 0 ? rows[0].value : 0;

        // reconcile by position/name to avoid flicker
        var wanted = rows.Take(_settings.MaxRows).ToList();
        long grand = rows.Sum(r => r.value);
        for (int i = 0; i < wanted.Count; i++)
        {
            (string name, long value, double perSec) = wanted[i];
            OverlayBar bar = i < Bars.Count && Bars[i].Name == name
                ? Bars[i]
                : GetOrCreate(name, i);

            bar.Value = value;
            bar.PerSecond = perSec;
            bar.Fraction = top > 0 ? (double)value / top : 0;
            bar.ValueText = Fmt.Pad(Fmt.Short(value), 7);
            bar.RateText = Fmt.Pad(Fmt.Rate(perSec), 8);
            bar.ShareText = Fmt.Pad(grand > 0 ? Fmt.Percent((double)value / grand) : "", 4);
        }

        while (Bars.Count > wanted.Count)
            Bars.RemoveAt(Bars.Count - 1);
    }

    private OverlayBar GetOrCreate(string name, int index)
    {
        OverlayBar bar = new() { Name = name, Color = Palette[index % Palette.Length] };
        if (index < Bars.Count)
            Bars[index] = bar;
        else
            Bars.Add(bar);
        return bar;
    }

    private List<(string name, long value, double perSec)> BuildRows(EncounterReport report, double seconds)
    {
        double s = seconds > 0 ? seconds : 1;

        IEnumerable<(string, long, double)> Rows(IReadOnlyList<FighterStats> src, Func<FighterStats, long> pick) =>
            src.Where(f => pick(f) > 0).Select(f => (f.Name, pick(f), pick(f) / s));

        var result = _settings.Metric switch
        {
            OverlayMetric.Dps or OverlayMetric.Damage =>
                Rows(report.DamageDone, f => f.DamageDone),
            OverlayMetric.DamageTaken =>
                Rows(report.DamageTaken, f => f.DamageTaken),
            OverlayMetric.Healing =>
                Rows(report.Healing, f => f.HealingDone),
            _ => CombinedDamageHealing(report, s),
        };

        return result.OrderByDescending(r => r.Item2).ToList();
    }

    private static IEnumerable<(string, long, double)> CombinedDamageHealing(EncounterReport report, double s)
    {
        var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (FighterStats f in report.DamageDone)
            totals[f.Name] = totals.GetValueOrDefault(f.Name) + f.DamageDone;
        foreach (FighterStats f in report.Healing)
            totals[f.Name] = totals.GetValueOrDefault(f.Name) + f.HealingDone;
        return totals.Where(kv => kv.Value > 0).Select(kv => (kv.Key, kv.Value, kv.Value / s));
    }

    private static SolidColorBrush Brush(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }
}
