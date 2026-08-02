using System.Text;
using Spectre.Console;

namespace OpenKey.Ui;

/// <summary>
/// Streams a reply at block granularity: raw text appears as it arrives, and each time a markdown
/// block completes the raw lines are erased and replaced with the styled rendering.
/// <para>
/// <b>Why not <c>LiveDisplay</c>.</b> Spectre's live region clamps to the viewport and discards
/// overflow lines rather than scrolling them, and when the region shrinks it issues
/// <c>EraseInDisplay(2)</c> followed by <c>ClearScrollback()</c> — which would wipe the chat
/// transcript. It also has no fallback when output is redirected. None of that is survivable for a
/// scrolling conversation, so this writer drives the cursor directly instead.
/// </para>
/// <para>
/// The unstyled trailing text is intentional, not a limitation: styled means settled, raw means
/// still arriving.
/// </para>
/// </summary>
internal sealed class TranscriptWriter
{
    private readonly IAnsiConsole _console;
    private readonly StringBuilder _pending = new();

    /// <summary>Raw rows emitted since the last flush, including wrapped continuation rows.</summary>
    private int _rawRows;

    /// <summary>Column the raw cursor sits at, in terminal cells.</summary>
    private int _col;

    /// <summary>Width sampled when the current block started; a resize invalidates the rewind.</summary>
    private int _blockWidth;

    private bool _widthChanged;
    private bool _inFence;
    private bool _anyOutput;

    /// <summary>Styled blocks emitted so far. Distinct from <see cref="_anyOutput"/>, which also
    /// counts raw streamed text that gets erased again.</summary>
    private int _blocksRendered;

    /// <summary>
    /// Whether this writer may stream raw text and erase it again.
    /// <para>
    /// Judged <em>solely</em> from the console it was handed. It previously also consulted
    /// <c>ConsoleLayout.Rich</c>, which is global mutable state no caller controls — so the same
    /// code streamed raw text on one machine and not on another, and the tests could not pin it
    /// down. <c>caps.Ansi</c> is already false whenever output is redirected, which is the only
    /// thing the global added.
    /// </para>
    /// </summary>
    private readonly bool _canRewind;

    public TranscriptWriter(IAnsiConsole console)
    {
        _console = console;
        _canRewind = console.Profile.Capabilities.Ansi;
        _blockWidth = ConsoleLayout.CurrentWidth();
    }

    /// <summary>True once any text has been written, styled or raw.</summary>
    public bool HasContent => _anyOutput || _pending.Length > 0;

    public void Append(string delta)
    {
        if (string.IsNullOrEmpty(delta)) return;

        _pending.Append(delta);

        // Never stream a fence raw, and note the test is "does the pending text contain a fence
        // marker at all", not "are we currently inside one". A single delta can carry both the
        // opening and closing marker, which nets to not-inside — and that used to let a whole
        // code block reach the screen as raw backticks before the flush replaced it.
        UpdateFenceState(delta);
        if (!_inFence && !HasFenceMarker()) WriteRaw(delta);

        FlushCompletedBlocks();
    }

    /// <summary>Renders whatever is left, including an unterminated fence.</summary>
    public void Complete()
    {
        var rest = _pending.ToString();
        _pending.Clear();
        if (rest.Trim().Length == 0)
        {
            if (_rawRows > 0) Rewind();
            return;
        }

        Rewind();
        RenderBlock(rest);
        _inFence = false;
    }

    /// <summary>
    /// Discards everything from an abandoned attempt. Called on
    /// <see cref="Core.Providers.ChatChunk.IsAttemptRestart"/> — without it a mid-reply rotation
    /// renders the answer twice concatenated.
    /// </summary>
    public void Reset()
    {
        Rewind();
        _pending.Clear();
        _inFence = false;
        _blocksRendered = 0;
        _anyOutput = false;
    }

    private void FlushCompletedBlocks()
    {
        while (true)
        {
            var text = _pending.ToString();
            var cut = FindBlockEnd(text);
            if (cut < 0) return;

            var block = text[..cut];
            _pending.Remove(0, cut);

            if (block.Trim().Length == 0) continue;

            Rewind();
            RenderBlock(block);
        }
    }

    /// <summary>
    /// Returns the index just past a complete block, or -1. Outside a fence a block ends at a blank
    /// line; a fence ends only at its closing marker, so a blank line inside code never splits it.
    /// </summary>
    private static int FindBlockEnd(string text)
    {
        var fenceOpen = false;
        var lineStart = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;

            var line = text.AsSpan(lineStart, i - lineStart).TrimEnd('\r');

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (fenceOpen) return i + 1;    // closing fence completes the block
                fenceOpen = true;
            }
            else if (!fenceOpen && line.Trim().IsEmpty && lineStart > 0)
            {
                return i + 1;                   // blank line completes a prose block
            }

            lineStart = i + 1;
        }

        return -1;
    }

    private bool HasFenceMarker()
    {
        for (var i = 0; i + 2 < _pending.Length; i++)
        {
            if (_pending[i] == '`' && _pending[i + 1] == '`' && _pending[i + 2] == '`') return true;
        }
        return false;
    }

    private void UpdateFenceState(string delta)
    {
        var text = _pending.ToString();
        var open = false;
        var lineStart = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != '\n') continue;
            var line = text.AsSpan(lineStart, Math.Min(i, text.Length) - lineStart).TrimEnd('\r');
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) open = !open;
            lineStart = i + 1;
        }
        _inFence = open;
    }

    private void WriteRaw(string delta)
    {
        if (!_canRewind)
        {
            // Nothing could be erased later, so emit nothing now and let the styled render at each
            // block boundary be the only output.
            return;
        }

        var width = ConsoleLayout.CurrentWidth();
        if (width != _blockWidth) _widthChanged = true;

        _console.Write(delta);
        _anyOutput = true;
        CountRows(delta, width);
    }

    /// <summary>
    /// Tracks how many terminal rows the raw text occupies. Counting characters would be wrong:
    /// CJK and emoji are double-width, combining marks are zero-width, and tabs jump to stops.
    /// Wrapping is computed at <c>width - 1</c> because terminals disagree about whether a glyph
    /// landing exactly on the last column wraps immediately (conhost) or is deferred (Windows
    /// Terminal) — staying a column short makes the count agree with both.
    /// </summary>
    private void CountRows(string text, int width)
    {
        var usable = Math.Max(1, width - 1);

        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == '\n') { _rawRows++; _col = 0; continue; }
            if (rune.Value == '\r') { _col = 0; continue; }

            if (rune.Value == '\t')
            {
                var next = ((_col / 8) + 1) * 8;
                if (next >= usable) { _rawRows++; _col = 0; } else { _col = next; }
                continue;
            }

            var w = TextWidth.Of(rune);
            if (w <= 0) continue;                                  // combining mark / ZWJ
            if (_col + w > usable) { _rawRows++; _col = 0; }
            _col += w;
        }
    }

    /// <summary>
    /// Erases the raw text written since the last flush so the styled version can replace it.
    /// Skipped — leaving the raw text in place — whenever erasing would be wrong rather than
    /// merely ugly.
    /// </summary>
    private void Rewind()
    {
        var rows = _rawRows;
        var col = _col;
        _rawRows = 0;
        _col = 0;

        if (!_canRewind) return;
        if (rows == 0 && col == 0) return;

        // A resize mid-block invalidates the row count, and an incorrect rewind erases unrelated
        // transcript above. Losing the restyle is much cheaper than eating the conversation.
        if (_widthChanged)
        {
            _widthChanged = false;
            _console.WriteLine();
            return;
        }

        // Past the viewport the earlier rows are already in scrollback, where no escape sequence
        // can reach them. Leave the raw text and let the styled copy follow below.
        if (rows > ConsoleLayout.CurrentHeight() - 2)
        {
            _console.WriteLine();
            return;
        }

        // _canRewind already required caps.Ansi, so the escape sequence is safe here. Emitting it
        // via ControlCode rather than a raw Console.Write keeps it inside the capability gate.
        var caps = _console.Profile.Capabilities;
        _console.Write(ControlCode.Create(caps, w =>
        {
            w.Write("\r");
            if (rows > 0) w.CursorUp(rows);
            w.EraseInDisplay(0);   // CSI 0 J — cursor to end of screen. Never 2, never scrollback.
        }));
    }

    private void RenderBlock(string block)
    {
        // One blank line between blocks, never before the first — the vertical rhythm unit is a
        // single blank line, never zero and never two.
        if (_blocksRendered > 0) _console.WriteLine();

        MarkdownConsoleRenderer.Render(_console, block.TrimEnd('\n', '\r'));
        _blocksRendered++;
        _anyOutput = true;
        _blockWidth = ConsoleLayout.CurrentWidth();
        _widthChanged = false;
    }
}
