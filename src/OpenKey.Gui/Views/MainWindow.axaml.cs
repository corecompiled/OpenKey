using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using OpenKey.Core.Providers;
using OpenKey.Gui.ViewModels;

namespace OpenKey.Gui.Views;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType[] MarkdownFileType =
    {
        new("Markdown") { Patterns = new[] { "*.md" } },
    };

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

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        // Reflect the persisted theme without firing SetTheme back at the view model.
        Opened += (_, _) =>
        {
            if (this.FindControl<ComboBox>("ThemePicker") is { } picker)
                picker.SelectedItem = Vm.Theme;
        };
    }

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

    private async void OnRetry(object? sender, RoutedEventArgs e) => await Vm.RetryAsync();

    private async void OnCopyLast(object? sender, RoutedEventArgs e)
    {
        if (Vm.LastReply is not { } text)
        {
            Vm.NotifyStatus(StatusKind.Info, "No reply to copy yet.");
            return;
        }

        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        await clipboard.SetTextAsync(text);
        Vm.NotifyStatus(StatusKind.Ok, "Last reply copied to the clipboard.");
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (Vm.BuildExport() is not { } markdown)
        {
            Vm.NotifyStatus(StatusKind.Info, "Nothing to export yet.");
            return;
        }

        // A real save dialog rather than the console's "guess a path on the Desktop": in a window
        // the user expects to choose, and expects to be told where it went.
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save conversation",
            SuggestedFileName = MainWindowViewModel.SuggestedExportName,
            DefaultExtension = "md",
            FileTypeChoices = MarkdownFileType,
        });

        if (file is null) return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(markdown);
            Vm.NotifyStatus(StatusKind.Ok, $"Saved to {file.Path.LocalPath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Vm.NotifyStatus(StatusKind.Warn, "Couldn't save there. Try a different folder.");
        }
    }

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string theme }) Vm.SetTheme(theme);
    }

    private async void OnAbout(object? sender, RoutedEventArgs e)
    {
        var body = string.Join("\n", Vm.AboutRows.Select(r => $"{r.Label}:  {r.Value}"));
        await new ConfirmWindow("About OpenKey", body, "Close").ShowDialog<bool>(this);
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
