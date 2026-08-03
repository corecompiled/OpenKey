using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace OpenKey.Gui.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow() => InitializeComponent();

    /// <param name="destructive">
    /// Styles the confirm button as destructive. Off by default, because this window is also used
    /// for plain acknowledgement — About reused it and inherited a dark-red "Close" button, which
    /// on the light theme was a maroon block on near-white.
    /// </param>
    public ConfirmWindow(string title, string body, string confirmLabel, bool destructive = false) : this()
    {
        Title = title;
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("BodyText")!.Text = body;

        var confirm = this.FindControl<Button>("ConfirmButton")!;
        confirm.Content = confirmLabel;
        confirm.Classes.Add(destructive ? "destructive" : "primary");

        Opened += (_, _) => this.FindControl<Button>("CancelButton")?.Focus();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
