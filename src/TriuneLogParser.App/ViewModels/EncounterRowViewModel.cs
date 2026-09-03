using TriuneLogParser.App.Services;

namespace TriuneLogParser.App.ViewModels;

public sealed class EncounterRowViewModel : ObservableObject
{
    public int Id { get; }

    private string _title = "";
    private string _durationText = "";
    private string _topText = "";
    private bool _isActive;
    private int _deaths;
    private long _totalDamage;

    public EncounterRowViewModel(EncounterSummary s)
    {
        Id = s.Id;
        StartText = s.Start.ToString("HH:mm:ss");
        Update(s);
    }

    public string StartText { get; }

    public string Title { get => _title; private set => Set(ref _title, value); }
    public string DurationText { get => _durationText; private set => Set(ref _durationText, value); }
    public string TopText { get => _topText; private set => Set(ref _topText, value); }
    public bool IsActive { get => _isActive; private set { if (Set(ref _isActive, value)) Raise(nameof(StatusGlyph)); } }
    public int Deaths { get => _deaths; private set { if (Set(ref _deaths, value)) Raise(nameof(DeathsText)); } }
    public long TotalDamage { get => _totalDamage; private set => Set(ref _totalDamage, value); }

    public string StatusGlyph => IsActive ? "●" : "";
    public string DeathsText => Deaths > 0 ? $"☠ {Deaths}" : "";

    public void Update(EncounterSummary s)
    {
        Title = s.Title;
        DurationText = FormatDuration(s.Duration);
        IsActive = s.IsActive;
        Deaths = s.Deaths;
        TotalDamage = s.TotalDamage;
        TopText = s.TotalDamage > 0 ? $"{s.TopFighter} · {s.TopDps:N0} dps" : "no damage";
    }

    private static string FormatDuration(TimeSpan d) =>
        d.TotalHours >= 1 ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}" : $"{d.Minutes}:{d.Seconds:00}";
}
