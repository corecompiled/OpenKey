using System.Reflection;
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
    private readonly Func<CancellationToken, Task> _resetAction;
    private readonly Action _clearScreen;

    public CommandRouter(
        ChatEngine engine,
        IAppPaths paths,
        IModelCatalog catalog,
        Func<CancellationToken, Task> resetAction,
        Action clearScreen)
    {
        _engine = engine;
        _paths = paths;
        _catalog = catalog;
        _resetAction = resetAction;
        _clearScreen = clearScreen;
    }

    public async Task<CommandResult> HandleAsync(string input, CancellationToken ct)
    {
        if (!input.StartsWith('/')) return CommandResult.NotACommand;

        var parts = input.Trim().Split(' ', 2);
        var cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "/quit":
            case "/exit":
                return CommandResult.Exit;

            case "/cls":
                _clearScreen();
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

                if (AnsiConsole.Confirm("Erase everything and start over?", defaultValue: false))
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

        var prompt = new SelectionPrompt<string>
        {
            Title = Components.PickerTitle("Which model should answer you?"),
            PageSize = 12,
            MoreChoicesText = $"[{Theme.Muted}]More below[/]",
        };
        prompt.AddChoice(AutoChoiceLabel);
        foreach (var row in rows) prompt.AddChoice(row);

        string choice = AnsiConsole.Prompt(prompt);

        if (choice == AutoChoiceLabel)
        {
            _engine.PreferredModelId = null;
            Components.SuccessLine("OpenKey will pick the best available model for each message.");
            return;
        }

        var picked = models[rows.IndexOf(choice)];
        _engine.PreferredModelId = picked.Id;
        Components.SuccessLine($"Now using {picked.DisplayName}.");
        Components.HintLine("This lasts until you close OpenKey.");
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

        Row("/models", "Choose which AI model answers you");
        Row("/model", "Show which model is answering right now");
        Row("/about", "Show version, where your data lives, and who made this");
        Row("/cls", "Clear the screen");
        Row("/help", "Show this list");
        Row("/reset", "Erase everything and start over, including your key and chat history");
        Row("/quit", "Close OpenKey");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        Components.HintLine("Anything that doesn't start with / is sent to the AI.");
    }
}

public enum CommandResult
{
    NotACommand,
    Handled,
    Exit,
}
