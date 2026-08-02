using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using OpenKey.Ui;
using Spectre.Console;

namespace OpenKey;

/// <summary>
/// Renders a markdown block to the console using Spectre. Every literal is escaped, so model
/// output can never inject Spectre markup — the fallback path re-escapes too.
/// </summary>
internal static class MarkdownConsoleRenderer
{
    public static void Render(IAnsiConsole console, string markdown)
    {
        try
        {
            var doc = Markdown.Parse(markdown);
            RenderBlocks(console, doc, indent: 0, topLevel: true);
        }
        catch (Exception)
        {
            // Never let rendering crash a turn — fall back to plain escaped text.
            foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
                console.MarkupLine(Markup.Escape(line));
        }
    }

    private static void RenderBlocks(IAnsiConsole console, ContainerBlock container, int indent, bool topLevel)
    {
        var first = true;
        foreach (var block in container)
        {
            // One blank line between top-level blocks, never zero and never two. Previously the
            // renderer emitted none at all, so headings, paragraphs and lists butted together.
            if (topLevel && !first) console.WriteLine();
            RenderBlock(console, block, indent);
            first = false;
        }
    }

    private static void RenderBlock(IAnsiConsole console, Block block, int indent)
    {
        var pad = new string(' ', indent);
        switch (block)
        {
            case HeadingBlock h:
            {
                // H1-H2 are structural, so they take the brand colour; deeper headings are just
                // emphasis and stay uncoloured. "white" would break a light-background console.
                var style = h.Level <= 2 ? Theme.BrandStrong : Theme.Strong;
                console.MarkupLine($"{pad}[{style}]{InlineToMarkup(h.Inline)}[/]");
                break;
            }
            case FencedCodeBlock fenced:
                RenderCode(console, fenced.Lines.ToString(), fenced.Info);
                break;
            case CodeBlock code:
                RenderCode(console, code.Lines.ToString(), null);
                break;
            case QuoteBlock quote:
                foreach (var child in quote)
                {
                    if (child is LeafBlock lb && lb.Inline is not null)
                        console.MarkupLine($"{pad}[{Theme.Muted}]{Glyphs.QuoteBar} {InlineToMarkup(lb.Inline)}[/]");
                    else
                        RenderBlock(console, child, indent);
                }
                break;
            case ListBlock list:
                RenderList(console, list, indent);
                break;
            case ThematicBreakBlock:
                console.Write(new Rule().RuleStyle(Theme.Muted));
                break;
            case ParagraphBlock p:
                console.MarkupLine($"{pad}{InlineToMarkup(p.Inline)}");
                break;
            case ContainerBlock cont:
                RenderBlocks(console, cont, indent, topLevel: false);
                break;
            case LeafBlock leaf when leaf.Inline is not null:
                console.MarkupLine($"{pad}{InlineToMarkup(leaf.Inline)}");
                break;
        }
    }

    private static void RenderList(IAnsiConsole console, ListBlock list, int indent)
    {
        var n = int.TryParse(list.OrderedStart, out var start) ? start : 1;
        foreach (var item in list)
        {
            if (item is not ListItemBlock li) continue;
            var bullet = list.IsOrdered ? $"{n}." : Glyphs.Bullet;
            n++;

            var firstChild = true;
            foreach (var child in li)
            {
                if (firstChild && child is ParagraphBlock p)
                {
                    console.MarkupLine(
                        $"{new string(' ', indent)}[{Theme.Muted}]{bullet}[/] {InlineToMarkup(p.Inline)}");
                    firstChild = false;
                }
                else
                {
                    // Nested content hangs under the item text: bullet + space = 2 columns.
                    RenderBlock(console, child, indent + 2);
                }
            }
        }
    }

    private static void RenderCode(IAnsiConsole console, string code, string? lang)
    {
        var body = code.TrimEnd('\n', '\r');
        var panel = new Panel(new Text(body))
            .Border(Glyphs.Box)
            .BorderColor(Color.Grey)
            .Expand();
        if (!string.IsNullOrWhiteSpace(lang))
            panel.Header($"[{Theme.Muted}] {Markup.Escape(lang.Trim().ToLowerInvariant())} [/]");
        console.Write(panel);
    }

    private static string InlineToMarkup(ContainerInline? container)
    {
        if (container is null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var inline in container)
            AppendInline(sb, inline);
        return sb.ToString();
    }

    private static void AppendInline(StringBuilder sb, Inline inline)
    {
        switch (inline)
        {
            case LiteralInline lit:
                sb.Append(Markup.Escape(lit.Content.ToString()));
                break;
            case EmphasisInline em:
            {
                var tag = em.DelimiterCount >= 2 ? "bold" : "italic";
                sb.Append('[').Append(tag).Append(']');
                foreach (var child in em)
                    AppendInline(sb, child);
                sb.Append("[/]");
                break;
            }
            case CodeInline code:
                // Foreground only. The old "white on grey23" downsampled to white-on-black in
                // 16-colour mode: invisible on light schemes, identical to body text on dark ones,
                // so the one style meant to make code stand out did nothing.
                sb.Append('[').Append(Theme.Code).Append(']')
                  .Append(Markup.Escape(code.Content)).Append("[/]");
                break;
            case LinkInline link:
            {
                var label = new StringBuilder();
                foreach (var child in link)
                    AppendInline(label, child);
                // Escape the URL rather than dropping links whose URL contains a bracket. The old
                // filter silently discarded the target of any such link — legal in a URL, and
                // common in generated ones.
                var url = link.Url ?? string.Empty;
                if (!string.IsNullOrEmpty(url))
                    sb.Append("[link=").Append(Markup.Escape(url)).Append(']').Append(label).Append("[/]");
                else
                    sb.Append(label);
                break;
            }
            case AutolinkInline auto:
                sb.Append("[link=").Append(Markup.Escape(auto.Url)).Append(']')
                  .Append(Markup.Escape(auto.Url)).Append("[/]");
                break;
            case LineBreakInline:
                sb.Append('\n');
                break;
            case ContainerInline cont:
                foreach (var child in cont)
                    AppendInline(sb, child);
                break;
        }
    }
}
