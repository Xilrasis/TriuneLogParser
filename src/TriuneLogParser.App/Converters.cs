using System.Globalization;
using System.Windows.Data;

namespace TriuneLogParser.App;

/// <summary>Multiplies all bound double values together (used for bar width = fraction × available width).</summary>
public sealed class MultiplyConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        double result = 1;
        foreach (object v in values)
        {
            if (v is double d && !double.IsNaN(d))
                result *= d;
            else
                return 0d;
        }

        return Math.Max(0, result);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Fraction (0..1) → a star <see cref="System.Windows.GridLength"/> for a proportional bar column.</summary>
public sealed class FractionStarConverter : IValueConverter
{
    public bool Rest { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double f = value is double d && !double.IsNaN(d) ? Math.Clamp(d, 0, 1) : 0;
        double weight = Rest ? 1 - f : f;
        return new System.Windows.GridLength(Math.Max(0.0001, weight), System.Windows.GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Depth (int) → left Thickness for tree indentation.</summary>
public sealed class IndentMarginConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int depth = value is int d ? d : 0;
        return new System.Windows.Thickness(depth * 16, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Empty/whitespace string → Collapsed, otherwise Visible.</summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string)
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True when the bound value's string form equals the parameter (enum → is-selected).</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? Enum.Parse(targetType, parameter.ToString()!) : Binding.DoNothing;
}
