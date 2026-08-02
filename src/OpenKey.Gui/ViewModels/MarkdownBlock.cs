using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace OpenKey.Gui.ViewModels;

public enum BlockKind
{
    Paragraph,
    Heading,
    Code,
    Quote,
    ListItem,
    Rule,
}

/// <summary>
/// A renderable piece of a reply.
/// <para>
/// Deliberately a flat, plain model rather than a tree of Avalonia controls. The view binds to
/// these, so parsing stays free of UI types and is testable without a window — the same separation
/// the console keeps between <c>MarkdownConsoleRenderer</c> and the engine.
/// </para>
/// <para>
/// Inline formatting is flattened to text in this version. Code fences and structure carry most of
/// the readability benefit; inline bold and italic can come later without changing this shape.
/// </para>
/// </summary>
public sealed record MarkdownBlock(BlockKind Kind, string Text, string? Language = null, int Level = 0, int Indent = 0)
{
    public bool IsCode => Kind == BlockKind.Code;
    public bool IsRule => Kind == BlockKind.Rule;
    public bool IsNotCode => Kind != BlockKind.Code && Kind != BlockKind.Rule;

    public double FontSize => Kind switch
    {
        BlockKind.Heading when Level <= 1 => 21,
        BlockKind.Heading when Level == 2 => 18,
        BlockKind.Heading => 16,
        _ => 14,
    };

    public bool IsHeading => Kind == BlockKind.Heading;
    public bool IsQuote => Kind == BlockKind.Quote;

    /// <summary>
    /// A Thickness, not a double: binding a bare number to Margin indents all four sides, which
    /// pushes list items down the page as well as across.
    /// </summary>
    public Avalonia.Thickness LeftMargin =>
        Kind == BlockKind.ListItem ? new Avalonia.Thickness(16 + (Indent * 16), 0, 0, 0) : default;

    public static IReadOnlyList<MarkdownBlock> Parse(string markdown)
    {
        var blocks = new List<MarkdownBlock>();
        if (string.IsNullOrWhiteSpace(markdown)) return blocks;

        try
        {
            var doc = Markdown.Parse(markdown);
            foreach (var block in doc) Walk(block, blocks, indent: 0);
        }
        catch (Exception)
        {
            // Never let a parse failure lose the reply — fall back to showing it verbatim.
            blocks.Clear();
            blocks.Add(new MarkdownBlock(BlockKind.Paragraph, markdown));
        }

        return blocks;
    }

    private static void Walk(Block block, List<MarkdownBlock> into, int indent)
    {
        switch (block)
        {
            case HeadingBlock h:
                into.Add(new MarkdownBlock(BlockKind.Heading, Inline(h.Inline), Level: h.Level));
                break;

            case FencedCodeBlock fenced:
                into.Add(new MarkdownBlock(
                    BlockKind.Code,
                    fenced.Lines.ToString().TrimEnd('\n', '\r'),
                    Language: string.IsNullOrWhiteSpace(fenced.Info) ? null : fenced.Info.Trim().ToLowerInvariant()));
                break;

            case CodeBlock code:
                into.Add(new MarkdownBlock(BlockKind.Code, code.Lines.ToString().TrimEnd('\n', '\r')));
                break;

            case QuoteBlock quote:
                foreach (var child in quote)
                {
                    if (child is LeafBlock lb && lb.Inline is not null)
                        into.Add(new MarkdownBlock(BlockKind.Quote, Inline(lb.Inline)));
                    else
                        Walk(child, into, indent);
                }
                break;

            case ListBlock list:
            {
                var n = int.TryParse(list.OrderedStart, out var start) ? start : 1;
                foreach (var item in list)
                {
                    if (item is not ListItemBlock li) continue;
                    var marker = list.IsOrdered ? $"{n}." : "•";
                    n++;

                    var first = true;
                    foreach (var child in li)
                    {
                        if (first && child is ParagraphBlock p)
                        {
                            into.Add(new MarkdownBlock(
                                BlockKind.ListItem, $"{marker}  {Inline(p.Inline)}", Indent: indent));
                            first = false;
                        }
                        else
                        {
                            Walk(child, into, indent + 1);
                        }
                    }
                }
                break;
            }

            case ThematicBreakBlock:
                into.Add(new MarkdownBlock(BlockKind.Rule, string.Empty));
                break;

            case ParagraphBlock p2:
                into.Add(new MarkdownBlock(BlockKind.Paragraph, Inline(p2.Inline)));
                break;

            case ContainerBlock container:
                foreach (var child in container) Walk(child, into, indent);
                break;

            case LeafBlock leaf when leaf.Inline is not null:
                into.Add(new MarkdownBlock(BlockKind.Paragraph, Inline(leaf.Inline)));
                break;
        }
    }

    private static string Inline(ContainerInline? container)
    {
        if (container is null) return string.Empty;
        var sb = new StringBuilder();
        Append(sb, container);
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, Inline inline)
    {
        switch (inline)
        {
            case LiteralInline lit:
                sb.Append(lit.Content.ToString());
                break;
            case CodeInline code:
                sb.Append(code.Content);
                break;
            case LineBreakInline:
                sb.Append('\n');
                break;
            case LinkInline link:
            {
                foreach (var child in link) Append(sb, child);
                if (!string.IsNullOrEmpty(link.Url)) sb.Append(" (").Append(link.Url).Append(')');
                break;
            }
            case AutolinkInline auto:
                sb.Append(auto.Url);
                break;
            case ContainerInline container:
                foreach (var child in container) Append(sb, child);
                break;
        }
    }
}
