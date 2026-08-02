namespace OpenKey.Ui;

/// <summary>
/// Every colour the app uses, as Spectre style names.
/// <para>
/// Governing rule: <b>one accent, one neutral, three signals</b>. Colour carries meaning, never
/// decoration; body text is never coloured; no background colours anywhere.
/// </para>
/// <para>
/// Only the 16 base ANSI colours appear here. Legacy conhost downsamples anything richer, and the
/// "nicer" greys (grey19, grey23) land on black or silver depending on the user's scheme — so they
/// either vanish or invert. Base-16 renders identically everywhere, which is also why no
/// capability-degradation step is needed: there is nothing left to downsample, and Spectre strips
/// SGR itself under NO_COLOR.
/// </para>
/// <para>
/// <b>Accessibility.</b> Colour is never the only signal. Roughly 8% of men cannot reliably
/// separate red from green, so every state that uses those also carries a glyph or a worded title:
/// success is <c>✓ …</c>, failure is <c>✗ …</c>, and error cards name the problem in their header.
/// Removing all colour must never remove information — that is what the <c>mono</c> palette tests.
/// </para>
/// <para>A colour literal anywhere outside this type is a defect.</para>
/// </summary>
internal static class Theme
{
    /// <summary>Wordmark, AI label, caret, command tokens, model ids, panel titles.</summary>
    public static string Brand { get; private set; } = "aqua";

    /// <summary>Names something: labels, panel headers, column heads.</summary>
    public static string BrandStrong { get; private set; } = "bold aqua";

    /// <summary>Bold with no colour — markdown strong, the username.</summary>
    public static string Strong => "bold";

    /// <summary>Hints, elapsed time, borders, secondary detail. Never something the user must act on.</summary>
    public static string Muted { get; private set; } = "grey";

    /// <summary>The success glyph, and at most one leading word. Never a whole sentence.</summary>
    public static string Ok { get; private set; } = "green";

    /// <summary>Card border and title when the state is recoverable.</summary>
    public static string Warn { get; private set; } = "yellow";

    /// <summary>Card border and title when the state is fatal. Never body text.</summary>
    public static string Danger { get; private set; } = "red";

    /// <summary>Markdown inline code. Foreground only — no background.</summary>
    public static string Code { get; private set; } = "aqua";

    /// <summary>Currently applied palette name.</summary>
    public static string Current { get; private set; } = OpenKeyConfigThemes.Default;

    // Body text deliberately has no entry: it must inherit the terminal's own foreground.
    // Hardcoding white breaks every light-background console.

    public static bool IsKnown(string name) => OpenKeyConfigThemes.All.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static void Apply(string name)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case OpenKeyConfigThemes.Light:
                // Aqua and yellow are washed out on a white background; the darker pair holds up.
                Brand = "blue";
                BrandStrong = "bold blue";
                Muted = "grey";
                Ok = "green";
                Warn = "olive";
                Danger = "maroon";
                Code = "blue";
                Current = OpenKeyConfigThemes.Light;
                break;

            case OpenKeyConfigThemes.Mono:
                // No hue at all. Everything must still be legible, which is the real test that
                // meaning never depended on colour in the first place.
                Brand = "default";
                BrandStrong = "bold";
                Muted = "grey";
                Ok = "default";
                Warn = "default";
                Danger = "bold";
                Code = "default";
                Current = OpenKeyConfigThemes.Mono;
                break;

            case OpenKeyConfigThemes.Dark:
            case OpenKeyConfigThemes.Default:
            default:
                Brand = "aqua";
                BrandStrong = "bold aqua";
                Muted = "grey";
                Ok = "green";
                Warn = "yellow";
                Danger = "red";
                Code = "aqua";
                Current = name?.Trim().ToLowerInvariant() == OpenKeyConfigThemes.Dark
                    ? OpenKeyConfigThemes.Dark
                    : OpenKeyConfigThemes.Default;
                break;
        }
    }
}

internal static class OpenKeyConfigThemes
{
    public const string Default = "default";
    public const string Dark = "dark";
    public const string Light = "light";
    public const string Mono = "mono";

    public static readonly string[] All = { Default, Dark, Light, Mono };
}
