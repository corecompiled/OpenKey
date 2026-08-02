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

    private CancellationTokenSource? _turnCts;
    private volatile bool _exiting;

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
        // One CTS per turn, held here so the Ctrl+C handler can reach the in-flight one.
        // The previous design used a single process-lifetime CTS: once Ctrl+C cancelled it, every
        // later turn was born already cancelled and the session was unusable until restart.
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;                       // always swallow; never let the CLR kill us
            var turn = Volatile.Read(ref _turnCts);
            if (turn is not null && !turn.IsCancellationRequested)
            {
                turn.Cancel();                     // mid-turn: cancel just this reply
            }
            else
            {
                _exiting = true;                   // at the prompt: quit
            }
        };

        PrintBanner();

        if (!await EnsureFirstRunAsync(CancellationToken.None))
        {
            AnsiConsole.MarkupLine("[red]setup aborted. exiting.[/]");
            HoldIfOwnConsole();
            return;
        }

        _commands = new CommandRouter(_engine, _paths, _catalog, ResetAllAsync, ClearAndShowChatHeader);
        _engine.OnRotation += msg => AnsiConsole.MarkupLine($"[yellow]rotating: {Markup.Escape(msg)}[/]");

        ClearAndShowChatHeader();

        await _engine.ResumeAsync(CancellationToken.None);
        ShowResumeRecapIfAny();

        while (!_exiting)
        {
            string? line;
            try
            {
                line = ReadUserLine();
            }
            catch (Exception)
            {
                break;
            }

            if (line is null) break;                       // EOF / Ctrl+D
            if (_exiting) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var result = await _commands.HandleAsync(line, CancellationToken.None);
            if (result == CommandResult.Exit) break;
            if (result == CommandResult.Handled) continue;

            await SendAndRenderAsync(line);
        }

        AnsiConsole.MarkupLine("[grey]goodbye.[/]");
        HoldIfOwnConsole();
    }

    private async Task SendAndRenderAsync(string userText)
    {
        var turnCts = new CancellationTokenSource();
        Volatile.Write(ref _turnCts, turnCts);
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
        catch (OperationCanceledException) when (turnCts.IsCancellationRequested)
        {
            // Filtered on our own token so an upstream deadline isn't mislabelled "cancelled".
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey](cancelled)[/]");
        }
        catch (ChatException ex) when (ex.Kind == ChatErrorKind.AuthFailure)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[red]auth failure:[/] {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine("[red]Run[/] /reset [red]to sign in again. This also erases your chat history.[/]");
        }
        catch (ChatException ex)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[red]error ({ex.Kind}):[/] {Markup.Escape(ex.Message)}");
        }
        finally
        {
            Volatile.Write(ref _turnCts, null);
            turnCts.Dispose();
            AnsiConsole.Cursor.Show();             // Status() hides it; an aborted turn must restore it
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
            return;
        }

        // Setup was abandoned, so there is no key. Returning to the REPL here would strand the user
        // in a loop: every message fails auth, the error says "run /reset", and /reset lands back
        // exactly here. Exiting is the only honest option.
        AnsiConsole.MarkupLine("[red]OpenKey needs a key to work, so it will close now.[/]");
        AnsiConsole.MarkupLine("[grey]Start it again when you're ready to sign in.[/]");
        _exiting = true;
    }

    /// <summary>
    /// Reads one chat line. Uses <see cref="Console.ReadLine"/> rather than a Spectre prompt for
    /// three reasons: it gets the Windows console's native line editor (arrows, Home/End, word
    /// jump, F7 history) which Spectre's reader does not implement; it does not throw when output
    /// is redirected, which Spectre prompts now do; and a multi-line paste leaves its remaining
    /// lines in the driver buffer where they can be drained instead of being executed as commands.
    /// </summary>
    private string? ReadUserLine()
    {
        AnsiConsole.Markup(_userPrompt);

        var first = Console.ReadLine();
        if (first is null) return null;

        // A human cannot type the next line within milliseconds, so input already buffered here
        // means a paste. Drain it so the rest of the paste joins this message rather than being
        // submitted as separate turns — one of which could start with '/' and run as a command.
        if (!TryPeekBufferedInput()) return first;

        var sb = new StringBuilder(first);
        while (TryPeekBufferedInput())
        {
            var next = Console.ReadLine();
            if (next is null) break;
            sb.Append('\n').Append(next);
        }
        return sb.ToString();
    }

    private static bool TryPeekBufferedInput()
    {
        try
        {
            for (var i = 0; i < 3; i++)
            {
                if (Console.KeyAvailable) return true;
                Thread.Sleep(5);
            }
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;   // stdin redirected — no key buffer to inspect
        }
    }

    /// <summary>
    /// Renders an unhandled exception and holds the window. Called from the top-level handler.
    /// </summary>
    public static void ReportFatal(Exception ex)
    {
        try
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[red]OpenKey hit an unexpected problem and has to close.[/]");
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(ex.GetType().Name)}: {Markup.Escape(ex.Message)}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]If this keeps happening, run[/] /reset [grey]on the next start.[/]");
        }
        catch
        {
            Console.WriteLine("OpenKey hit an unexpected problem and has to close.");
            Console.WriteLine(ex);
        }
        HoldIfOwnConsole();
    }

    /// <summary>
    /// When OpenKey owns its console — i.e. it was double-clicked rather than run from an existing
    /// terminal — the window dies with the process, so any parting message is unreadable by
    /// construction. Hold it open in that case only; never when run from a shell or a script.
    /// </summary>
    private static void HoldIfOwnConsole()
    {
        if (!OwnsConsole()) return;
        try
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to close.[/]");
            Console.ReadKey(intercept: true);
        }
        catch (InvalidOperationException)
        {
            // No console to wait on.
        }
    }

    private static bool OwnsConsole()
    {
        try
        {
            if (Console.IsOutputRedirected || Console.IsInputRedirected) return false;
            var buffer = new uint[4];
            var count = GetConsoleProcessList(buffer, (uint)buffer.Length);
            return count == 1;   // only us attached => we created this window
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    // DllImport rather than LibraryImport: the latter requires AllowUnsafeBlocks project-wide,
    // which is a lot of permission to buy for one blittable call.
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList(
        [System.Runtime.InteropServices.Out] uint[] processList, uint processCount);

    private enum OAuthOutcomeKind
    {
        KeyAcquired,
        UserChosePaste,
        UserCanceled,
    }

    private sealed record OAuthOutcome(OAuthOutcomeKind Kind, string? Key);
}
