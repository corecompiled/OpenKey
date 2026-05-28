using System.Reflection;
using OpenKey.Core.AppPaths;
using OpenKey.Core.Engine;
using OpenKey.Core.Providers;
using OpenKey.Core.Storage;
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
                if (AnsiConsole.Confirm("Wipe all OpenKey data and start fresh?", defaultValue: false))
                {
                    await _resetAction(ct);
                }
                else
                {
                    AnsiConsole.MarkupLine("[grey]reset cancelled.[/]");
                }
                return CommandResult.Handled;

            default:
                AnsiConsole.MarkupLine($"[red]unknown command:[/] {Markup.Escape(cmd)}");
                return CommandResult.Handled;
        }
    }

    private void ShowActiveModel()
    {
        var m = _engine.ActiveModel;
        if (m is null)
            AnsiConsole.MarkupLine("[grey]no active model yet — send a message first.[/]");
        else
            AnsiConsole.MarkupLine($"current model: [bold cyan]{Markup.Escape(m.Id)}[/]");
    }

    private async Task ShowModelPickerAsync(CancellationToken ct)
    {
        IReadOnlyList<ModelInfo>? models = null;
        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("loading free models…", async _ =>
                {
                    models = await _catalog.GetFreeModelsAsync(ct);
                });
        }
        catch (ChatException ex)
        {
            AnsiConsole.MarkupLine($"[red]could not load models ({ex.Kind}):[/] {Markup.Escape(ex.Message)}");
            return;
        }

        if (models is null || models.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]no free models available.[/]");
            return;
        }

        var prompt = new SelectionPrompt<string>()
            .Title("pick a model ([grey]↑/↓ to scroll, enter to select[/])")
            .PageSize(15)
            .MoreChoicesText("[grey](move up and down for more)[/]")
            .AddChoices(new[] { AutoChoiceLabel }.Concat(models.Select(m => m.Id)));

        var choice = AnsiConsole.Prompt(prompt);

        if (choice == AutoChoiceLabel)
        {
            _engine.PreferredModelId = null;
            AnsiConsole.MarkupLine("[green]rotation re-enabled.[/]");
            return;
        }

        _engine.PreferredModelId = choice;
        var ctx = models.FirstOrDefault(m => m.Id == choice)?.ContextLength;
        var ctxText = ctx is > 0 ? $" [grey](ctx {ctx})[/]" : string.Empty;
        AnsiConsole.MarkupLine($"[green]pinned:[/] [bold cyan]{Markup.Escape(choice)}[/]{ctxText} [grey](until restart)[/]");
    }

    private void ShowAbout()
    {
        var ver = typeof(CommandRouter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(CommandRouter).Assembly.GetName().Version?.ToString()
            ?? "dev";

        var active = _engine.ActiveModel?.Id ?? "(none yet — send a message)";
        var pinned = _engine.PreferredModelId ?? "(auto / rotate)";

        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap().PadRight(2))
            .AddColumn();

        grid.AddRow("[grey]Version[/]",      $"[bold]{Markup.Escape(ver)}[/]");
        grid.AddRow("[grey]Data dir[/]",     Markup.Escape(_paths.RootDir));
        grid.AddRow("[grey]Active model[/]", Markup.Escape(active));
        grid.AddRow("[grey]Pinned model[/]", Markup.Escape(pinned));
        grid.AddRow("[grey]Developer[/]",    "Paolo Patron");

        AnsiConsole.Write(new Panel(grid)
            .Header("[bold cyan] OpenKey [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey));
    }

    private static void ShowHelp()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[bold]Command[/]"))
            .AddColumn(new TableColumn("[bold]What it does[/]"));

        table.AddRow("[cyan]/about[/]",  "Show version, data dir, active model, dev info");
        table.AddRow("[cyan]/models[/]", "Pick a free model with arrow keys ([grey]pinned until restart[/])");
        table.AddRow("[cyan]/model[/]",  "Show the current active free model");
        table.AddRow("[cyan]/cls[/]",    "Clear the screen and reprint the header");
        table.AddRow("[cyan]/help[/]",   "Show this list of commands");
        table.AddRow("[cyan]/reset[/]",  "Wipe all OpenKey data and re-run first-run setup");
        table.AddRow("[cyan]/quit[/]",   "Exit the app cleanly ([grey]alias: /exit[/])");

        AnsiConsole.Write(table);
    }
}

public enum CommandResult
{
    NotACommand,
    Handled,
    Exit,
}
