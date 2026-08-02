using System.Globalization;
using System.Text;

namespace OpenKey.Ui;

/// <summary>
/// How many terminal cells a run of text occupies.
/// <para>
/// Needed because <c>string.Length</c> is wrong in three ways that all show up in LLM output:
/// CJK and emoji occupy two cells, combining marks and zero-width joiners occupy none, and a
/// surrogate pair is two chars but one glyph. Getting this wrong makes the streaming rewind erase
/// the wrong number of rows, which damages the transcript above.
/// </para>
/// <para>
/// Spectre has an equivalent calculator but it is internal in 0.57.2, so this is deliberately
/// self-contained.
/// </para>
/// </summary>
internal static class TextWidth
{
    /// <summary>Cells occupied by a single rune: 0 for zero-width, 2 for wide, otherwise 1.</summary>
    public static int Of(Rune rune)
    {
        var value = rune.Value;

        if (value == 0) return 0;

        // Combining marks, joiners and variation selectors render into the preceding cell.
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.Format)
        {
            return 0;
        }

        if (Rune.IsControl(rune)) return 0;

        return IsWide(value) ? 2 : 1;
    }

    /// <summary>Cells occupied by a string, ignoring control characters.</summary>
    public static int Of(string text)
    {
        var total = 0;
        foreach (var rune in text.EnumerateRunes()) total += Of(rune);
        return total;
    }

    /// <summary>
    /// East Asian Wide and Fullwidth ranges, plus the emoji blocks that terminals render double
    /// width. Ranges rather than a full property table: this only has to be right for text a chat
    /// model actually emits.
    /// </summary>
    private static bool IsWide(int cp) =>
        (cp >= 0x1100 && cp <= 0x115F) ||     // Hangul Jamo
        (cp >= 0x2E80 && cp <= 0x303E) ||     // CJK radicals, Kangxi
        (cp >= 0x3041 && cp <= 0x33FF) ||     // Hiragana, Katakana, CJK compatibility
        (cp >= 0x3400 && cp <= 0x4DBF) ||     // CJK Extension A
        (cp >= 0x4E00 && cp <= 0x9FFF) ||     // CJK Unified Ideographs
        (cp >= 0xA000 && cp <= 0xA4CF) ||     // Yi
        (cp >= 0xAC00 && cp <= 0xD7A3) ||     // Hangul syllables
        (cp >= 0xF900 && cp <= 0xFAFF) ||     // CJK compatibility ideographs
        (cp >= 0xFE10 && cp <= 0xFE19) ||     // vertical forms
        (cp >= 0xFE30 && cp <= 0xFE6F) ||     // CJK compatibility forms
        (cp >= 0xFF00 && cp <= 0xFF60) ||     // fullwidth forms
        (cp >= 0xFFE0 && cp <= 0xFFE6) ||
        (cp >= 0x1F300 && cp <= 0x1F64F) ||   // emoji, emoticons
        (cp >= 0x1F900 && cp <= 0x1F9FF) ||   // supplemental symbols and pictographs
        (cp >= 0x20000 && cp <= 0x3FFFD);     // CJK Extension B and beyond
}
