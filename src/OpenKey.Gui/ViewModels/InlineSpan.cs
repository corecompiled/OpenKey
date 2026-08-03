namespace OpenKey.Gui.ViewModels;

/// <summary>
/// How a run of text within a block is emphasised. Flags, because markdown nests: <c>***x***</c>
/// parses as bold wrapping italic, and both have to survive to the renderer.
/// </summary>
[Flags]
public enum InlineStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Code = 4,
    Link = 8,
    Strikethrough = 16,
}

/// <summary>
/// A run of text with one style, produced by flattening Markdig's inline tree.
/// <para>
/// The view model stops here and does not build UI elements: this stays a plain list of records so
/// the parsing can be tested without starting a window, which is the same split the block model
/// already uses.
/// </para>
/// </summary>
/// <param name="Url">Set only for <see cref="InlineStyle.Link"/>; the destination.</param>
public sealed record InlineSpan(string Text, InlineStyle Style = InlineStyle.None, string? Url = null)
{
    public bool IsBold => Style.HasFlag(InlineStyle.Bold);
    public bool IsItalic => Style.HasFlag(InlineStyle.Italic);
    public bool IsCode => Style.HasFlag(InlineStyle.Code);
    public bool IsLink => Style.HasFlag(InlineStyle.Link);
    public bool IsStruck => Style.HasFlag(InlineStyle.Strikethrough);
}
