using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using OpenKey.Gui.ViewModels;

namespace OpenKey.Gui.Converters;

/// <summary>
/// Maps a status severity to its accent colour. Kept in one place for the same reason the console
/// keeps every colour in <c>Theme</c>: a brush chosen at a call site is how a palette drifts.
/// </summary>
public sealed class StatusBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Info = new(Color.Parse("#22D3EE"));
    private static readonly SolidColorBrush Ok = new(Color.Parse("#4ADE80"));
    private static readonly SolidColorBrush Warn = new(Color.Parse("#FBBF24"));
    private static readonly SolidColorBrush Danger = new(Color.Parse("#F87171"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as StatusKind?) switch
        {
            StatusKind.Ok => Ok,
            StatusKind.Warn => Warn,
            StatusKind.Danger => Danger,
            _ => Info,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
