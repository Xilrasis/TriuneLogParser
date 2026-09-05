using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using TriuneLogParser.Core.Aggregation;
using TriuneLogParser.Core.Config;

namespace TriuneLogParser.App.ViewModels;

public sealed class OverlayBar : ObservableObject
{
    private long _value;
    private double _perSecond;
    private double _fraction;
    private string _centerText = "";
    private string _dpsText = "";
    private GridLength _nameCol = OverlayColumns.Default(0);
    private GridLength _midCol = OverlayColumns.Default(1);
    private GridLength _rateCol = OverlayColumns.Default(2);

    public required string Name { get; init; }
    public Brush Color { get; init; } = Brushes.SteelBlue;

    /// <summary>Shared bar-column widths, pushed from the view-model.</summary>
    public GridLength NameCol { get => _nameCol; set => Set(ref _nameCol, value); }
    public GridLength MidCol { get => _midCol; set => Set(ref _midCol, value); }
    public GridLength RateCol { get => _rateCol; set => Set(ref _rateCol, value); }

    public long Value { get => _value; set => Set(ref _value, value); }
    public double PerSecond { get => _perSecond; set => Set(ref _perSecond, value); }

    /// <summary>Share of the top bar's value, 0–1 — drives the bar-fill column width.</summary>
    public double Fraction { get => _fraction; set => Set(ref _fraction, value); }

    /// <summary>"1.75M  (42%)" — total and share, shown centred.</summary>
    public string CenterText { get => _centerText; set => Set(ref _centerText, value); }

    /// <summary>"9.7K/s" — the rate, shown in the right column.</summary>
    public string DpsText { get => _dpsText; set => Set(ref _dpsText, value); }
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
    private OverlayColumns _columns = new(new OverlaySettings());
    private double _lastDurationSeconds;
    private long _lastGrandTotal;

    public ObservableCollection<OverlayBar> Bars { get; } = new();

    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

    public OverlaySettings Settings
    {
        get => _settings;
        set { _settings = value; _columns = new OverlayColumns(value); RaiseSettings(); RaiseColumns(); }
    }

    // ---- bar columns (name / middle / rate), proportional, header-resizable ----
    public System.Windows.GridLength NameCol => _columns.Name;
    public System.Windows.GridLength MidCol => _columns.Mid;
    public System.Windows.GridLength RateCol => _columns.Rate;
    public bool ColumnsUnlocked => !_settings.Locked;

    public string MidColHeader => _settings.Metric switch
    {
        OverlayMetric.DamageTaken => "Taken",
        OverlayMetric.Healing => "Healing",
        _ => "Total",
    };

    /// <summary>Called from the header splitter drag (view). Persist is the caller's job.</summary>
    public void SetColumnFractions(double nameFraction, double rateFraction)
    {
        _columns.SetFractions(nameFraction, rateFraction);
        RaiseColumns();
        foreach (OverlayBar b in Bars)
            _columns.CopyTo(b);
    }

    private void RaiseColumns()
    {
        Raise(nameof(NameCol));
        Raise(nameof(MidCol));
        Raise(nameof(RateCol));
        Raise(nameof(ColumnsUnlocked));
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
        Raise(nameof(MidColHeader));
        Raise(nameof(ColumnsUnlocked));
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
            _lastGrandTotal = 0;
            _lastDurationSeconds = 0;
            return;
        }

        Title = encTitle ?? "Encounter";

        string dur = FormatDuration(durationSeconds);
        Subtitle = $"{(active ? "● " : "")}{dur} · {MetricLabel}";

        List<(string name, long value, double perSec)> rows = BuildRows(report, durationSeconds);
        long top = rows.Count > 0 ? rows[0].value : 0;

        // reconcile by position/name to avoid flicker
        var wanted = rows.Take(_settings.MaxRows).ToList();
        long grand = rows.Sum(r => r.value);
        _lastGrandTotal = grand;
        _lastDurationSeconds = durationSeconds;
        for (int i = 0; i < wanted.Count; i++)
        {
            (string name, long value, double perSec) = wanted[i];
            OverlayBar bar = i < Bars.Count && Bars[i].Name == name
                ? Bars[i]
                : GetOrCreate(name, i);

            bar.Value = value;
            bar.PerSecond = perSec;
            bar.Fraction = top > 0 ? (double)value / top : 0;
            string pct = grand > 0 ? $"({Fmt.Percent((double)value / grand)})" : "";
            bar.CenterText = $"{Fmt.Short(value)}  {pct}";
            bar.DpsText = Fmt.Rate(perSec);
            _columns.CopyTo(bar);
        }

        while (Bars.Count > wanted.Count)
            Bars.RemoveAt(Bars.Count - 1);
    }

    /// <summary>
    /// A single-line summary of the bars currently shown, safe to paste into EverQuest
    /// chat: plain ASCII only (EQ's bitmap font doesn't render most Unicode symbols,
    /// including the ones used in this app's own UI) and capped well under any
    /// channel's line-length limit, dropping the lowest contributors first.
    /// </summary>
    public string BuildChatSummary()
    {
        if (Bars.Count == 0)
            return "No parse data yet.";

        string header = $"{Title} ({FormatDuration(_lastDurationSeconds)}) {MetricLabel} -";
        string footer = _lastGrandTotal > 0
            ? $" | Total {Fmt.Short(_lastGrandTotal)} @ {Fmt.Rate(_lastGrandTotal / Math.Max(1, _lastDurationSeconds))}"
            : "";

        const int budget = 480; // comfortably under every EQ chat channel's line limit
        var chunks = new List<string>();
        int used = header.Length + footer.Length;

        foreach (OverlayBar b in Bars)
        {
            int pct = _lastGrandTotal > 0 ? (int)Math.Round(100.0 * b.Value / _lastGrandTotal) : 0;
            string chunk = $"{b.Name} {Fmt.Short(b.Value)} ({pct}%) {b.DpsText}";
            int add = chunk.Length + (chunks.Count > 0 ? 2 : 0);
            if (used + add > budget && chunks.Count > 0)
                break;
            chunks.Add(chunk);
            used += add;
        }

        string body = string.Join(", ", chunks);
        if (chunks.Count < Bars.Count)
            body += $" (+{Bars.Count - chunks.Count} more)";

        return $"{header} {body}{footer}";
    }

    private static string FormatDuration(double seconds) =>
        TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

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
