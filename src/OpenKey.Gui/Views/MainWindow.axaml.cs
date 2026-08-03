using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using OpenKey.Core.Providers;
using OpenKey.Core.Storage;
using OpenKey.Gui.ViewModels;

namespace OpenKey.Gui.Views;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType[] MarkdownFileType =
    {
        new("Markdown") { Patterns = new[] { "*.md" } },
    };

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    /// <summary>
    /// Whether the view should stay pinned to the newest content. Cleared when the reader scrolls
    /// up, restored when they come back down or send something.
    /// </summary>
    private bool _followTail = true;

    public MainWindow()
    {
        InitializeComponent();

        if (this.FindControl<ScrollViewer>("Scroller") is { } scroller)
        {
            // Following is a mode the reader controls, not a fixed rule. Scrolling up to re-read
            // turns it off so a streaming reply can't yank the page away; coming back to the
            // bottom turns it on again. Sending is an explicit action and always re-arms it.
            scroller.ScrollChanged += (_, _) => _followTail = IsNearBottom(scroller);

            DispatcherTimer.Run(() =>
            {
                if (Vm is { IsBusy: true } && _followTail) scroller.ScrollToEnd();
                return true;
            }, TimeSpan.FromMilliseconds(120));
        }
    }

    /// <summary>
    /// Jumps to the newest message. Posted rather than called directly: the message has just been
    /// added and the layout pass that gives it a height has not run yet, so scrolling now would
    /// stop short of the real bottom.
    /// </summary>
    private void ScrollToBottomSoon()
    {
        _followTail = true;
        if (this.FindControl<ScrollViewer>("Scroller") is not { } scroller) return;

        Dispatcher.UIThread.Post(scroller.ScrollToEnd, DispatcherPriority.Background);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        // Enter must be intercepted on the TUNNEL route. AcceptsReturn="True" makes the TextBox
        // handle Enter itself and insert a newline, and that happens before a bubbling KeyDown
        // handler ever runs — so setting e.Handled there is too late and Enter behaves exactly
        // like Shift+Enter.
        if (this.FindControl<TextBox>("Composer") is { } composer)
            composer.AddHandler(KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);

        // A chat window should be ready to type in the moment it opens.
        Opened += (_, _) => FocusComposer();
    }

    private static bool IsNearBottom(ScrollViewer scroller) =>
        scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - 80;

    private async void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        // Enter sends; Shift+Enter is a newline. The reverse trips people up constantly.
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;

        e.Handled = true;
        ScrollToBottomSoon();
        await Vm.SendAsync();
    }

    private async void OnSend(object? sender, RoutedEventArgs e)
    {
        ScrollToBottomSoon();
        await Vm.SendAsync();
    }

    private void OnStop(object? sender, RoutedEventArgs e) => Vm.Stop();

    private async void OnNewChat(object? sender, RoutedEventArgs e)
    {
        await Vm.NewChatAsync();
        ScrollToBottomSoon();
        FocusComposer();
    }

    private void OnToggleChats(object? sender, RoutedEventArgs e) => Vm.ShowChats = !Vm.ShowChats;

    private async void OnDeleteChat(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: ChatSummary chat }) return;

        // Deleting a conversation cannot be undone — there is nowhere to put it — so unlike
        // starting a new chat this one asks first.
        var confirm = new ConfirmWindow(
            "Delete this chat?",
            $"\"{chat.Title}\" will be permanently deleted from this PC.",
            "Delete");

        if (await confirm.ShowDialog<bool>(this)) await Vm.DeleteChatAsync(chat);
    }

    private async void OnRenameChat(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: ChatSummary chat }) return;

        var dialog = new RenameWindow(chat.Title);
        if (await dialog.ShowDialog<string?>(this) is { } title)
        {
            if (chat.Id != Vm.SelectedChat?.Id) await Vm.OpenChatAsync(chat.Id);
            await Vm.RenameCurrentChatAsync(title);
        }
    }

    private void OnThemeMenu(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string theme }) Vm.SetTheme(theme);
    }

    /// <summary>
    /// Typing is what someone does next in a chat window, so the caret goes back there after any
    /// action that isn't itself about typing.
    /// </summary>
    private void FocusComposer() => this.FindControl<TextBox>("Composer")?.Focus();

    private async void OnSignIn(object? sender, RoutedEventArgs e) => await Vm.SignInWithBrowserAsync();

    private async void OnUseKey(object? sender, RoutedEventArgs e) => await Vm.UseTypedKeyAsync();

    private void OnDismissStatus(object? sender, RoutedEventArgs e) => Vm.DismissStatus();

    private async void OnRetry(object? sender, RoutedEventArgs e)
    {
        ScrollToBottomSoon();
        await Vm.RetryAsync();
    }

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
