using System.Windows;
using TriuneLogParser.Core.Config;

namespace TriuneLogParser.App.ViewModels;

/// <summary>
/// The three shared bar columns (name / middle / rate) for an overlay, as star
/// <see cref="GridLength"/>s derived from the persisted fractions in
/// <see cref="OverlaySettings"/>. Columns stay proportional on window resize; a header
/// splitter drag calls <see cref="SetFractions"/>.
/// </summary>
public sealed class OverlayColumns
{
    private readonly OverlaySettings _settings;

    public OverlayColumns(OverlaySettings settings) => _settings = settings;

    public GridLength Name => new(_settings.NameColFraction, GridUnitType.Star);
    public GridLength Mid => new(MidFraction, GridUnitType.Star);
    public GridLength Rate => new(_settings.RateColFraction, GridUnitType.Star);

    public double MidFraction =>
        Math.Max(0.1, 1.0 - _settings.NameColFraction - _settings.RateColFraction);

    /// <summary>Set the name / rate fractions (middle takes the rest); values are clamped.</summary>
    public void SetFractions(double name, double rate)
    {
        _settings.NameColFraction = name;
        _settings.RateColFraction = rate;
        _settings.Clamp();
    }

    public void CopyTo(OverlayBar bar)
    {
        bar.NameCol = Name;
        bar.MidCol = Mid;
        bar.RateCol = Rate;
    }

    /// <summary>Placeholder star length for a fresh bar before the view-model fills it in.</summary>
    public static GridLength Default(int col) => col switch
    {
        0 => new GridLength(0.40, GridUnitType.Star),
        2 => new GridLength(0.24, GridUnitType.Star),
        _ => new GridLength(0.36, GridUnitType.Star),
    };
}
