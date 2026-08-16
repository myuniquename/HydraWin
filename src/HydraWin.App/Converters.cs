using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace HydraWin.App;

/// <summary>Turns a <c>#RRGGBB</c> string from the model into a brush for the row's colour chip.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Fallback = Brushes.Gray;
    private readonly Dictionary<string, SolidColorBrush> cache = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || hex.Length == 0)
        {
            return Fallback;
        }

        if (cache.TryGetValue(hex, out SolidColorBrush? cached))
        {
            return cached;
        }

        SolidColorBrush brush;
        try
        {
            brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
        }
        catch (FormatException)
        {
            // state.json is hand-editable, so a bad colour is a typo rather than a bug.
            brush = Fallback;
        }

        cache[hex] = brush;
        return brush;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Colours are only ever read from the model.");
}

/// <summary>Shows an element only when a count is greater than zero — the notification badge.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("One-way only.");
}

/// <summary>Shows an element when a flag is <see langword="false"/>.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("One-way only.");
}

/// <summary>Shows an element only when a string has content.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("One-way only.");
}
