using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace OpenKey.Gui.Views;

public partial class RenameWindow : Window
{
    public RenameWindow() => InitializeComponent();

    public RenameWindow(string current) : this()
    {
        var box = this.FindControl<TextBox>("TitleBox")!;
        box.Text = current;

        // Selected, so typing replaces the old name — renaming usually means replacing.
        Opened += (_, _) => { box.SelectAll(); box.Focus(); };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnRename(object? sender, RoutedEventArgs e) =>
        Close(this.FindControl<TextBox>("TitleBox")?.Text?.Trim() is { Length: > 0 } t ? t : null);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
