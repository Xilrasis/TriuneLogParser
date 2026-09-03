using System.Globalization;

namespace TriuneLogParser.App;

/// <summary>
/// Number formatting helpers. Right-alignment via <see cref="System.Windows.Controls.TextBlock"/>
/// <c>TextAlignment</c> does not render on some Windows 11 builds, so numbers are shown
/// in a monospace font left-padded to a fixed width instead.
/// </summary>
public static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Compact damage number: 12345 → "12.3K", 1234567 → "1.23M".</summary>
    public static string Short(double v)
    {
        double a = Math.Abs(v);
        return a >= 1_000_000_000 ? (v / 1e9).ToString("0.##", Inv) + "B"
             : a >= 1_000_000 ? (v / 1e6).ToString("0.##", Inv) + "M"
             : a >= 10_000 ? (v / 1e3).ToString("0.#", Inv) + "K"
             : a >= 1_000 ? (v / 1e3).ToString("0.##", Inv) + "K"
             : v.ToString("0", Inv);
    }

    public static string Full(long v) => v.ToString("N0", Inv);

    public static string Rate(double perSecond) => Short(perSecond) + "/s";

    public static string Percent(double fraction) => fraction.ToString("P0", Inv);

    /// <summary>Left-pad to <paramref name="width"/> so a monospace column reads right-aligned.</summary>
    public static string Pad(string s, int width) => s.Length >= width ? s : s.PadLeft(width);
}
