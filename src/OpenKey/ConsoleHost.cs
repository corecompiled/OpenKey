using System.Reflection;
using System.Text;
using OpenKey.Core.AppPaths;
using OpenKey.Core.Engine;
using OpenKey.Core.Providers;
using OpenKey.Core.Storage;
using OpenKey.OAuth;
using OpenKey.Providers.OpenRouter;
using Spectre.Console;

namespace OpenKey;

public sealed class ConsoleHost
{
    private const string AiLabel = "[bold magenta]OpenKey AI[/]";

    private readonly string _userPrompt = $"[bold cyan]{Markup.Escape(Environment.UserName)}[/]: ";
    private readonly string _userLabel = $"[bold cyan]{Markup.Escape(Environment.UserName)}[/]";

    private readonly IAppPaths _paths;
    private readonly IKeyStore _keyStore;
    private readonly ISessionStore _sessions;
    private readonly IModelCatalog _catalog;
    private readonly IRotationPolicy _rotation;
    private readonly ChatEngine _engine;
    private readonly HttpClient _http;

    private CommandRouter _commands = default!;

    public ConsoleHost(
        IAppPaths paths,
        IKeyStore keyStore,
        ISessionStore sessions,
        IModelCatalog catalog,
        IRotationPolicy rotation,
        ChatEngine engine,
        HttpClient http)
    {
        _paths = paths;
        _keyStore = keyStore;
        _sessions = sessions;
        _catalog = catalog;
        _rotation = rotation;
        _engine = engine;
        _http = http;
    }

    public async Task RunAsync()
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        PrintBanner();

        if (!await EnsureFirstRunAsync(CancellationToken.None))
        {
            AnsiConsole.MarkupLine("[red]setup aborted. exiting.[/]");
            return;
        }

        _commands = new CommandRouter(_engine, _paths, _catalog, ResetAllAsync, ClearAndShowChatHeader);
        _engine.OnRotation += msg => AnsiConsole.MarkupLine($"[yellow]rotating: {Markup.Escape(msg)}[/]");

        ClearAndShowChatHeader();

        await _engine.ResumeAsync(CancellationToken.None);
        ShowResumeRecapIfAny();

        while (true)
        {
            string line;
            try
            {
                line = AnsiConsole.Prompt(new TextPrompt<string>(_userPrompt).AllowEmpty());
            }
            catch (Exception)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line)) continue;

            var result = await _commands.HandleAsync(line, CancellationToken.None);
            if (result == CommandResult.Exit) break;
            if (result == CommandResult.Handled) continue;

            await SendAndRenderAsync(line, cts);
        }

        AnsiConsole.MarkupLine("[grey]goodbye.[/]");
    }

    private async Task SendAndRenderAsync(string userText, CancellationTokenSource outerCts)
    {
        using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(outerCts.Token);
        var ct = turnCts.Token;

        try
        {
            await using var iter = _engine.SendAsync(userText, ct).GetAsyncEnumerator(ct);

            var sb = new StringBuilder();
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"{AiLabel} is thinking…", async _ =>
                {
                    while (await iter.MoveNextAsync())
                    {
                        var c = iter.Current;
                        if (!string.IsNullOrEmpty(c.DeltaText))
                            sb.Append(c.DeltaText);
                        if (c.IsFinal) break;
                    }
                });

            if (sb.Length == 0)
            {
                AnsiConsole.MarkupLine("[grey](no response)[/]");
                return;
            }

            AnsiConsole.Markup($"{AiLabel}: ");
            MarkdownConsoleRenderer.Render(AnsiConsole.Console, sb.ToString());
            Console.Out.Flush();
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey](cancelled)[/]");
        }
        catch (ChatException ex) when (ex.Kind == ChatErrorKind.AuthFailure)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[red]auth failure:[/] {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine("[red]run [/]/reset[red] to re-enter your API key.[/]");
        }
        catch (ChatException ex)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[red]error ({ex.Kind}):[/] {Markup.Escape(ex.Message)}");
        }
    }

    private static void PrintBanner()
    {
        var ver = typeof(ConsoleHost).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(ConsoleHost).Assembly.GetName().Version?.ToString()
            ?? "dev";
        AnsiConsole.Write(new Rule($"[bold cyan]OpenKey[/] [grey]v{Markup.Escape(ver)}[/]").LeftJustified());
        AnsiConsole.MarkupLine("[grey]Developed by Paolo Patron[/]");
        AnsiConsole.WriteLine();
    }

    private static void ClearAndShowChatHeader()
    {
        AnsiConsole.Clear();
        PrintBanner();

        var body = new Markup(
            "Type a message to start chatting.\n" +
            "\n" +
            "[cyan]/models[/]   Choose a model\n" +
            "[cyan]/help[/]     View all commands\n" +
            "[cyan]/quit[/]     Exit");
        AnsiConsole.Write(new Panel(body)
            .Header("[bold cyan] Getting started [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey)
            .Padding(1, 0));
        AnsiConsole.WriteLine();
    }

    private void ShowResumeRecapIfAny()
    {
        var turns = _engine.Turns;
        var nonSystem = turns.Where(t => t.Role != ChatMessage.SystemRole).ToList();
        if (nonSystem.Count == 0) return;

        AnsiConsole.MarkupLine("[grey]resumed previous session. last turns:[/]");
        foreach (var t in nonSystem.TakeLast(2))
        {
            var label = t.Role == ChatMessage.UserRole ? _userLabel : AiLabel;
            var preview = t.Content.Length > 200 ? t.Content[..200] + "…" : t.Content;
            AnsiConsole.MarkupLine($"{label}: {Markup.Escape(preview)}");
        }
        AnsiConsole.WriteLine();
    }

    private async Task<bool> EnsureFirstRunAsync(CancellationToken ct)
    {
        if (_keyStore.HasKey())
        {
            var existing = _keyStore.Load();
            if (!string.IsNullOrEmpty(existing)) return true;
            _keyStore.Clear();
        }

        AnsiConsole.MarkupLine("[bold]welcome to OpenKey.[/]");
        AnsiConsole.MarkupLine("[grey]your key will be encrypted via Windows DPAPI for your user account only.[/]");
        AnsiConsole.WriteLine();

        const string OAuthChoice = "Sign in with browser (OAuth/PKCE) — recommended";
        const string PasteChoice = "I already have a key — paste it";

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"how do you want to provide your OpenRouter key? (attempt {attempt}/3)")
                    .AddChoices(OAuthChoice, PasteChoice));

            string? key = null;

            if (choice == OAuthChoice)
            {
                OAuthOutcome outcome;
                try
                {
                    outcome = await AcquireKeyViaOAuthAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    AnsiConsole.MarkupLine("[grey](cancelled)[/]");
                    continue;
                }
                catch (OAuthPortInUseException ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]⚠ {Markup.Escape(ex.Message)}[/]");
                    AnsiConsole.MarkupLine("[grey]switching to paste in this attempt…[/]");
                    string? pasteKey;
                    try { pasteKey = AcquireKeyViaPaste(); }
                    catch (Exception) { continue; }
                    if (!string.IsNullOrEmpty(pasteKey) && await ValidateAndSaveAsync(pasteKey, ct))
                        return true;
                    continue;
                }
                catch (ChatException ex)
                {
                    AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(ex.Kind.ToString())}:[/] {Markup.Escape(ex.Message)}");
                    continue;
                }

                switch (outcome.Kind)
                {
                    case OAuthOutcomeKind.KeyAcquired:
                        key = outcome.Key;
                        break;
                    case OAuthOutcomeKind.UserChosePaste:
                        AnsiConsole.MarkupLine("[grey]switching to paste…[/]");
                        try { key = AcquireKeyViaPaste(); }
                        catch (Exception) { continue; }
                        break;
                    case OAuthOutcomeKind.UserCanceled:
                    default:
                        AnsiConsole.MarkupLine("[grey](cancelled)[/]");
                        continue;
                }
            }
            else
            {
                try { key = AcquireKeyViaPaste(); }
                catch (Exception) { continue; }
            }

            if (string.IsNullOrEmpty(key))
            {
                AnsiConsole.MarkupLine("[red]✗ no key acquired[/]");
                continue;
            }

            if (await ValidateAndSaveAsync(key, ct)) return true;
        }

        return false;
    }

    private async Task<OAuthOutcome> AcquireKeyViaOAuthAsync(CancellationToken ct)
    {
        AnsiConsole.MarkupLine("[grey]opening browser for OpenRouter sign-in…[/]");
        AnsiConsole.MarkupLine("[grey]press [/][bold]p[/][grey] to paste a key instead, [/][bold]c[/][grey] (or Esc) to cancel.[/]");

        using var interruptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var oauth = new OpenRouterOAuth(_http);

        char userChoice = '\0';
        var watcher = Task.Run(() =>
        {
            while (!interruptCts.IsCancellationRequested)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        var k = Console.ReadKey(intercept: true);
                        if (k.Key == ConsoleKey.P) { userChoice = 'p'; interruptCts.Cancel(); return; }
                        if (k.Key == ConsoleKey.C || k.Key == ConsoleKey.Escape) { userChoice = 'c'; interruptCts.Cancel(); return; }
                    }
                }
                catch (InvalidOperationException)
                {
                    // No console (e.g., redirected stdin). Stop watching.
                    return;
                }
                Thread.Sleep(50);
            }
        }, CancellationToken.None);

        try
        {
            var key = await oauth.AcquireKeyAsync(
                onAuthUrl: url => AnsiConsole.MarkupLine($"[grey]if your browser didn't open, visit:[/] [link]{Markup.Escape(url)}[/]"),
                interruptCts.Token);

            return new OAuthOutcome(OAuthOutcomeKind.KeyAcquired, key);
        }
        catch (OperationCanceledException)
        {
            if (userChoice == 'p') return new OAuthOutcome(OAuthOutcomeKind.UserChosePaste, null);
            if (userChoice == 'c') return new OAuthOutcome(OAuthOutcomeKind.UserCanceled, null);
            if (ct.IsCancellationRequested) throw;
            return new OAuthOutcome(OAuthOutcomeKind.UserCanceled, null);
        }
        finally
        {
            interruptCts.Cancel();
            try { await watcher; } catch { /* best-effort */ }
        }
    }

    private static string AcquireKeyViaPaste()
    {
        AnsiConsole.MarkupLine("[grey]paste your OpenRouter API key (input is hidden). get one at [/][link]https://openrouter.ai/keys[/]");
        return AnsiConsole.Prompt(
            new TextPrompt<string>("API key:")
                .Secret()
                .Validate(k => string.IsNullOrWhiteSpace(k)
                    ? ValidationResult.Error("[red]empty[/]")
                    : ValidationResult.Success()));
    }

    private async Task<bool> ValidateAndSaveAsync(string key, CancellationToken ct)
    {
        var tmp = new OpenRouterProvider(_http, () => key);
        try
        {
            IReadOnlyList<ModelInfo>? models = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("validating key against OpenRouter…", async _ =>
                {
                    models = await tmp.ListModelsAsync(ct);
                });

            if (models is null || models.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]✗ empty model list[/]");
                return false;
            }

            _keyStore.Save(key);
            AnsiConsole.MarkupLine("[green]✓ key validated and saved.[/]");
            AnsiConsole.WriteLine();
            return true;
        }
        catch (ChatException ex) when (ex.Kind == ChatErrorKind.AuthFailure)
        {
            AnsiConsole.MarkupLine($"[red]✗ invalid key:[/] {Markup.Escape(ex.Message)}");
            return false;
        }
        catch (ChatException ex) when (ex.Kind == ChatErrorKind.NetworkDown)
        {
            AnsiConsole.MarkupLine($"[red]✗ network unreachable:[/] {Markup.Escape(ex.Message)}");
            if (AnsiConsole.Confirm("save key anyway and try later?", defaultValue: false))
            {
                _keyStore.Save(key);
                return true;
            }
            return false;
        }
        catch (ChatException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ validation failed ({ex.Kind}):[/] {Markup.Escape(ex.Message)}");
            return false;
        }
    }

    private async Task ResetAllAsync(CancellationToken ct)
    {
        await _engine.NewSessionAsync(ct);
        _rotation.Clear();
        _catalog.ClearCache();
        _keyStore.Clear();

        try
        {
            if (Directory.Exists(_paths.RootDir))
                Directory.Delete(_paths.RootDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AnsiConsole.MarkupLine($"[yellow]warn: could not fully remove {Markup.Escape(_paths.RootDir)}: {Markup.Escape(ex.Message)}[/]");
        }

        AnsiConsole.MarkupLine("[green]✓ reset complete.[/]");
        AnsiConsole.WriteLine();

        if (await EnsureFirstRunAsync(ct))
        {
            try { await _catalog.RefreshAsync(ct); } catch (ChatException) { /* will retry on first turn */ }
            ClearAndShowChatHeader();
            await _engine.ResumeAsync(ct);
        }
    }

    private enum OAuthOutcomeKind
    {
        KeyAcquired,
        UserChosePaste,
        UserCanceled,
    }

    private sealed record OAuthOutcome(OAuthOutcomeKind Kind, string? Key);
}
