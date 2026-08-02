namespace OpenKey.Ui;

/// <summary>
/// Every colour the app uses, as Spectre style names.
/// <para>
/// Governing rule: <b>one accent, one neutral, three signals</b>. Colour carries meaning, never
/// decoration; body text is never coloured; no background colours anywhere.
/// </para>
/// <para>
/// Only the 16 base ANSI colours appear here. Legacy conhost downsamples 256/truecolour, and the
/// "nicer" greys (grey19, grey23) land on black or silver depending on the user's scheme — so they
/// either vanish or invert. Base-16 renders identically everywhere, which is also why this type
/// needs no capability-degradation step: there is nothing left to downsample, and Spectre strips
/// SGR itself under NO_COLOR.
/// </para>
/// <para>
/// A colour literal anywhere outside this type is a defect.
/// </para>
/// </summary>
internal static class Theme
{
    /// <summary>Wordmark, AI label, caret, command tokens, model ids, panel titles.</summary>
    public const string Brand = "aqua";

    /// <summary>Names something: labels, panel headers, column heads.</summary>
    public const string BrandStrong = "bold aqua";

    /// <summary>Bold with no colour — markdown strong, the username.</summary>
    public const string Strong = "bold";

    /// <summary>Hints, elapsed time, borders, secondary detail. Never something the user must act on.</summary>
    public const string Muted = "grey";

    /// <summary>The success glyph, and at most one leading word. Never a whole sentence.</summary>
    public const string Ok = "green";

    /// <summary>Card border and title when the state is recoverable.</summary>
    public const string Warn = "yellow";

    /// <summary>Card border and title when the state is fatal. Never body text.</summary>
    public const string Danger = "red";

    /// <summary>Markdown inline code. Foreground only — no background.</summary>
    public const string Code = "aqua";

    // Body text deliberately has no constant: it must inherit the terminal's own foreground.
    // Hardcoding white breaks every light-background console.
}
