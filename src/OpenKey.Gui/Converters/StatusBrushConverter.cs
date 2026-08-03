using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using OpenKey.Gui.ViewModels;

namespace OpenKey.Gui.Converters;

/// <summary>
/// Maps a status severity to its accent colour.
/// <para>
/// Resolved from the active palette rather than hardcoded. It previously held four literal hex
/// values copied from the dark theme, which meant the status bar showed dark-theme colours in the
/// light theme — and injected the only four hues into <c>mono</c>, the one palette whose entire
/// purpose is to have none.
/// </para>
/// </summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        GuiTheme.Brush((value as StatusKind?) switch
        {
            StatusKind.Ok => "Ok",
            StatusKind.Warn => "Warn",
            StatusKind.Danger => "Danger",
            _ => "Brand",
        });

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps a status severity to its background tint.
/// <para>
/// The banner used to paint every severity on <c>SurfaceRaised</c>, which in the light theme is
/// pure white on a near-white page — a 1.088 step. The banner was effectively invisible and the
/// only thing distinguishing a deletion from a failure was a 2px line along one edge.
/// </para>
/// <para>
/// <c>mono</c> has no hue to spend, so its tints are a luminance ladder instead: the more serious
/// the message, the lighter the panel.
/// </para>
/// </summary>
public sealed class StatusSurfaceConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        GuiTheme.Brush((value as StatusKind?) switch
        {
            StatusKind.Ok => "OkSurface",
            StatusKind.Warn => "WarnSurface",
            StatusKind.Danger => "DangerSurface",
            _ => "InfoSurface",
        });

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
