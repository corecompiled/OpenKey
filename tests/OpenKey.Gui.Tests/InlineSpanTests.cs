using OpenKey.Gui.ViewModels;
using Xunit;

namespace OpenKey.Gui.Tests;

/// <summary>
/// Inline formatting used to be flattened to plain text, so a reply full of <c>**emphasis**</c>
/// arrived looking uniform. These pin the span model that replaced it.
/// </summary>
public sealed class InlineSpanTests
{
    private static IReadOnlyList<InlineSpan> Runs(string markdown) =>
        Assert.Single(MarkdownBlock.Parse(markdown)).Runs;

    [Fact]
    public void BoldAndItalicAreSeparateRuns()
    {
        var runs = Runs("plain **bold** and *italic* end");

        Assert.Collection(runs,
            r => { Assert.Equal("plain ", r.Text); Assert.Equal(InlineStyle.None, r.Style); },
            r => { Assert.Equal("bold", r.Text); Assert.True(r.IsBold); Assert.False(r.IsItalic); },
            r => Assert.Equal(" and ", r.Text),
            r => { Assert.Equal("italic", r.Text); Assert.True(r.IsItalic); Assert.False(r.IsBold); },
            r => Assert.Equal(" end", r.Text));
    }

    [Fact]
    public void TripleAsterisksAreBothAtOnce()
    {
        // Markdig nests these — bold wrapping italic. Reassigning the style instead of OR-ing it
        // would silently drop the outer one.
        var run = Assert.Single(Runs("***both***"));

        Assert.True(run.IsBold);
        Assert.True(run.IsItalic);
    }

    [Fact]
    public void UnderscoresEmphasiseToo()
    {
        Assert.True(Assert.Single(Runs("__strong__")).IsBold);
        Assert.True(Assert.Single(Runs("_slanted_")).IsItalic);
    }

    [Fact]
    public void InlineCodeIsItsOwnStyle()
    {
        var runs = Runs("call `File.Read` now");

        var code = Assert.Single(runs, r => r.IsCode);
        Assert.Equal("File.Read", code.Text);
        Assert.False(code.IsBold);
    }

    [Fact]
    public void EmphasisInsideCodeStaysLiteral()
    {
        // Backticks win: `**x**` is code containing asterisks, not bold.
        var code = Assert.Single(Runs("`**x**`"));

        Assert.True(code.IsCode);
        Assert.Equal("**x**", code.Text);
        Assert.False(code.IsBold);
    }

    [Fact]
    public void LinksKeepTheirLabelAndTarget()
    {
        var link = Assert.Single(Runs("see [the docs](https://example.com/a)"), r => r.IsLink);

        Assert.Equal("the docs", link.Text);
        Assert.Equal("https://example.com/a", link.Url);
    }

    [Fact]
    public void ALinkWithNoLabelFallsBackToItsUrl()
    {
        // Otherwise the run has no text and the link disappears from the reply entirely.
        var link = Assert.Single(Runs("[](https://example.com/b)"), r => r.IsLink);

        Assert.Equal("https://example.com/b", link.Text);
    }

    [Fact]
    public void EmphasisInsideALinkSurvivesAlongsideIt()
    {
        var run = Assert.Single(Runs("[**bold link**](https://example.com/c)"), r => r.IsLink);

        Assert.True(run.IsBold);
        Assert.Equal("https://example.com/c", run.Url);
    }

    [Fact]
    public void AListMarkerNeverInheritsTheFirstWordsEmphasis()
    {
        var runs = Runs("- **Done**");

        Assert.False(runs[0].IsBold);                       // the bullet
        Assert.Contains("•", runs[0].Text, StringComparison.Ordinal);
        Assert.True(Assert.Single(runs, r => r.Text == "Done").IsBold);
    }

    [Fact]
    public void HeadingsCarryEmphasisToo()
    {
        var block = Assert.Single(MarkdownBlock.Parse("## A *slanted* heading"));

        Assert.Equal(BlockKind.Heading, block.Kind);
        Assert.True(Assert.Single(block.Runs, r => r.Text == "slanted").IsItalic);
    }

    [Fact]
    public void AdjacentPlainTextIsMergedIntoOneRun()
    {
        // Markdig emits literals in fragments — punctuation splits them. Without merging, an
        // ordinary sentence would become a dozen runs for no reason.
        var runs = Runs("No emphasis here at all, just words; and punctuation.");

        Assert.Single(runs);
        Assert.Equal(InlineStyle.None, runs[0].Style);
    }

    [Fact]
    public void PlainTextStillMatchesTheFlattenedForm()
    {
        // Text remains the copy and export source, so it must stay free of markup.
        var block = Assert.Single(MarkdownBlock.Parse("a **b** c"));

        Assert.Equal("a b c", block.Text);
    }

    [Fact]
    public void ACodeFenceHasNoSpansAndFallsBackToItsText()
    {
        var block = Assert.Single(MarkdownBlock.Parse("```\nx = 1\n```"));

        var run = Assert.Single(block.Runs);
        Assert.Equal("x = 1", run.Text);
        Assert.Equal(InlineStyle.None, run.Style);
    }

    [Fact]
    public void AsterisksThatAreNotEmphasisAreLeftAlone()
    {
        var run = Assert.Single(Runs("2 * 3 * 4"));

        Assert.Equal(InlineStyle.None, run.Style);
        Assert.Equal("2 * 3 * 4", run.Text);
    }
}
