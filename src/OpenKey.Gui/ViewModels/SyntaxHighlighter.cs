using System.Text.RegularExpressions;

namespace OpenKey.Gui.ViewModels;

public enum TokenKind
{
    Plain,
    Keyword,
    StringLiteral,
    Comment,
    Number,
    Type,
}

public sealed record CodeToken(string Text, TokenKind Kind);

/// <summary>
/// Colours code blocks in replies.
/// <para>
/// Deliberately a small tokenizer rather than an AvaloniaEdit or TextMate dependency. Those are
/// built for editing — buffers, folding, undo, grammar files — and none of that applies to text
/// that is displayed once and never modified. The cost of getting this slightly wrong is a keyword
/// in the wrong colour; the cost of the dependency is megabytes and an AOT risk.
/// </para>
/// <para>
/// Comments and strings are matched first and win, because a keyword inside a comment is not a
/// keyword. Anything unrecognised renders plain, which is the correct failure mode.
/// </para>
/// </summary>
internal static class SyntaxHighlighter
{
    private const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    // One pass, alternation ordered by precedence: whatever matches first wins the span.
    private static readonly Regex CStyle = new(
        """
        (?<comment>//[^\n]*|/\*[\s\S]*?\*/)
        |(?<string>"(?:\\.|[^"\\\n])*"|'(?:\\.|[^'\\\n])*'|`(?:\\.|[^`\\])*`)
        |(?<number>\b\d[\d_]*(?:\.\d+)?(?:[eE][+-]?\d+)?[fFdDmMlLuU]?\b)
        |(?<word>[A-Za-z_][A-Za-z0-9_]*)
        """,
        Opts | RegexOptions.IgnorePatternWhitespace);

    // Five-quote delimiter: the pattern itself matches Python's triple-quoted strings, which would
    // otherwise close a three-quote raw literal.
    private static readonly Regex HashStyle = new(
        """""
        (?<comment>\#[^\n]*)
        |(?<string>"""[\s\S]*?"""|'''[\s\S]*?'''|"(?:\\.|[^"\\\n])*"|'(?:\\.|[^'\\\n])*')
        |(?<number>\b\d[\d_]*(?:\.\d+)?(?:[eE][+-]?\d+)?\b)
        |(?<word>[A-Za-z_][A-Za-z0-9_]*)
        """"",
        Opts | RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex SqlStyle = new(
        """
        (?<comment>--[^\n]*|/\*[\s\S]*?\*/)
        |(?<string>'(?:''|[^'])*')
        |(?<number>\b\d+(?:\.\d+)?\b)
        |(?<word>[A-Za-z_][A-Za-z0-9_]*)
        """,
        Opts | RegexOptions.IgnorePatternWhitespace);

    private static readonly HashSet<string> CommonKeywords = new(StringComparer.Ordinal)
    {
        "if", "else", "for", "while", "return", "break", "continue", "switch", "case", "default",
        "try", "catch", "finally", "throw", "new", "class", "struct", "enum", "interface",
        "public", "private", "protected", "internal", "static", "const", "readonly", "async",
        "await", "using", "namespace", "var", "let", "const", "function", "def", "import", "from",
        "export", "type", "true", "false", "null", "nil", "None", "True", "False", "this", "self",
        "in", "is", "not", "and", "or", "with", "as", "yield", "lambda", "pass", "elif", "raise",
        "match", "record", "override", "virtual", "abstract", "sealed", "out", "ref", "params",
    };

    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "select", "from", "where", "join", "inner", "left", "right", "outer", "on", "group", "by",
        "order", "having", "insert", "into", "values", "update", "set", "delete", "create", "table",
        "alter", "drop", "index", "view", "as", "and", "or", "not", "null", "is", "in", "exists",
        "distinct", "limit", "offset", "union", "all", "case", "when", "then", "else", "end",
        "primary", "key", "foreign", "references", "default", "constraint", "with",
    };

    private static readonly HashSet<string> KnownTypes = new(StringComparer.Ordinal)
    {
        "string", "int", "long", "bool", "double", "float", "decimal", "byte", "char", "object",
        "void", "str", "list", "dict", "set", "tuple", "number", "boolean", "any", "unknown",
        "String", "Int32", "Boolean", "Task", "List", "Dictionary", "Array", "Object",
    };

    /// <summary>
    /// Splits code into coloured spans. An unrecognised or absent language yields a single plain
    /// token, so callers never need to special-case it.
    /// </summary>
    public static IReadOnlyList<CodeToken> Tokenize(string code, string? language)
    {
        if (string.IsNullOrEmpty(code)) return Array.Empty<CodeToken>();

        var (regex, keywords) = Dialect(language);
        if (regex is null) return new[] { new CodeToken(code, TokenKind.Plain) };

        var tokens = new List<CodeToken>();
        var last = 0;

        foreach (Match m in regex.Matches(code))
        {
            if (m.Index > last) tokens.Add(new CodeToken(code[last..m.Index], TokenKind.Plain));

            var kind = TokenKind.Plain;
            if (m.Groups["comment"].Success) kind = TokenKind.Comment;
            else if (m.Groups["string"].Success) kind = TokenKind.StringLiteral;
            else if (m.Groups["number"].Success) kind = TokenKind.Number;
            else if (m.Groups["word"].Success)
            {
                var word = m.Value;
                if (keywords.Contains(word)) kind = TokenKind.Keyword;
                else if (KnownTypes.Contains(word)) kind = TokenKind.Type;
            }

            tokens.Add(new CodeToken(m.Value, kind));
            last = m.Index + m.Length;
        }

        if (last < code.Length) tokens.Add(new CodeToken(code[last..], TokenKind.Plain));
        return tokens;
    }

    private static (Regex? Regex, HashSet<string> Keywords) Dialect(string? language) =>
        (language ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "python" or "py" or "ruby" or "rb" or "sh" or "bash" or "shell" or "zsh" or "yaml" or "yml" or "toml"
                => (HashStyle, CommonKeywords),

            "sql" or "postgres" or "postgresql" or "mysql" or "sqlite"
                => (SqlStyle, SqlKeywords),

            "c" or "cpp" or "c++" or "cs" or "csharp" or "c#" or "java" or "js" or "javascript"
                or "ts" or "typescript" or "jsx" or "tsx" or "go" or "rust" or "rs" or "kotlin"
                or "swift" or "php" or "scala" or "dart" or "json"
                => (CStyle, CommonKeywords),

            // Unknown or absent language: render plain rather than guess wrong.
            _ => (null, CommonKeywords),
        };
}
