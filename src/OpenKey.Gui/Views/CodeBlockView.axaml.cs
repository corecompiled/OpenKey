using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using OpenKey.Gui.ViewModels;

namespace OpenKey.Gui.Views;

public partial class CodeBlockView : UserControl
{
    public CodeBlockView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Render();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Render()
    {
        if (DataContext is not MarkdownBlock block) return;

        var label = this.FindControl<TextBlock>("LanguageLabel");
        if (label is not null)
        {
            label.Text = block.Language ?? string.Empty;
            label.IsVisible = block.Language is not null;
        }

        var text = this.FindControl<SelectableTextBlock>("CodeText");
        if (text is null) return;

        text.Inlines?.Clear();

        var tokens = SyntaxHighlighter.Tokenize(block.Text, block.Language);

        // A single plain token is the common case for unknown languages; skip the Inlines
        // machinery entirely so it renders as ordinary text.
        if (tokens.Count == 1 && tokens[0].Kind == TokenKind.Plain)
        {
            text.Text = tokens[0].Text;
            return;
        }

        text.Text = null;
        foreach (var token in tokens)
        {
            var run = new Run(token.Text) { Foreground = BrushFor(token.Kind) };

            // mono has no hue to spend, and its top luminance steps sit close enough that
            // keyword and type would not separate on lightness alone. Weight and slant are the
            // legitimate substitute; in the coloured themes they would just be noise on top of
            // a distinction the colour already makes.
            if (GuiTheme.Current == GuiTheme.Mono)
            {
                if (token.Kind == TokenKind.Keyword) run.FontWeight = FontWeight.SemiBold;
                if (token.Kind == TokenKind.Comment) run.FontStyle = FontStyle.Italic;
            }

            text.Inlines?.Add(run);
        }
    }

    /// <summary>
    /// Resolved from the active theme rather than hardcoded, so code colours follow a theme switch
    /// like everything else.
    /// </summary>
    private IBrush BrushFor(TokenKind kind)
    {
        var key = kind switch
        {
            TokenKind.Keyword => "CodeKeyword",
            TokenKind.StringLiteral => "CodeString",
            TokenKind.Comment => "CodeComment",
            TokenKind.Number => "CodeNumber",
            TokenKind.Type => "CodeType",
            _ => "CodeText",
        };

        return this.TryFindResource(key, out var value) && value is IBrush brush
            ? brush
            : Brushes.Gray;
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MarkdownBlock block) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        await clipboard.SetTextAsync(block.Text);

        // Confirm in place. A toast would be more machinery for less clarity.
        if (sender is Button button)
        {
            button.Content = "Copied";
            await Task.Delay(1400);
            button.Content = "Copy";
        }
    }
}
