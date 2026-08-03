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
/// Inline formatting is preserved as a list of <see cref="InlineSpan"/> runs rather than flattened,
/// so bold, italic, inline code and links survive to the renderer. The emphasis rules deliberately
/// match the console's — both hosts parse with Markdig, so the two cannot drift.
/// </para>
/// </summary>
public sealed record MarkdownBlock(
    BlockKind Kind,
    string Text,
    string? Language = null,
    int Level = 0,
    int Indent = 0,
    IReadOnlyList<InlineSpan>? Spans = null)
{
    /// <summary>
    /// The block's text split into styled runs. Falls back to one unstyled run, so a block built
    /// without spans — a code fence, or the verbatim fallback after a parse failure — still renders.
    /// <para>
    /// <see cref="Text"/> remains the plain-text form and stays the source for copy and export: a
    /// pasted transcript should not carry styling the destination cannot honour.
    /// </para>
    /// </summary>
    public IReadOnlyList<InlineSpan> Runs => Spans ?? new[] { new InlineSpan(Text) };

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

    /// <summary>
    /// Explicit leading. Avalonia's default is the font's own line spacing, which for Inter at
    /// 14px is roughly 1.2× — fine for a label, too tight for paragraphs of prose, and the
    /// clearest single tell of an interface nobody laid out. Headings take a tighter ratio
    /// because larger type needs proportionally less air to stay one unit.
    /// </summary>
    public double LineHeight => Kind switch
    {
        BlockKind.Heading => Math.Round(FontSize * 1.3),
        _ => Math.Round(FontSize * 1.55),
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
                into.Add(new MarkdownBlock(BlockKind.Heading, Inline(h.Inline), Level: h.Level, Spans: BuildSpans(h.Inline)));
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
                        into.Add(new MarkdownBlock(BlockKind.Quote, Inline(lb.Inline), Spans: BuildSpans(lb.Inline)));
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
                            // The marker is a span of its own so it never picks up the emphasis of
                            // the first word — "- **Done**" must not embolden the bullet.
                            var itemSpans = new List<InlineSpan> { new($"{marker}  ") };
                            itemSpans.AddRange(BuildSpans(p.Inline));

                            into.Add(new MarkdownBlock(
                                BlockKind.ListItem, $"{marker}  {Inline(p.Inline)}",
                                Indent: indent, Spans: itemSpans));
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
                into.Add(new MarkdownBlock(BlockKind.Paragraph, Inline(p2.Inline), Spans: BuildSpans(p2.Inline)));
                break;

            case ContainerBlock container:
                foreach (var child in container) Walk(child, into, indent);
                break;

            case LeafBlock leaf when leaf.Inline is not null:
                into.Add(new MarkdownBlock(BlockKind.Paragraph, Inline(leaf.Inline), Spans: BuildSpans(leaf.Inline)));
                break;
        }
    }

    /// <summary>
    /// Flattens Markdig's inline tree into styled runs.
    /// <para>
    /// Style is threaded down and OR-ed rather than replaced, because markdown nests:
    /// <c>***x***</c> parses as bold wrapping italic, and reassigning would lose the outer one.
    /// </para>
    /// </summary>
    private static List<InlineSpan> BuildSpans(ContainerInline? container)
    {
        var spans = new List<InlineSpan>();
        if (container is not null) AppendSpans(spans, container, InlineStyle.None, null);
        return spans;
    }

    private static void AppendSpans(List<InlineSpan> into, Inline inline, InlineStyle style, string? url)
    {
        switch (inline)
        {
            case LiteralInline lit:
                AddSpan(into, lit.Content.ToString(), style, url);
                break;

            case CodeInline code:
                AddSpan(into, code.Content, style | InlineStyle.Code, url);
                break;

            case EmphasisInline em:
            {
                var added = em.DelimiterChar == '~'
                    ? InlineStyle.Strikethrough
                    : em.DelimiterCount >= 2 ? InlineStyle.Bold : InlineStyle.Italic;

                foreach (var child in em) AppendSpans(into, child, style | added, url);
                break;
            }

            case LinkInline link:
            {
                var target = string.IsNullOrEmpty(link.Url) ? url : link.Url;
                var linkStyle = target is null ? style : style | InlineStyle.Link;

                var before = into.Count;
                foreach (var child in link) AppendSpans(into, child, linkStyle, target);

                // A link with no label — or an image, whose alt text may be empty — would otherwise
                // vanish entirely. Show the URL rather than nothing.
                if (into.Count == before && target is not null) AddSpan(into, target, linkStyle, target);
                break;
            }

            case AutolinkInline auto:
                AddSpan(into, auto.Url, style | InlineStyle.Link, auto.Url);
                break;

            case LineBreakInline:
                AddSpan(into, "\n", style, url);
                break;

            case ContainerInline container:
                foreach (var child in container) AppendSpans(into, child, style, url);
                break;
        }
    }

    /// <summary>
    /// Appends a run, merging it into the previous one when they share a style. Markdig emits
    /// literals in fragments, so without this a plain sentence becomes a dozen runs.
    /// </summary>
    private static void AddSpan(List<InlineSpan> into, string text, InlineStyle style, string? url)
    {
        if (text.Length == 0) return;

        if (into.Count > 0)
        {
            var last = into[^1];
            if (last.Style == style && last.Url == url)
            {
                into[^1] = last with { Text = last.Text + text };
                return;
            }
        }

        into.Add(new InlineSpan(text, style, url));
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
