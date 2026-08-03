using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace OpenKey.Gui.Views;

public partial class RenameWindow : Window
{
    public RenameWindow() => InitializeComponent();

    /// <param name="heading">
    /// What is being renamed. Parameterised because the name dialog is the same shape as the chat
    /// one, and a second window differing only in a string would be two things to keep in sync.
    /// </param>
    public RenameWindow(
        string current,
        string heading = "Rename this chat",
        string placeholder = "Chat name",
        string confirmLabel = "Rename")
        : this()
    {
        Title = heading;
        this.FindControl<TextBlock>("HeadingText")!.Text = heading;
        this.FindControl<Button>("ConfirmButton")!.Content = confirmLabel;

        var box = this.FindControl<TextBox>("TitleBox")!;
        box.PlaceholderText = placeholder;
        box.Text = current;

        // Selected, so typing replaces the old name — renaming usually means replacing.
        Opened += (_, _) => { box.SelectAll(); box.Focus(); };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnRename(object? sender, RoutedEventArgs e) =>
        Close(this.FindControl<TextBox>("TitleBox")?.Text?.Trim() is { Length: > 0 } t ? t : null);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
