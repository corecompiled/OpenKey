using Spectre.Console;

namespace OpenKey.Ui;

/// <summary>
/// Non-ASCII characters the app draws, resolved once at startup into a tier.
/// <para>
/// Windows Terminal has DirectWrite font fallback and renders anything. The GDI-rendered legacy
/// conhost does not fall back for glyphs missing from Consolas — it draws a box instead. So the
/// tier check is deliberately conservative: full Unicode only when we can see we are in Windows
/// Terminal with a UTF-8 codepage.
/// </para>
/// <para>A glyph literal anywhere outside this type is a defect.</para>
/// </summary>
internal static class Glyphs
{
    /// <summary>
    /// True only for Windows Terminal on codepage 65001. Conservative on purpose — the cost of a
    /// false positive is a row of boxes on every prompt.
    /// </summary>
    public static bool Unicode { get; private set; }

    /// <summary>Prompt caret. The single highest-risk glyph in the app: U+276F is in neither CP437 nor Consolas.</summary>
    public static string Caret { get; private set; } = ">";

    public static string Ok { get; private set; } = "+";
    public static string Fail { get; private set; } = "x";

    /// <summary>Separator. Safe in both tiers — CP437 0xFA, CP1252 0xB7.</summary>
    public static string Sep => "·";

    /// <summary>List bullet. A hyphen in both tiers: calmer than U+2022 and safe outside CP437.</summary>
    public static string Bullet => "-";

    /// <summary>Blockquote bar. Safe in both tiers — CP437.</summary>
    public static string QuoteBar => "│";

    public static string Ellipsis => "…";

    /// <summary>
    /// Braille dots on the Unicode tier only. Spinner.Known.Dots is U+28xx and absent from
    /// Consolas, and it runs on every single turn — the most likely visible breakage in the app.
    /// SimpleDots is pure ASCII and calmer than Line, which spins fast and reads retro-toy.
    /// </summary>
    public static Spinner Spinner => Unicode ? Spectre.Console.Spinner.Known.Dots : Spectre.Console.Spinner.Known.SimpleDots;

    /// <summary>Square borders in both tiers: the rounded set (U+256D–2570) is outside CP437.</summary>
    public static BoxBorder Box => BoxBorder.Square;

    public static TableBorder Table => TableBorder.Square;

    public static void Resolve()
    {
        Unicode = Console.OutputEncoding.CodePage == 65001
            && Environment.GetEnvironmentVariable("WT_SESSION") is not null;

        Caret = Unicode ? "❯" : ">";
        Ok = Unicode ? "✓" : "+";
        Fail = Unicode ? "✗" : "x";
    }
}
