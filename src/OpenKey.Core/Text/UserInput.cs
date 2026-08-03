namespace OpenKey.Core.Text;

/// <summary>
/// Cleans a line of typed or pasted input before anything looks at it.
/// <para>
/// Shared by the hosts rather than reimplemented in each, so a command is recognised on identical
/// terms everywhere. Pasted text is the case that matters: it arrives from editors, web pages and
/// files, carrying characters nobody typed and nobody can see.
/// </para>
/// <para>
/// Every character below is written as an escape rather than pasted literally. A source file full
/// of invisible characters cannot be reviewed, and the next person to edit it would have no way to
/// tell them apart — the same class of problem this class exists to fix.
/// </para>
/// </summary>
public static class UserInput
{
    private const char ByteOrderMark = '\uFEFF';

    /// <summary>
    /// Strips invisible characters that would otherwise sit in front of a command and stop it being
    /// recognised, then trims trailing space.
    /// <para>
    /// The concrete failure: a UTF-8 file read with its byte-order mark intact yields a line whose
    /// first character is U+FEFF, so <c>/help</c> does not start with <c>/</c> and is sent to the
    /// model as a message instead of running. A leading space does the same thing and is far easier
    /// to hit — command detection ran on the raw string, so <c>" /help"</c> was never a command
    /// either.
    /// </para>
    /// </summary>
    public static string Normalize(string? raw)
    {
        var text = StripInvisible(raw);

        // Both classes in one pass, because they interleave: a paste can easily start with a BOM,
        // then a couple of spaces, then a zero-width space. Consuming only whitespace here would
        // stop at that zero-width character and leave it in front of the slash — which is the exact
        // bug this method exists to prevent.
        var start = 0;
        while (start < text.Length && (char.IsWhiteSpace(text[start]) || IsZeroWidth(text[start])))
            start++;

        return text[start..].TrimEnd();
    }

    /// <summary>
    /// Removes characters that render as nothing, and leaves every visible one — including leading
    /// spaces — exactly where it was.
    /// <para>
    /// This is the right choice for a message body, where <see cref="Normalize"/> is not: the GUI
    /// composer accepts multi-line input, so trimming the front would silently eat the indentation
    /// of the first line of a pasted code block.
    /// </para>
    /// </summary>
    public static string StripInvisible(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        // U+FEFF is removed everywhere, not just at the start: as a byte-order mark it is an
        // encoding artifact, and its original zero-width-no-break-space meaning has been deprecated
        // in favour of U+2060, so it never carries intent in a chat message.
        var text = raw.Contains(ByteOrderMark)
            ? raw.Replace(ByteOrderMark.ToString(), string.Empty)
            : raw;

        // The rest are stripped only from the front. U+200D is load-bearing inside emoji
        // sequences — removing it throughout would break a family emoji into separate people.
        var start = 0;
        while (start < text.Length && IsZeroWidth(text[start])) start++;

        return text[start..];
    }

    /// <summary>
    /// True for characters that occupy no visual space at all. Ordinary and non-breaking spaces are
    /// deliberately excluded — they are handled by the trim in <see cref="Normalize"/>, which the
    /// message path does not apply.
    /// </summary>
    private static bool IsZeroWidth(char c) =>
        c is '\u200B'      // zero-width space
             or '\u200C'      // zero-width non-joiner
             or '\u200D'      // zero-width joiner
             or '\u2060'      // word joiner
             or '\u180E';     // Mongolian vowel separator
}
