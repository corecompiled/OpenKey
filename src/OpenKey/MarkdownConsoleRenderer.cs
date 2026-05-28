using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Spectre.Console;

namespace OpenKey;

/// <summary>
/// Renders an LLM markdown reply to the console using Spectre. Parses once (Markdig)
/// after the full reply is buffered — no per-token re-parse. All literal text is escaped
/// so model output can never inject Spectre markup.
/// </summary>
internal static class MarkdownConsoleRenderer
{
    public static void Render(IAnsiConsole console, string markdown)
    {
        try
        {
            var doc = Markdown.Parse(markdown);
            RenderBlocks(console, doc, indent: 0);
        }
        catch (Exception)
        {
            // Never let rendering crash a turn — fall back to plain escaped text.
            foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
                console.MarkupLine(Markup.Escape(line));
        }
    }

    private static void RenderBlocks(IAnsiConsole console, ContainerBlock container, int indent)
    {
        foreach (var block in container)
            RenderBlock(console, block, indent);
    }

    private static void RenderBlock(IAnsiConsole console, Block block, int indent)
    {
        var pad = new string(' ', indent);
        switch (block)
        {
            case HeadingBlock h:
            {
                var color = h.Level <= 2 ? "cyan" : "white";
                console.MarkupLine($"{pad}[bold {color}]{InlineToMarkup(h.Inline)}[/]");
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
                        console.MarkupLine($"{pad}[grey]│ {InlineToMarkup(lb.Inline)}[/]");
                    else
                        RenderBlock(console, child, indent);
                }
                break;
            case ListBlock list:
                RenderList(console, list, indent);
                break;
            case ThematicBreakBlock:
                console.Write(new Rule().RuleStyle("grey"));
                break;
            case ParagraphBlock p:
                console.MarkupLine($"{pad}{InlineToMarkup(p.Inline)}");
                break;
            case ContainerBlock cont:
                RenderBlocks(console, cont, indent);
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
            var bullet = list.IsOrdered ? $"{n}." : "•";
            n++;

            var first = true;
            foreach (var child in li)
            {
                if (first && child is ParagraphBlock p)
                {
                    console.MarkupLine($"{new string(' ', indent)}[grey]{bullet}[/] {InlineToMarkup(p.Inline)}");
                    first = false;
                }
                else
                {
                    RenderBlock(console, child, indent + 2);
                }
            }
        }
    }

    private static void RenderCode(IAnsiConsole console, string code, string? lang)
    {
        var body = code.TrimEnd('\n', '\r');
        var panel = new Panel(new Text(body))
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey)
            .Expand();
        if (!string.IsNullOrWhiteSpace(lang))
            panel.Header($" {Markup.Escape(lang)} ");
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
                sb.Append("[white on grey23]").Append(Markup.Escape(code.Content)).Append("[/]");
                break;
            case LinkInline link:
            {
                var label = new StringBuilder();
                foreach (var child in link)
                    AppendInline(label, child);
                var url = link.Url ?? string.Empty;
                if (!string.IsNullOrEmpty(url) && !url.Contains('[') && !url.Contains(']'))
                    sb.Append("[link=").Append(url).Append(']').Append(label).Append("[/]");
                else
                    sb.Append(label);
                break;
            }
            case AutolinkInline auto:
                sb.Append("[link]").Append(Markup.Escape(auto.Url)).Append("[/]");
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
