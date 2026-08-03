using OpenKey.Gui.ViewModels;
using Xunit;

namespace OpenKey.Gui.Tests;

public sealed class SyntaxHighlighterTests
{
    private static string TextOf(IEnumerable<CodeToken> tokens) =>
        string.Concat(tokens.Select(t => t.Text));

    private static IEnumerable<string> OfKind(IEnumerable<CodeToken> tokens, TokenKind kind) =>
        tokens.Where(t => t.Kind == kind).Select(t => t.Text);

    [Theory]
    [InlineData("csharp", "// note\nvar x = 1;")]
    [InlineData("python", "# note\nx = 1")]
    [InlineData("sql", "-- note\nSELECT 1")]
    [InlineData(null, "anything at all")]
    [InlineData("brainfuck", "+++.")]
    public void NeverLosesOrAltersACharacter(string? language, string code)
    {
        // The single invariant that matters: highlighting is a view over the text, so round-tripping
        // the tokens must reproduce the source exactly. A dropped character would silently corrupt
        // code the user is about to copy.
        Assert.Equal(code, TextOf(SyntaxHighlighter.Tokenize(code, language)));
    }

    [Fact]
    public void UnknownLanguageRendersAsOnePlainToken()
    {
        var tokens = SyntaxHighlighter.Tokenize("some ::: unusual +++ text", "cobol");

        var only = Assert.Single(tokens);
        Assert.Equal(TokenKind.Plain, only.Kind);
    }

    [Fact]
    public void EmptyCodeProducesNoTokens()
    {
        Assert.Empty(SyntaxHighlighter.Tokenize(string.Empty, "python"));
    }

    [Fact]
    public void FindsKeywordsStringsAndCommentsInCSharp()
    {
        var tokens = SyntaxHighlighter.Tokenize("""
            // greet
            public var name = "world";
            """, "csharp");

        Assert.Contains("public", OfKind(tokens, TokenKind.Keyword));
        Assert.Contains("\"world\"", OfKind(tokens, TokenKind.StringLiteral));
        Assert.Contains(OfKind(tokens, TokenKind.Comment), c => c.Contains("greet", StringComparison.Ordinal));
    }

    [Fact]
    public void AKeywordInsideACommentIsNotAKeyword()
    {
        // Precedence is the whole reason comments and strings are matched first.
        var tokens = SyntaxHighlighter.Tokenize("// return this class", "csharp");

        Assert.Empty(OfKind(tokens, TokenKind.Keyword));
        Assert.Single(OfKind(tokens, TokenKind.Comment));
    }

    [Fact]
    public void AKeywordInsideAStringIsNotAKeyword()
    {
        var tokens = SyntaxHighlighter.Tokenize("""x = "if else return" """, "python");

        Assert.Empty(OfKind(tokens, TokenKind.Keyword));
    }

    [Fact]
    public void HandlesPythonTripleQuotedStrings()
    {
        var code = "x = \"\"\"if this were code\nit is not\"\"\"";
        var tokens = SyntaxHighlighter.Tokenize(code, "python");

        Assert.Equal(code, TextOf(tokens));
        Assert.Empty(OfKind(tokens, TokenKind.Keyword));
    }

    [Fact]
    public void SqlKeywordsAreCaseInsensitive()
    {
        var upper = SyntaxHighlighter.Tokenize("SELECT * FROM t", "sql");
        var lower = SyntaxHighlighter.Tokenize("select * from t", "sql");

        Assert.Contains("SELECT", OfKind(upper, TokenKind.Keyword));
        Assert.Contains("select", OfKind(lower, TokenKind.Keyword));
    }

    [Fact]
    public void CSharpKeywordsAreCaseSensitive()
    {
        // "Public" is a perfectly good identifier and must not be coloured as a keyword.
        var tokens = SyntaxHighlighter.Tokenize("Public Class", "csharp");
        Assert.Empty(OfKind(tokens, TokenKind.Keyword));
    }

    [Fact]
    public void RecognisesNumbersAndTypes()
    {
        var tokens = SyntaxHighlighter.Tokenize("int count = 42;", "csharp");

        Assert.Contains("42", OfKind(tokens, TokenKind.Number));
        Assert.Contains("int", OfKind(tokens, TokenKind.Type));
    }

    [Fact]
    public void UnterminatedStringDoesNotSwallowTheRestOfTheBlock()
    {
        // Mid-stream a fence can arrive half-written; the text must still survive intact.
        var code = "x = \"unclosed\ny = 1";
        Assert.Equal(code, TextOf(SyntaxHighlighter.Tokenize(code, "python")));
    }

    [Fact]
    public void ShellAndYamlUseHashComments()
    {
        Assert.Single(OfKind(SyntaxHighlighter.Tokenize("# a note\nls -la", "bash"), TokenKind.Comment));
        Assert.Single(OfKind(SyntaxHighlighter.Tokenize("# a note\nkey: value", "yaml"), TokenKind.Comment));
    }
}
