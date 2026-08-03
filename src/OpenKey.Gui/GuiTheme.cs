using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace OpenKey.Gui;

/// <summary>
/// Applies a palette to the running application.
/// <para>
/// Same governing rule as the console: one accent, one neutral, three signals. Colour carries
/// meaning, never decoration, and no state is signalled by colour alone — which is what makes the
/// <c>mono</c> palette a legitimate option rather than a novelty.
/// </para>
/// <para>
/// Themes are shared with the console through <c>config.json</c>, so switching in one and opening
/// the other keeps your choice. Rendering is per-host; the setting is not.
/// </para>
/// </summary>
internal static class GuiTheme
{
    public const string Default = "default";
    public const string Dark = "dark";
    public const string Light = "light";
    public const string Mono = "mono";

    public static readonly string[] All = { Default, Dark, Light, Mono };

    public static string Current { get; private set; } = Default;

    public static bool IsKnown(string? name) =>
        name is not null && All.Contains(name.Trim().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);

    public static void Apply(Application app, string? name)
    {
        var theme = (name ?? Default).Trim().ToLowerInvariant();
        if (!IsKnown(theme)) theme = Default;
        Current = theme;

        app.RequestedThemeVariant = theme == Light ? ThemeVariant.Light : ThemeVariant.Dark;

        var r = app.Resources;

        switch (theme)
        {
            case Light:
                Set(r, "Surface", "#F8FAFC");
                Set(r, "SurfaceRaised", "#EEF2F7");
                Set(r, "CodeSurface", "#F1F5F9");
                Set(r, "Line", "#CBD5E1");
                Set(r, "Body", "#0F172A");
                Set(r, "Muted", "#64748B");
                Set(r, "Brand", "#0E7490");
                Set(r, "Ok", "#15803D");
                Set(r, "Warn", "#B45309");
                Set(r, "Danger", "#B91C1C");
                Set(r, "OnBrand", "#F8FAFC");
                Set(r, "CodeKeyword", "#7C3AED");
                Set(r, "CodeString", "#047857");
                Set(r, "CodeComment", "#94A3B8");
                Set(r, "CodeNumber", "#B45309");
                Set(r, "CodeType", "#0E7490");
                break;

            case Mono:
                // No hue at all. Everything must stay legible, which is the standing test that
                // meaning never depended on colour in the first place.
                Set(r, "Surface", "#101010");
                Set(r, "SurfaceRaised", "#1B1B1B");
                Set(r, "CodeSurface", "#161616");
                Set(r, "Line", "#3A3A3A");
                Set(r, "Body", "#E8E8E8");
                Set(r, "Muted", "#9A9A9A");
                Set(r, "Brand", "#E8E8E8");
                Set(r, "Ok", "#E8E8E8");
                Set(r, "Warn", "#C8C8C8");
                Set(r, "Danger", "#FFFFFF");
                Set(r, "OnBrand", "#101010");
                Set(r, "CodeKeyword", "#FFFFFF");
                Set(r, "CodeString", "#C8C8C8");
                Set(r, "CodeComment", "#7A7A7A");
                Set(r, "CodeNumber", "#C8C8C8");
                Set(r, "CodeType", "#E8E8E8");
                break;

            default:    // default and dark are the same palette; "dark" exists so the name works
                Set(r, "Surface", "#0F172A");
                Set(r, "SurfaceRaised", "#1E293B");
                Set(r, "CodeSurface", "#0B1220");
                Set(r, "Line", "#334155");
                Set(r, "Body", "#E2E8F0");
                Set(r, "Muted", "#94A3B8");
                Set(r, "Brand", "#22D3EE");
                Set(r, "Ok", "#4ADE80");
                Set(r, "Warn", "#FBBF24");
                Set(r, "Danger", "#F87171");
                Set(r, "OnBrand", "#04121A");
                Set(r, "CodeKeyword", "#C4B5FD");
                Set(r, "CodeString", "#86EFAC");
                Set(r, "CodeComment", "#64748B");
                Set(r, "CodeNumber", "#FCD34D");
                Set(r, "CodeType", "#7DD3FC");
                break;
        }
    }

    private static void Set(IResourceDictionary resources, string key, string hex) =>
        resources[key] = new SolidColorBrush(Color.Parse(hex));
}
