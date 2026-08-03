using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using OpenKey.Gui.ViewModels;

namespace OpenKey.Gui.Views;

/// <summary>
/// Renders a list of <see cref="InlineSpan"/> into a <see cref="SelectableTextBlock"/>'s inlines.
/// <para>
/// An attached property rather than a control, because the only thing needed is a different way to
/// fill a text block that the template already has. A wrapping control would bring its own layout,
/// its own selection behaviour, and a second place for the transcript's typography to drift.
/// </para>
/// </summary>
public static class InlineText
{
    /// <summary>Monospace stack, matching <c>CodeBlockView</c> so inline and fenced code agree.</summary>
    private static readonly FontFamily Mono = new("Cascadia Mono,Consolas,Courier New,monospace");

    public static readonly AttachedProperty<IReadOnlyList<InlineSpan>?> SpansProperty =
        AvaloniaProperty.RegisterAttached<SelectableTextBlock, IReadOnlyList<InlineSpan>?>(
            "Spans", typeof(InlineText));

    static InlineText() => SpansProperty.Changed.AddClassHandler<SelectableTextBlock>(OnSpansChanged);

    public static void SetSpans(SelectableTextBlock target, IReadOnlyList<InlineSpan>? value) =>
        target.SetValue(SpansProperty, value);

    public static IReadOnlyList<InlineSpan>? GetSpans(SelectableTextBlock target) =>
        target.GetValue(SpansProperty);

    private static void OnSpansChanged(SelectableTextBlock target, AvaloniaPropertyChangedEventArgs e)
    {
        var spans = e.NewValue as IReadOnlyList<InlineSpan>;

        target.Inlines?.Clear();

        if (spans is null || spans.Count == 0)
        {
            target.Text = string.Empty;
            return;
        }

        // One unstyled run is the overwhelmingly common case — most sentences carry no emphasis at
        // all. Setting Text directly skips building the inline collection for them.
        if (spans.Count == 1 && spans[0].Style == InlineStyle.None)
        {
            target.Text = spans[0].Text;
            return;
        }

        // Text and Inlines are alternatives, not additives: a non-null Text would render alongside
        // the runs and duplicate the paragraph.
        target.Text = null;

        foreach (var span in spans) target.Inlines?.Add(ToRun(span));
    }

    private static Run ToRun(InlineSpan span)
    {
        var run = new Run(span.Text);

        if (span.IsBold) run.FontWeight = FontWeight.SemiBold;
        if (span.IsItalic) run.FontStyle = FontStyle.Italic;

        if (span.IsCode)
        {
            // Foreground and face only, no background. A highlight behind a run does not follow the
            // text's line boxes when it wraps, so a long inline snippet breaks into ragged blocks.
            run.FontFamily = Mono;
            run.FontSize = 13;
            Tint(run, "CodeType");
        }

        if (span.IsLink)
        {
            Tint(run, "Brand");
            run.TextDecorations = TextDecorations.Underline;
        }

        if (span.IsStruck)
        {
            run.TextDecorations = TextDecorations.Strikethrough;
            Tint(run, "Muted");
        }

        return run;
    }

    /// <summary>
    /// Binds the foreground to a theme token rather than resolving it now. A resolved brush would be
    /// correct at the moment the message rendered and wrong after the next theme switch, since
    /// nothing rebuilds an already-displayed transcript.
    /// </summary>
    private static void Tint(Run run, string token) =>
        run[!TextElement.ForegroundProperty] = new DynamicResourceExtension(token);
}
