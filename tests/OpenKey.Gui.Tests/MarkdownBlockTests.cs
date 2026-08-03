using OpenKey.Gui.ViewModels;
using Xunit;

namespace OpenKey.Gui.Tests;

/// <summary>
/// The GUI's markdown model is deliberately free of UI types, so it can be tested without starting
/// a window — the same separation the console keeps between its renderer and the engine.
/// </summary>
public sealed class MarkdownBlockTests
{
    [Fact]
    public void SplitsProseIntoParagraphs()
    {
        var blocks = MarkdownBlock.Parse("First paragraph.\n\nSecond paragraph.");

        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(BlockKind.Paragraph, b.Kind));
    }

    [Fact]
    public void KeepsAFenceWholeIncludingBlankLines()
    {
        var blocks = MarkdownBlock.Parse("```python\nfirst = 1\n\nsecond = 2\n```");

        var code = Assert.Single(blocks);
        Assert.Equal(BlockKind.Code, code.Kind);
        Assert.Equal("python", code.Language);
        Assert.Contains("first = 1", code.Text, StringComparison.Ordinal);
        Assert.Contains("second = 2", code.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("```", code.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void FenceWithoutALanguageHasNoLabel()
    {
        var code = Assert.Single(MarkdownBlock.Parse("```\nplain\n```"));
        Assert.Null(code.Language);
    }

    [Fact]
    public void HeadingsCarryTheirLevel()
    {
        var blocks = MarkdownBlock.Parse("# One\n\n### Three");

        Assert.Equal(1, blocks[0].Level);
        Assert.Equal(3, blocks[1].Level);
        Assert.True(blocks[0].FontSize > blocks[1].FontSize);
    }

    [Fact]
    public void ListItemsBecomeSeparateBlocksWithMarkers()
    {
        var blocks = MarkdownBlock.Parse("- alpha\n- beta");

        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(BlockKind.ListItem, b.Kind));
        Assert.Contains("alpha", blocks[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("•", blocks[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderedListsNumberSequentially()
    {
        var blocks = MarkdownBlock.Parse("1. first\n2. second");

        Assert.StartsWith("1.", blocks[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("2.", blocks[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyListItemsAreIndented()
    {
        var list = Assert.Single(MarkdownBlock.Parse("- item"));
        var prose = Assert.Single(MarkdownBlock.Parse("just text"));

        Assert.True(list.LeftMargin.Left > 0);
        Assert.Equal(0, list.LeftMargin.Top);      // a bare number would indent all four sides
        Assert.Equal(0, prose.LeftMargin.Left);
    }

    [Fact]
    public void MixedContentKeepsItsOrder()
    {
        var blocks = MarkdownBlock.Parse("Intro line.\n\n```js\nconst x = 1;\n```\n\n- after");

        Assert.Equal(3, blocks.Count);
        Assert.Equal(BlockKind.Paragraph, blocks[0].Kind);
        Assert.Equal(BlockKind.Code, blocks[1].Kind);
        Assert.Equal(BlockKind.ListItem, blocks[2].Kind);
    }

    [Fact]
    public void EmptyInputProducesNothing()
    {
        Assert.Empty(MarkdownBlock.Parse(string.Empty));
        Assert.Empty(MarkdownBlock.Parse("   \n  "));
    }

    [Fact]
    public void PartialFenceStillShowsItsContent()
    {
        // Mid-stream the closing marker has not arrived yet; the text must not vanish.
        var blocks = MarkdownBlock.Parse("```python\nprint(1)");

        Assert.NotEmpty(blocks);
        Assert.Contains(blocks, b => b.Text.Contains("print(1)", StringComparison.Ordinal));
    }
}

public sealed class MessageViewModelTests
{
    [Fact]
    public void AppendingRebuildsBlocks()
    {
        var msg = new MessageViewModel(Speaker.Assistant, "Tester");
        msg.Append("Hello");
        msg.Append(" there.");

        Assert.Equal("Hello there.", msg.Text);
        Assert.Single(msg.Blocks);
    }

    [Fact]
    public void ResetDiscardsAnAbandonedAttempt()
    {
        // The GUI half of the mid-reply rotation fix: without this the answer appears twice.
        var msg = new MessageViewModel(Speaker.Assistant, "Tester");
        msg.Append("The capital of France is Par");
        msg.Reset();
        msg.Append("The capital of France is Paris.");

        Assert.Equal("The capital of France is Paris.", msg.Text);
        Assert.Single(msg.Blocks);
    }

    [Fact]
    public void HeaderNamesTheSpeaker()
    {
        Assert.Equal("OpenKey AI", new MessageViewModel(Speaker.Assistant, "Tester").Header);
        Assert.Equal("Tester", new MessageViewModel(Speaker.You, "Tester").Header);
    }

    [Fact]
    public void RenamingYourselfRelabelsMessagesYouAlreadySent()
    {
        // A transcript addressing you by two different names would read as two people.
        var mine = new MessageViewModel(Speaker.You, "Tester", "hello");
        var reply = new MessageViewModel(Speaker.Assistant, "Tester", "hi");

        mine.SetUserName("Sam");
        reply.SetUserName("Sam");

        Assert.Equal("Sam", mine.Header);
        Assert.Equal("OpenKey AI", reply.Header);
    }

    [Fact]
    public void SubtitleIsEmptyUntilAModelAnswers()
    {
        var msg = new MessageViewModel(Speaker.Assistant, "Tester");
        Assert.Equal(string.Empty, msg.Subtitle);

        msg.ModelId = "vendor/model:free";
        msg.Elapsed = TimeSpan.FromSeconds(1.2);
        Assert.Contains("vendor/model:free", msg.Subtitle, StringComparison.Ordinal);
        Assert.Contains("1.2s", msg.Subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public void LongRepliesSwitchToMinutes()
    {
        var msg = new MessageViewModel(Speaker.Assistant, "Tester") { ModelId = "m", Elapsed = TimeSpan.FromSeconds(75) };
        Assert.Contains("1m 15s", msg.Subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public void BlocksShrinkWhenTextIsReplacedWithLess()
    {
        // Blocks are updated in place to avoid flicker, so the trailing ones must be removed.
        var msg = new MessageViewModel(Speaker.Assistant, "Tester");
        msg.Append("one\n\ntwo\n\nthree");
        Assert.Equal(3, msg.Blocks.Count);

        msg.Reset();
        msg.Append("only one");
        Assert.Single(msg.Blocks);
    }
}
