using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace OpenKey.Gui;

/// <summary>
/// Applies a palette to the running application.
/// <para>
/// Governing rule, uniform across every theme: <b>chrome recedes, content advances, overlays
/// float.</b> One direction, no exceptions, so the eye learns the structure immediately. The
/// previous palette did the reverse — header and sidebar sat <em>above</em> the transcript — which
/// is a large part of why the layout never resolved into planes.
/// </para>
/// <para>
/// These values are chosen for this app. The palette they replace was, every single token, an
/// unmodified Tailwind swatch (slate-900, cyan-400, amber-400…). That is what reads as generic —
/// not an impression, but a fact about where the numbers came from.
/// </para>
/// <para>
/// Every pair carrying text has a computed WCAG ratio; body text clears 4.5:1 and most clear 7:1.
/// <c>mono</c> is the standing proof that no state depends on hue, so it uses separated luminance
/// steps plus weight and italic. The mono palette it replaces collapsed Body, Brand, Ok and
/// CodeType onto one identical grey, which proved nothing.
/// </para>
/// <para>
/// Themes are shared with the console through <c>config.json</c>: the setting travels, the
/// rendering is per-host.
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
                // Four real planes ending in true white, warm but only just.
                //
                // Two rounds of feedback shaped this. The first version was flat — Surface and
                // SurfaceRaised sat 3 L* apart, a 1.07:1 step nobody can see, so the window was one
                // sheet held together by hairlines. Fixing that overcorrected into beige: the
                // surfaces read dingy, and every secondary label measured 5.0:1 against them, which
                // is AA but is also the floor, and it applied to *most of the chrome* — buttons,
                // captions, hints, timestamps. Body text was never the problem at 15:1.
                //
                // So: keep the plane separation (1.175 sunken-to-surface), drop most of the yellow,
                // and pull Muted to 7.2:1 so a secondary label is quiet rather than faint.
                Set(r, "SurfaceSunken", "#E8E4DD");
                Set(r, "Surface", "#F8F6F3");
                Set(r, "SurfaceRaised", "#FFFFFF");
                Set(r, "SurfaceOverlay", "#FFFFFF");
                Set(r, "CodeSurface", "#EDE9E2");
                Set(r, "Line", "#DDD8D0");
                Set(r, "LineStrong", "#BEB8AE");
                Set(r, "LineControl", "#6F685E");
                Set(r, "Body", "#1B1916");
                Set(r, "Muted", "#4D473F");
                Set(r, "Brand", "#4B45C9");
                Set(r, "BrandHover", "#5A54DA");
                Set(r, "BrandPressed", "#3B36A6");
                Set(r, "OnBrand", "#FFFFFF");
                Set(r, "Ok", "#146039");
                Set(r, "Warn", "#7A4A08");
                Set(r, "Danger", "#9E2620");
                // Tinted surfaces, one per severity. The status banner used to paint every message
                // on SurfaceRaised, which in this theme is pure white on a near-white page — a
                // 1.088 step, so the banner was invisible and only a 2px accent line carried the
                // meaning. These also give the destructive button a rest state that is not a solid
                // red slab, which beside neutral verbs would train people to ignore it.
                Set(r, "DangerSurface", "#F8E1DF");
                Set(r, "InfoSurface", "#E6E4F6");
                Set(r, "OkSurface", "#DCEFE2");
                Set(r, "WarnSurface", "#FAE8CE");
                Set(r, "DangerHover", "#9E2620");
                Set(r, "DangerPressed", "#821D18");
                Set(r, "OnDanger", "#FFFFFF");
                Set(r, "BrandDisabled", "#DEDCF0");
                Set(r, "SelectionUnfocused", "#E6E2DA");
                Set(r, "Hover", "#FFFFFF");
                Set(r, "Pressed", "#DFDAD1");     // on a light ground, pushed reads as *less* light
                Set(r, "Selection", "#DCD9EC");
                Set(r, "Focus", "#4B45C9");
                Set(r, "CodeText", "#24211D");
                Set(r, "CodeKeyword", "#4A41B4");
                Set(r, "CodeString", "#17663F");
                Set(r, "CodeComment", "#5E5648");
                Set(r, "CodeNumber", "#7E4E0D");
                Set(r, "CodeType", "#0B5566");
                // White is the ceiling in light, so menus and dialogs cannot separate by tone.
                // The shadow is load-bearing here, not decoration.
                SetShadow(r, "ShadowSoft", "0 1 2 0 #14000000");
                SetShadow(r, "ShadowOverlay", "0 8 24 0 #26000000");
                break;

            case Mono:
                // Achromatic on purpose. The six code roles are six separated luminance steps;
                // keyword takes SemiBold and comment italic, because the top two steps are too
                // close to separate on lightness alone. Weight and style are the legitimate
                // substitute for hue.
                Set(r, "SurfaceSunken", "#0B0B0B");
                Set(r, "Surface", "#141414");
                Set(r, "SurfaceRaised", "#1D1D1D");
                Set(r, "SurfaceOverlay", "#262626");
                // The code ramp is re-spaced and the block is pushed further from the page.
                // It previously sat 1.048 against Surface — no visible plane at all, so a snippet
                // did not read as a block — while three of the six roles clustered at the top of
                // the luminance range and comments sat at 4.7. Six greys is the most this palette
                // can carry, so the steps are now even and weight and italic do the rest.
                Set(r, "CodeSurface", "#060606");
                Set(r, "Line", "#2A2A2A");
                Set(r, "LineStrong", "#3F3F3F");
                Set(r, "LineControl", "#6E6E6E");
                Set(r, "Body", "#E4E4E4");
                Set(r, "Muted", "#A2A2A2");
                // Severity is a luminance ladder here, not a set of hues: Ok < Warn < Brand <
                // Danger, each step at least 1.25x the last. Brand sat 1.05:1 from Danger before,
                // which is indistinguishable — an accent and an alarm that look identical is
                // exactly the failure mono exists to rule out.
                Set(r, "Brand", "#E0E0E0");
                Set(r, "BrandHover", "#F5F5F5");
                Set(r, "BrandPressed", "#C4C4C4");
                Set(r, "OnBrand", "#0B0B0B");
                Set(r, "Ok", "#8E8E8E");
                Set(r, "Warn", "#C6C6C6");
                Set(r, "Danger", "#FFFFFF");
                Set(r, "DangerSurface", "#363636");
                Set(r, "InfoSurface", "#1E1E1E");
                Set(r, "OkSurface", "#242424");
                Set(r, "WarnSurface", "#2C2C2C");
                Set(r, "DangerHover", "#FFFFFF");
                Set(r, "DangerPressed", "#E0E0E0");
                Set(r, "OnDanger", "#0B0B0B");
                Set(r, "BrandDisabled", "#3A3A3A");
                Set(r, "SelectionUnfocused", "#1C1C1C");
                Set(r, "Hover", "#202020");
                Set(r, "Pressed", "#2B2B2B");
                Set(r, "Selection", "#242424");
                Set(r, "Focus", "#FFFFFF");
                Set(r, "CodeText", "#C0C0C0");
                Set(r, "CodeKeyword", "#EEEEEE");
                Set(r, "CodeString", "#A3A3A3");
                Set(r, "CodeComment", "#888888");
                Set(r, "CodeNumber", "#D3D3D3");
                Set(r, "CodeType", "#E0E0E0");
                SetShadow(r, "ShadowSoft", "0 1 2 0 #40000000");
                SetShadow(r, "ShadowOverlay", "0 10 28 0 #73000000");
                break;

            default:
                // Neutral dark rather than navy. The old surface was slate-900 (b* ≈ -16) with a
                // cyan accent only ~40° away in hue — accent and ground from one family, so the
                // accent vibrated instead of separating. Iris on a near-neutral ground gives the
                // accent somewhere to stand.
                Set(r, "SurfaceSunken", "#0E1014");
                Set(r, "Surface", "#161920");
                Set(r, "SurfaceRaised", "#1E222B");
                Set(r, "SurfaceOverlay", "#262B36");
                Set(r, "CodeSurface", "#0A0C10");
                Set(r, "Line", "#272C36");
                Set(r, "LineStrong", "#3A4150");
                Set(r, "LineControl", "#6A7283");
                Set(r, "Body", "#E6E9EF");
                Set(r, "Muted", "#A8B1C0");
                Set(r, "Brand", "#9FB1FF");
                Set(r, "BrandHover", "#B6C3FF");
                Set(r, "BrandPressed", "#8697EE");
                Set(r, "OnBrand", "#0E1120");
                Set(r, "Ok", "#5FD3A0");
                Set(r, "Warn", "#E7B45C");
                Set(r, "Danger", "#F2786E");
                Set(r, "DangerSurface", "#3A1F20");
                Set(r, "InfoSurface", "#1E2340");
                Set(r, "OkSurface", "#14302A");
                Set(r, "WarnSurface", "#332A1A");
                Set(r, "DangerHover", "#F2786E");
                Set(r, "DangerPressed", "#DC5F55");
                Set(r, "OnDanger", "#1A0C0B");
                Set(r, "BrandDisabled", "#2B3142");
                Set(r, "SelectionUnfocused", "#232733");
                Set(r, "Hover", "#20252F");
                Set(r, "Pressed", "#2A3040");
                Set(r, "Selection", "#232A3D");
                Set(r, "Focus", "#8FA2FF");
                Set(r, "CodeText", "#D5DAE3");
                Set(r, "CodeKeyword", "#A9B7FF");
                Set(r, "CodeString", "#7FCCA5");
                Set(r, "CodeComment", "#7B8698");
                Set(r, "CodeNumber", "#E2B77C");
                Set(r, "CodeType", "#79C6DA");
                SetShadow(r, "ShadowSoft", "0 1 2 0 #40000000");
                SetShadow(r, "ShadowOverlay", "0 10 28 0 #73000000");
                break;
        }
    }

    /// <summary>
    /// Resolves a token to a brush for code that cannot use <c>DynamicResource</c> — converters,
    /// and runs built in code-behind.
    /// </summary>
    public static IBrush Brush(string key) =>
        Application.Current?.Resources.TryGetResource(key, null, out var value) == true && value is IBrush brush
            ? brush
            : Brushes.Gray;

    private static void Set(IResourceDictionary resources, string key, string hex) =>
        resources[key] = new SolidColorBrush(Color.Parse(hex));

    private static void SetShadow(IResourceDictionary resources, string key, string value) =>
        resources[key] = BoxShadows.Parse(value);
}
