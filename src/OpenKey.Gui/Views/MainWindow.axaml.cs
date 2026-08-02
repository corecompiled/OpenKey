using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using OpenKey.Core.Providers;
using OpenKey.Gui.ViewModels;

namespace OpenKey.Gui.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();

        // Keep the newest message in view as a reply streams in, but only when the user is already
        // at the bottom — yanking the view back while they are reading earlier text is worse than
        // not following at all.
        if (this.FindControl<ScrollViewer>("Scroller") is { } scroller)
        {
            scroller.ScrollChanged += (_, _) => { };
            DispatcherTimer.Run(() =>
            {
                if (Vm is { IsBusy: true } && IsNearBottom(scroller)) scroller.ScrollToEnd();
                return true;
            }, TimeSpan.FromMilliseconds(120));
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private static bool IsNearBottom(ScrollViewer scroller) =>
        scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - 80;

    private async void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        // Enter sends; Shift+Enter is a newline. The reverse trips people up constantly.
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;

        e.Handled = true;
        await Vm.SendAsync();
    }

    private async void OnSend(object? sender, RoutedEventArgs e) => await Vm.SendAsync();

    private void OnStop(object? sender, RoutedEventArgs e) => Vm.Stop();

    private async void OnNewChat(object? sender, RoutedEventArgs e) => await Vm.NewConversationAsync();

    private async void OnSignIn(object? sender, RoutedEventArgs e) => await Vm.SignInWithBrowserAsync();

    private async void OnUseKey(object? sender, RoutedEventArgs e) => await Vm.UseTypedKeyAsync();

    private void OnDismissStatus(object? sender, RoutedEventArgs e) => Vm.DismissStatus();

    private void OnModelChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: ModelInfo model }) Vm.PinModel(model);
    }

    private async void OnResetClicked(object? sender, RoutedEventArgs e)
    {
        // Destructive and irreversible, so it states exactly what is lost and defaults to no —
        // the same rule the console's /reset follows.
        var confirm = new ConfirmWindow(
            "Erase everything?",
            "Your saved key and your entire chat history will be deleted from this PC. "
                + "You'll need to sign in again.",
            "Erase everything");

        if (await confirm.ShowDialog<bool>(this)) await Vm.ResetEverythingAsync();
    }
}
