using OpenKey.Core.Text;
using Xunit;

namespace OpenKey.Core.Tests;

/// <summary>
/// Guards the "why didn't my command run?" class of bug. Every case here produces a string that
/// looks identical on screen to one that works.
/// </summary>
public sealed class UserInputTests
{
    [Fact]
    public void AByteOrderMarkNoLongerHidesACommand()
    {
        // The case that actually happened: a UTF-8 file piped in with its BOM intact. The line
        // rendered as "/name", did not start with '/', and was sent to the model as a message.
        Assert.Equal("/name", UserInput.Normalize("﻿/name"));
    }

    [Theory]
    [InlineData(" /help")]              // the easy one to hit, and it was broken too
    [InlineData("\t/help")]
    [InlineData(" /help")]         // non-breaking space, typical of a paste from a web page
    [InlineData("​/help")]         // zero-width space
    [InlineData("⁠/help")]         // word joiner
    [InlineData("﻿  ​/help")] // several at once
    public void InvisibleLeadingCharactersDoNotHideACommand(string raw)
    {
        Assert.Equal("/help", UserInput.Normalize(raw));
    }

    [Fact]
    public void EmojiSurviveIntact()
    {
        // U+200D is load-bearing here: stripping zero-width joiners throughout, rather than only
        // at the start, would break this family into three separate people.
        const string family = "\U0001F468‍\U0001F469‍\U0001F467";

        Assert.Equal($"look: {family}", UserInput.Normalize($"look: {family}"));
    }

    [Fact]
    public void OrdinaryTextIsUntouchedApartFromTrailingSpace()
    {
        Assert.Equal("hello there", UserInput.Normalize("hello there   "));
        Assert.Equal("what is 2 + 2?", UserInput.Normalize("what is 2 + 2?"));
    }

    [Fact]
    public void InternalSpacingIsPreserved()
    {
        // Code and poetry both depend on interior spacing; only the ends are the app's business.
        Assert.Equal("def f():\n    return 1", UserInput.Normalize("def f():\n    return 1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("﻿")]
    public void NothingInMeansEmptyOut(string? raw)
    {
        // Never null: callers test length, and a blank line should be skipped rather than sent.
        Assert.Equal(string.Empty, UserInput.Normalize(raw));
    }

    [Fact]
    public void StripInvisibleKeepsIndentationThatNormalizeWouldEat()
    {
        // The message path uses StripInvisible precisely so a pasted code block keeps the
        // indentation of its first line. Normalize is for command detection, where leading space
        // must go; using it on a message body would silently reformat the paste.
        const string indented = "    if (x) {";

        Assert.Equal(indented, UserInput.StripInvisible(indented));
        Assert.Equal("if (x) {", UserInput.Normalize(indented));
    }

    [Fact]
    public void StripInvisibleStillRemovesAByteOrderMark()
    {
        Assert.Equal("    indented", UserInput.StripInvisible("﻿    indented"));
    }

    [Fact]
    public void AMessageThatMerelyMentionsASlashIsStillAMessage()
    {
        Assert.Equal("use the /help command", UserInput.Normalize("use the /help command"));
    }
}
