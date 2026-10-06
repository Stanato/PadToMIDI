using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Data.Converters;

namespace PadToMIDI.App.ViewModels.Settings;

/// <summary>Presentation only: readable labels keep enum-backed assignments strongly typed.</summary>
public sealed class EnumLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Enum item ? Regex.Replace(item.ToString(), "([a-z0-9])([A-Z])", "$1 $2") : value?.ToString();
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => AvaloniaProperty.UnsetValue;
}
