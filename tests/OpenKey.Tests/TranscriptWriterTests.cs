using System.Text;
using OpenKey.Ui;
using Spectre.Console;
using Xunit;

namespace OpenKey.Tests;

/// <summary>
/// Block-splitting behaviour of the streaming writer. The console here is redirected, so
/// <c>ConsoleLayout.Rich</c> is false and no raw text or cursor movement is emitted — what remains
/// is exactly the styled block output, which is what these assertions are about.
/// </summary>
public sealed class TranscriptWriterTests
{
    private static (TranscriptWriter Writer, StringWriter Output) Create()
    {
        var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(output),
        });
        return (new TranscriptWriter(console), output);
    }

    [Fact]
    public void RendersASingleParagraphOnComplete()
    {
        var (writer, output) = Create();
        writer.Append("Hello there.");
        writer.Complete();

        Assert.Contains("Hello there.", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SplitsParagraphsOnABlankLine()
    {
        var (writer, output) = Create();
        writer.Append("First paragraph.\n\nSecond paragraph.");
        writer.Complete();

        var text = output.ToString();
        Assert.Contains("First paragraph.", text, StringComparison.Ordinal);
        Assert.Contains("Second paragraph.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsAFenceWholeEvenWhenItContainsABlankLine()
    {
        // A blank line inside a code fence must not split the block, or the fence renders as two
        // broken panels.
        var (writer, output) = Create();
        writer.Append("```python\nfirst = 1\n\nsecond = 2\n```\n");
        writer.Complete();

        var text = output.ToString();
        Assert.Contains("first = 1", text, StringComparison.Ordinal);
        Assert.Contains("second = 2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("```", text, StringComparison.Ordinal);   // rendered, not literal
    }

    [Fact]
    public void HandlesAFenceArrivingAcrossManyDeltas()
    {
        // The realistic case: a model emits a fence a few characters at a time.
        var (writer, output) = Create();
        foreach (var piece in new[] { "Here:\n", "\n", "```", "js\n", "const ", "x = 1;\n", "```", "\n" })
            writer.Append(piece);
        writer.Complete();

        var text = output.ToString();
        Assert.Contains("Here:", text, StringComparison.Ordinal);
        Assert.Contains("const x = 1;", text, StringComparison.Ordinal);
        Assert.DoesNotContain("```", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NeverShowsRawBackticksWhateverTheConsoleSupports(bool ansi)
    {
        // A single delta can carry both fence markers, which nets to "not inside a fence". That
        // used to let a whole code block reach the screen as raw markdown before the flush
        // replaced it — and it only reproduced on consoles that allow raw streaming, so it passed
        // locally and failed in CI.
        var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = ansi ? AnsiSupport.Yes : AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(output),
        });

        var writer = new TranscriptWriter(console);
        writer.Append("```python\nfirst = 1\n\nsecond = 2\n```\n");
        writer.Complete();

        Assert.DoesNotContain("```", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RendersAnUnterminatedFenceOnComplete()
    {
        // A stream cut short mid-fence must still show what arrived rather than swallowing it.
        var (writer, output) = Create();
        writer.Append("```python\nprint(1)\n");
        writer.Complete();

        Assert.Contains("print(1)", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SplitsWhenABlankLineArrivesAcrossTwoDeltas()
    {
        var (writer, output) = Create();
        writer.Append("One.\n");
        writer.Append("\nTwo.");
        writer.Complete();

        var text = output.ToString();
        Assert.Contains("One.", text, StringComparison.Ordinal);
        Assert.Contains("Two.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ResetDiscardsEverythingFromAnAbandonedAttempt()
    {
        // What stops a mid-reply rotation from rendering the answer twice concatenated.
        var (writer, output) = Create();
        writer.Append("The capital of France is Par");
        writer.Reset();
        writer.Append("The capital of France is Paris.");
        writer.Complete();

        var text = output.ToString();
        Assert.Contains("The capital of France is Paris.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("is Par\n", text, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(text, "The capital of France"));
    }

    [Fact]
    public void EmitsNothingForEmptyInput()
    {
        var (writer, output) = Create();
        writer.Complete();

        Assert.True(string.IsNullOrWhiteSpace(output.ToString()));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }
        return count;
    }
}

public sealed class MarkdownRenderingTests
{
    private static string Render(string markdown)
    {
        var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(output),
        });
        MarkdownConsoleRenderer.Render(console, markdown);
        return output.ToString();
    }

    [Fact]
    public void KeepsLinksWhoseUrlContainsBrackets()
    {
        // The old code filtered out any URL containing a bracket, silently dropping the target.
        // Brackets are legal in URLs and common in generated ones.
        var text = Render("See [the docs](https://example.com/a[b]c) for more.");

        Assert.Contains("the docs", text, StringComparison.Ordinal);
        Assert.Contains("for more.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelOutputCannotInjectConsoleMarkup()
    {
        var text = Render("A reply containing [red]alarming[/] markup and [[brackets]].");

        Assert.Contains("alarming", text, StringComparison.Ordinal);
        Assert.Contains("[red]", text, StringComparison.Ordinal);   // shown literally, not applied
    }

    [Fact]
    public void RendersInlineCodeWithoutABackground()
    {
        Assert.Contains("value", Render("Set `value` first."), StringComparison.Ordinal);
    }
}

public sealed class ThemeTests
{
    [Theory]
    [InlineData("default")]
    [InlineData("dark")]
    [InlineData("light")]
    [InlineData("mono")]
    public void EveryAdvertisedThemeApplies(string name)
    {
        Assert.True(Theme.IsKnown(name));
        Theme.Apply(name);
        Assert.Equal(name, Theme.Current);
        Assert.False(string.IsNullOrWhiteSpace(Theme.Brand));
        Theme.Apply("default");
    }

    [Fact]
    public void UnknownThemesAreRejectedRatherThanSilentlyAccepted()
    {
        Assert.False(Theme.IsKnown("neon"));
    }

    [Fact]
    public void MonoRemovesHueWithoutRemovingMeaning()
    {
        // Accessibility check: severity must never be carried by colour alone. Under mono there is
        // no hue left, so if this palette is still usable the glyphs and titles are doing the work.
        Theme.Apply("mono");
        foreach (var style in new[] { Theme.Ok, Theme.Warn, Theme.Danger, Theme.Brand })
        {
            Assert.DoesNotContain("red", style, StringComparison.Ordinal);
            Assert.DoesNotContain("green", style, StringComparison.Ordinal);
            Assert.DoesNotContain("yellow", style, StringComparison.Ordinal);
        }
        Theme.Apply("default");
    }

    [Fact]
    public void SeverityGlyphsDifferSoRedGreenConfusionIsNeverTheOnlySignal()
    {
        Assert.NotEqual(Glyphs.Ok, Glyphs.Fail);
    }
}

public sealed class TextWidthTests
{
    [Fact]
    public void AsciiIsOneCellPerCharacter()
    {
        Assert.Equal(5, TextWidth.Of("hello"));
    }

    [Fact]
    public void CjkIsTwoCellsPerCharacter()
    {
        Assert.Equal(4, TextWidth.Of("你好"));
    }

    [Fact]
    public void CombiningMarksTakeNoSpace()
    {
        // "e" + combining acute renders in one cell, not two.
        Assert.Equal(1, TextWidth.Of("é"));
    }

    [Fact]
    public void EmojiIsTwoCells()
    {
        Assert.Equal(2, TextWidth.Of("\U0001F600"));
    }

    [Fact]
    public void SurrogatePairsCountAsOneGlyphNotTwoChars()
    {
        var emoji = "\U0001F600";
        Assert.Equal(2, emoji.Length);              // two chars
        Assert.Equal(2, TextWidth.Of(emoji));       // but two cells, not four
    }

    [Fact]
    public void MixedTextAddsUp()
    {
        Assert.Equal(5 + 4, TextWidth.Of("hello你好"));
    }
}
