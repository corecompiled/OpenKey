using System.Diagnostics;
using System.Globalization;
using System.Text;
using OpenKey.Core.AppPaths;
using OpenKey.Core.Engine;
using OpenKey.Core.Providers;
using OpenKey.Core.Storage;
using OpenKey.Ui;
using Spectre.Console;

namespace OpenKey;

public sealed class CommandRouter
{
    private const string AutoChoiceLabel = "Auto (rotate across free models)";

    private readonly ChatEngine _engine;
    private readonly IAppPaths _paths;
    private readonly IModelCatalog _catalog;
    private readonly IConfigStore _config;
    private readonly Func<CancellationToken, Task> _resetAction;
    private readonly Action _clearScreen;

    /// <summary>Set when a command wants the host to resend a message — see <c>/retry</c>.</summary>
    public string? PendingResend { get; private set; }

    public CommandRouter(
        ChatEngine engine,
        IAppPaths paths,
        IModelCatalog catalog,
        IConfigStore config,
        Func<CancellationToken, Task> resetAction,
        Action clearScreen)
    {
        _engine = engine;
        _paths = paths;
        _catalog = catalog;
        _config = config;
        _resetAction = resetAction;
        _clearScreen = clearScreen;
    }

    public async Task<CommandResult> HandleAsync(string input, CancellationToken ct)
    {
        if (!input.StartsWith('/')) return CommandResult.NotACommand;

        PendingResend = null;

        var parts = input.Trim().Split(' ', 2);
        var cmd = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1].Trim() : null;

        switch (cmd)
        {
            case "/quit":
            case "/exit":
                return CommandResult.Exit;

            case "/cls":
                _clearScreen();
                return CommandResult.Handled;

            case "/new":
                await StartNewConversationAsync(ct);
                return CommandResult.Handled;

            case "/chats":
                await ShowChatsAsync(ct);
                return CommandResult.Handled;

            case "/chat":
                await SwitchChatAsync(arg, ct);
                return CommandResult.Handled;

            case "/rename":
                await RenameChatAsync(arg, ct);
                return CommandResult.Handled;

            case "/delete":
                await DeleteChatAsync(arg, ct);
                return CommandResult.Handled;

            case "/retry":
                return Retry();

            case "/history":
                ShowHistory();
                return CommandResult.Handled;

            case "/export":
                Export(arg);
                return CommandResult.Handled;

            case "/copy":
                CopyLastReply();
                return CommandResult.Handled;

            case "/theme":
                SetTheme(arg);
                return CommandResult.Handled;

            case "/model":
                ShowActiveModel();
                return CommandResult.Handled;

            case "/models":
                await ShowModelPickerAsync(ct);
                return CommandResult.Handled;

            case "/about":
                ShowAbout();
                return CommandResult.Handled;

            case "/help":
                ShowHelp();
                return CommandResult.Handled;

            case "/reset":
                // Destructive confirms always state exactly what is lost first, and never default
                // to yes.
                Components.StatusCard(
                    Severity.Warn,
                    "This erases everything",
                    "Your saved key and your entire chat history will be deleted from this PC. "
                        + "You'll need to sign in again.",
                    "Only continue if you meant to start completely fresh.");

                if (Prompts.Confirm("Erase everything and start over?"))
                {
                    await _resetAction(ct);
                }
                else
                {
                    Components.HintLine("Nothing was changed.");
                }
                return CommandResult.Handled;

            default:
                AnsiConsole.MarkupLine($"[{Theme.Muted}]There's no[/] {Markup.Escape(cmd)} [{Theme.Muted}]command.[/]");
                Components.HintLine("Type /help to see what OpenKey can do.");
                return CommandResult.Handled;
        }
    }

    /// <summary>
    /// Starts a conversation alongside the existing ones. Nothing is destroyed — the previous chat
    /// stays in <c>/chats</c>.
    /// </summary>
    private async Task StartNewConversationAsync(CancellationToken ct)
    {
        if (!_engine.Turns.Any(t => t.Role != ChatMessage.SystemRole))
        {
            Components.HintLine("This chat is already empty.");
            return;
        }

        await _engine.NewSessionAsync(ct);
        _clearScreen();
        Components.SuccessLine("Started a new chat. The previous one is in /chats.");
    }

    private async Task<IReadOnlyList<ChatSummary>> ShowChatsAsync(CancellationToken ct)
    {
        var chats = await _engine.ListChatsAsync(ct);
        if (chats.Count == 0)
        {
            Components.HintLine("No saved chats yet.");
            return chats;
        }

        var table = new Table()
            .Border(Glyphs.Table)
            .BorderColor(Color.Grey)
            .Expand()
            .AddColumn(new TableColumn($"[{Theme.Strong}]#[/]").Width(4))
            .AddColumn(new TableColumn($"[{Theme.Strong}]Chat[/]"))
            .AddColumn(new TableColumn($"[{Theme.Strong}]Messages[/]").Width(10))
            .AddColumn(new TableColumn($"[{Theme.Strong}]Last used[/]").Width(16));

        for (var i = 0; i < chats.Count; i++)
        {
            var c = chats[i];
            var current = c.Id == _engine.CurrentChatId;
            var style = current ? Theme.Brand : Theme.Strong;
            table.AddRow(
                $"[{Theme.Muted}]{i + 1}[/]",
                $"[{style}]{Markup.Escape(c.Title)}[/]" + (current ? $" [{Theme.Muted}](open)[/]" : string.Empty),
                $"[{Theme.Muted}]{c.MessageCount}[/]",
                $"[{Theme.Muted}]{Markup.Escape(Ago(c.UpdatedAt))}[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        Components.HintLine("Type /chat 2 to switch, /rename <name> for this one, /delete 2 to remove one.");
        return chats;
    }

    private async Task SwitchChatAsync(string? arg, CancellationToken ct)
    {
        var chats = await ShowChatsAsync(ct);
        if (chats.Count == 0) return;
        if (string.IsNullOrWhiteSpace(arg)) return;

        if (!TryResolve(arg, chats, out var chat))
        {
            Components.HintLine($"No chat {Markup.Escape(arg)}. Use a number from /chats.");
            return;
        }

        if (await _engine.OpenChatAsync(chat.Id, ct))
        {
            _clearScreen();
            Components.SuccessLine($"Opened \"{chat.Title}\".");
        }
        else
        {
            Components.HintLine("That chat could not be opened.");
        }
    }

    private async Task RenameChatAsync(string? arg, CancellationToken ct)
    {
        if (_engine.CurrentChatId is not { } id)
        {
            Components.HintLine("Nothing to rename yet — send a message first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(arg))
        {
            Components.HintLine("Give it a name, for example /rename Trip planning");
            return;
        }

        await _engine.RenameChatAsync(id, arg, ct);
        Components.SuccessLine($"Renamed to \"{arg.Trim()}\".");
    }

    private async Task DeleteChatAsync(string? arg, CancellationToken ct)
    {
        var chats = await _engine.ListChatsAsync(ct);
        if (chats.Count == 0)
        {
            Components.HintLine("No saved chats yet.");
            return;
        }

        if (string.IsNullOrWhiteSpace(arg) || !TryResolve(arg, chats, out var chat))
        {
            Components.HintLine("Say which one, for example /delete 2. Use /chats to see the numbers.");
            return;
        }

        if (!Prompts.Confirm($"Delete \"{chat.Title}\"? This can't be undone."))
        {
            Components.HintLine("Nothing was changed.");
            return;
        }

        await _engine.DeleteChatAsync(chat.Id, ct);
        Components.SuccessLine($"Deleted \"{chat.Title}\".");
    }

    private static bool TryResolve(string arg, IReadOnlyList<ChatSummary> chats, out ChatSummary chat)
    {
        if (int.TryParse(arg.Trim(), out var n) && n >= 1 && n <= chats.Count)
        {
            chat = chats[n - 1];
            return true;
        }

        chat = default!;
        return false;
    }

    /// <summary>Relative time reads faster than a timestamp when scanning a list.</summary>
    private static string Ago(DateTimeOffset when)
    {
        var d = DateTimeOffset.UtcNow - when;
        if (d < TimeSpan.FromMinutes(1)) return "just now";
        if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes}m ago";
        if (d < TimeSpan.FromDays(1)) return $"{(int)d.TotalHours}h ago";
        if (d < TimeSpan.FromDays(30)) return $"{(int)d.TotalDays}d ago";
        return when.LocalDateTime.ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture);
    }

    private CommandResult Retry()
    {
        if (_engine.LastUserMessage is not { } last)
        {
            Components.HintLine("Nothing to retry yet — send a message first.");
            return CommandResult.Handled;
        }

        PendingResend = last;
        Components.HintLine($"Resending: {Markup.Escape(Shorten(last, 60))}");
        return CommandResult.Handled;
    }

    private void ShowHistory()
    {
        var turns = _engine.Turns.Where(t => t.Role != ChatMessage.SystemRole).ToList();
        if (turns.Count == 0)
        {
            Components.HintLine("No messages yet.");
            return;
        }

        var table = new Table()
            .Border(Glyphs.Table)
            .BorderColor(Color.Grey)
            .Expand()
            .AddColumn(new TableColumn($"[{Theme.Strong}]Who[/]").Width(12))
            .AddColumn(new TableColumn($"[{Theme.Strong}]Message[/]"));

        foreach (var t in turns)
        {
            var who = t.Role == ChatMessage.UserRole ? Environment.UserName : "OpenKey AI";
            var style = t.Role == ChatMessage.UserRole ? Theme.Strong : Theme.Brand;
            table.AddRow(
                $"[{style}]{Markup.Escape(who)}[/]",
                Markup.Escape(Shorten(t.Content.ReplaceLineEndings(" ").Trim(), 400)));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        Components.HintLine($"{turns.Count} messages. Use /export to save the full text.");
    }

    private void Export(string? path)
    {
        var turns = _engine.Turns.Where(t => t.Role != ChatMessage.SystemRole).ToList();
        if (turns.Count == 0)
        {
            Components.HintLine("Nothing to export yet.");
            return;
        }

        // Default to a timestamped file on the Desktop: a non-technical user shouldn't have to
        // think about paths, and a bare /export should still do something obviously useful.
        if (string.IsNullOrWhiteSpace(path))
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture);
            path = Path.Combine(desktop, $"OpenKey-chat-{stamp}.md");
        }

        try
        {
            var full = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.Append("# OpenKey conversation\n\n");
            sb.Append(CultureInfo.InvariantCulture, $"Exported {DateTimeOffset.Now:yyyy-MM-dd HH:mm}\n");
            if (_engine.ActiveModel is { } m)
                sb.Append(CultureInfo.InvariantCulture, $"Model: {m.Id}\n");
            sb.Append('\n');

            foreach (var t in turns)
            {
                var who = t.Role == ChatMessage.UserRole ? "You" : "OpenKey AI";
                sb.Append(CultureInfo.InvariantCulture, $"## {who}\n\n{t.Content.TrimEnd()}\n\n");
            }

            File.WriteAllText(full, sb.ToString());
            Components.SuccessLine($"Saved to {full}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Components.StatusCard(
                Severity.Warn,
                "Couldn't save the file",
                ex.Message,
                "Try /export with a different location, for example /export C:\\Users\\Me\\chat.md");
        }
    }

    /// <summary>
    /// Copies the last reply via <c>clip.exe</c>. A console app has no clipboard API without
    /// dragging in a UI framework, and <c>clip.exe</c> ships with Windows.
    /// </summary>
    private void CopyLastReply()
    {
        var last = _engine.Turns.LastOrDefault(t => t.Role == ChatMessage.AssistantRole);
        if (last is null)
        {
            Components.HintLine("No reply to copy yet.");
            return;
        }

        try
        {
            var psi = new ProcessStartInfo("clip.exe")
            {
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                Components.HintLine("Couldn't reach the Windows clipboard.");
                return;
            }

            proc.StandardInput.Write(last.Content);
            proc.StandardInput.Close();
            proc.WaitForExit(5000);

            Components.SuccessLine("Last reply copied to the clipboard.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            Components.HintLine("Couldn't reach the Windows clipboard.");
        }
    }

    private void SetTheme(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            AnsiConsole.MarkupLine(
                $"Current theme: [{Theme.Brand}]{Markup.Escape(Theme.Current)}[/]");
            Components.HintLine($"Choose one of: {string.Join(", ", OpenKeyConfigThemes.All)} — for example /theme light");
            return;
        }

        var wanted = name.Trim().ToLowerInvariant();
        if (!Theme.IsKnown(wanted))
        {
            Components.HintLine($"There's no \"{Markup.Escape(wanted)}\" theme. Try: {string.Join(", ", OpenKeyConfigThemes.All)}");
            return;
        }

        Theme.Apply(wanted);
        _config.Save(_config.Current with { Theme = wanted });
        _clearScreen();
        Components.SuccessLine($"Theme set to {wanted}.");
    }

    private static string Shorten(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + Glyphs.Ellipsis;

    private void ShowActiveModel()
    {
        var m = _engine.ActiveModel;
        if (m is null)
            Components.HintLine("No model has answered yet. Send a message first.");
        else
            AnsiConsole.MarkupLine($"Currently answering with [{Theme.Brand}]{Markup.Escape(m.Id)}[/].");
    }

    private async Task ShowModelPickerAsync(CancellationToken ct)
    {
        IReadOnlyList<ModelInfo>? models = null;
        try
        {
            await AnsiConsole.Status()
                .Spinner(Glyphs.Spinner)
                .SpinnerStyle(new Style(Color.Grey))
                .StartAsync($"[{Theme.Muted}]Loading models[/]", async _ =>
                {
                    models = await _catalog.GetFreeModelsAsync(ct);
                });
        }
        catch (ChatException ex)
        {
            Components.StatusCard(
                Severity.Warn,
                "Couldn't load the model list",
                ex.Message,
                "Check your connection and try /models again.");
            return;
        }

        if (models is null || models.Count == 0)
        {
            Components.StatusCard(
                Severity.Warn,
                "No free models available",
                "OpenRouter didn't offer any free models just now.",
                "This is usually temporary. Try /models again shortly.");
            return;
        }

        // Show the human-readable name and context size, not the raw id. DisplayName was fetched
        // from the API and then never displayed anywhere.
        var rows = models
            .Select(m => $"{Truncate(m.DisplayName, 44).PadRight(46)}{FormatContext(m.ContextLength)}")
            .ToList();

        var choices = new List<string> { AutoChoiceLabel };
        choices.AddRange(rows);

        var choice = Prompts.Select("Which model should answer you?", choices);
        if (choice is null)
        {
            Components.HintLine("Kept the current choice.");
            return;
        }

        if (choice == AutoChoiceLabel)
        {
            _engine.PreferredModelId = null;
            Components.SuccessLine("OpenKey will pick the best available model for each message.");
            return;
        }

        var picked = models[rows.IndexOf(choice)];
        _engine.PreferredModelId = picked.Id;
        Components.SuccessLine($"Now using {picked.DisplayName}.");
        Components.HintLine("Remembered for next time. Choose Auto to hand the choice back.");
    }

    private static string FormatContext(int contextLength) =>
        contextLength <= 0 ? string.Empty
        : contextLength >= 1000 ? $"{contextLength / 1000}k context"
        : $"{contextLength} context";

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + Glyphs.Ellipsis;

    private void ShowAbout() =>
        Components.KeyValuePanel("About OpenKey", new (string, string)[]
        {
            ("Version", Components.Version),
            ("This chat", _engine.CurrentChatId is null ? "Not saved yet — send a message" : _engine.CurrentChatTitle),
            ("Answering with", _engine.ActiveModel?.Id ?? "Nothing yet — send a message"),
            ("Model choice", _engine.PreferredModelId ?? "Automatic"),
            ("Your data", _paths.RootDir),
            ("Key security", "Encrypted for your Windows account, stored on this PC only"),
            ("Developer", "Paolo Patron"),
        });

    private static void ShowHelp()
    {
        var table = new Table()
            .Border(Glyphs.Table)
            .BorderColor(Color.Grey)
            .Expand()
            .AddColumn(new TableColumn($"[{Theme.Strong}]Command[/]"))
            .AddColumn(new TableColumn($"[{Theme.Strong}]What it does[/]"));

        void Row(string cmd, string what) =>
            table.AddRow($"[{Theme.Brand}]{cmd}[/]", what);

        table.AddRow($"[{Theme.Muted}]Chatting[/]", string.Empty);
        Row("/new", "Start another chat, keeping this one");
        Row("/chats", "List your saved chats");
        Row("/chat", "Switch to another chat, e.g. /chat 2");
        Row("/rename", "Rename this chat");
        Row("/delete", "Delete a chat, e.g. /delete 2");
        Row("/retry", "Send your last message again");
        Row("/history", "Show the conversation so far");
        Row("/copy", "Copy the last reply to the clipboard");
        Row("/export", "Save the conversation as a markdown file");

        table.AddEmptyRow();
        table.AddRow($"[{Theme.Muted}]Models[/]", string.Empty);
        Row("/models", "Choose which AI model answers you");
        Row("/model", "Show which model is answering right now");

        table.AddEmptyRow();
        table.AddRow($"[{Theme.Muted}]OpenKey[/]", string.Empty);
        Row("/theme", "Switch colours: default, dark, light, mono");
        Row("/about", "Show version, where your data lives, and who made this");
        Row("/cls", "Clear the screen");
        Row("/help", "Show this list");
        Row("/reset", "Erase everything and start over, including your key and chat history");
        Row("/quit", "Close OpenKey");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        Components.HintLine("Anything that doesn't start with / is sent to the AI. Press Ctrl+C to stop a reply.");
    }
}

public enum CommandResult
{
    NotACommand,
    Handled,
    Exit,
}
