using System.Collections.ObjectModel;
using System.Windows.Media;
using TriuneLogParser.Core.Aggregation;
using TriuneLogParser.Core.Config;

namespace TriuneLogParser.App.ViewModels;

/// <summary>
/// Experimental "branch" overlay: one player's damage (or heal / damage-taken) sources
/// as bars, in the same visual style as the main overlay. Opened by clicking a bar in
/// the main overlay; kept as its own window/VM so it can be iterated on independently.
/// </summary>
public sealed class BranchOverlayViewModel : ObservableObject
{
    private static readonly Brush[] Palette =
    {
        Brush("#4c8dff"), Brush("#5bbf6a"), Brush("#e0a13c"), Brush("#c26bd8"),
        Brush("#e5636b"), Brush("#3fb6c9"), Brush("#9aa7f2"), Brush("#c9a24b"),
        Brush("#7bd88f"), Brush("#d87ba6"),
    };

    private string _title = "";
    private string _subtitle = "";
    private OverlaySettings _settings = new();
    private OverlayColumns _columns = new(new OverlaySettings());

    public ObservableCollection<OverlayBar> Bars { get; } = new();

    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

    public OverlaySettings Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            _columns = new OverlayColumns(value);
            Raise(nameof(Scale));
            RaiseColumns();
        }
    }

    public double Scale => _settings.Scale;

    public System.Windows.GridLength NameCol => _columns.Name;
    public System.Windows.GridLength MidCol => _columns.Mid;
    public System.Windows.GridLength RateCol => _columns.Rate;
    public bool ColumnsUnlocked => !_settings.Locked;

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

    /// <summary>The main overlay owns the lock toggle; keep our splitter-enable binding in sync.</summary>
    public void NotifyLockChanged() => Raise(nameof(ColumnsUnlocked));

    /// <summary>Rebuild the bars for one fighter and the overlay's current metric.</summary>
    public void Update(string fighterName, FighterStats? fighter, double durationSeconds, OverlayMetric metric)
    {
        Title = fighterName;

        List<SourceBucket> buckets = fighter is null ? new() : metric switch
        {
            OverlayMetric.DamageTaken => fighter.DamageTakenSources,
            OverlayMetric.Healing => fighter.HealingSources,
            _ => fighter.DamageSources,
        };

        var rows = buckets.Where(b => b.Total > 0).OrderByDescending(b => b.Total).ToList();
        if (rows.Count == 0)
        {
            Bars.Clear();
            Subtitle = "no sources yet";
            return;
        }

        double s = durationSeconds > 0 ? durationSeconds : 1;
        long total = rows.Sum(b => b.Total);
        long top = rows[0].Total;
        Subtitle = $"{MetricWord(metric)} · {Fmt.Short(total)}";

        int cap = Math.Max(4, _settings.MaxRows + 4); // a few more than the main overlay
        var wanted = rows.Take(cap).ToList();

        for (int i = 0; i < wanted.Count; i++)
        {
            SourceBucket b = wanted[i];
            string label = Label(b);
            OverlayBar bar = i < Bars.Count && Bars[i].Name == label ? Bars[i] : GetOrCreate(label, i);

            bar.Value = b.Total;
            bar.PerSecond = b.Total / s;
            bar.Fraction = top > 0 ? (double)b.Total / top : 0;
            int pct = total > 0 ? (int)Math.Round(100.0 * b.Total / total) : 0;
            bar.CenterText = $"{Fmt.Short(b.Total)}  ({pct}%)";
            bar.DpsText = Fmt.Rate(b.Total / s);
            _columns.CopyTo(bar);
        }

        while (Bars.Count > wanted.Count)
            Bars.RemoveAt(Bars.Count - 1);
    }

    public void Clear()
    {
        Bars.Clear();
        Title = "";
        Subtitle = "";
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

    private static string Label(SourceBucket b) =>
        b.PetName is { } p ? $"{p}: {b.Name}" : b.Name;

    private static string MetricWord(OverlayMetric m) => m switch
    {
        OverlayMetric.DamageTaken => "Damage taken",
        OverlayMetric.Healing => "Healing",
        _ => "Damage",
    };

    private static SolidColorBrush Brush(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }
}
