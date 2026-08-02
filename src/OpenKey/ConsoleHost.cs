using System.Reflection;
using System.Text;
using OpenKey.Core.AppPaths;
using OpenKey.Core.Engine;
using OpenKey.Core.Providers;
using OpenKey.Core.Storage;
using OpenKey.OAuth;
using OpenKey.Providers.OpenRouter;
using OpenKey.Ui;
using Spectre.Console;

namespace OpenKey;

public sealed class ConsoleHost
{
    // Computed, not a field initializer: those run at DI construction, before ConsoleLayout.Initialize
    // resolves the glyph tier, so a cached value would always be the ASCII fallback.
    private static string UserPrompt =>
        $"[{Theme.Strong}]{Markup.Escape(Environment.UserName)}[/] [{Theme.Brand}]{Glyphs.Caret}[/] ";

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

        ConsoleLayout.Initialize();

        // The banner is printed by first-run setup (which needs it) or by the home header (which
        // clears first). Printing it here too showed it twice whenever the screen couldn't be cleared.
        if (!await EnsureFirstRunAsync(CancellationToken.None))
        {
            Components.HintLine("Setup didn't finish, so OpenKey will close.");
            HoldIfOwnConsole();
            return;
        }

        _commands = new CommandRouter(_engine, _paths, _catalog, ResetAllAsync, ClearAndShowChatHeader);

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

        AnsiConsole.WriteLine();
        Components.HintLine("Thanks for using OpenKey.");
        HoldIfOwnConsole();
    }

    private async Task SendAndRenderAsync(string userText)
    {
        var turnCts = new CancellationTokenSource();
        Volatile.Write(ref _turnCts, turnCts);
        var ct = turnCts.Token;

        var started = System.Diagnostics.Stopwatch.StartNew();
        var rotations = 0;
        void CountRotation(string _) => rotations++;
        _engine.OnRotation += CountRotation;

        try
        {
            await using var iter = _engine.SendAsync(userText, ct).GetAsyncEnumerator(ct);

            // Phase 1 — spinner owns the screen until there is something to show. The enumerator is
            // created outside the callback so it survives the handoff; returning ends the spinner.
            ChatChunk? firstText = null;
            var finishedDuringSpinner = false;

            await AnsiConsole.Status()
                .Spinner(Glyphs.Spinner)
                .SpinnerStyle(new Style(Color.Grey))
                .StartAsync($"[{Theme.Muted}]Thinking[/]", async _ =>
                {
                    while (await iter.MoveNextAsync())
                    {
                        var c = iter.Current;
                        if (!string.IsNullOrEmpty(c.DeltaText)) { firstText = c; return; }
                        if (c.IsFinal) { finishedDuringSpinner = true; return; }
                    }
                    finishedDuringSpinner = true;
                });

            if (firstText is null && finishedDuringSpinner)
            {
                Components.StatusCard(
                    Severity.Warn,
                    "No reply came back",
                    "The model accepted the message but returned nothing.",
                    "Send it again, or type /models to try a different model.");
                return;
            }

            // Phase 2 — spinner is torn down and the cursor restored, so text can stream freely.
            // Per-token writing under a live spinner garbles: the spinner repaints from column 0.
            Components.ReplyHeader(_engine.ActiveModel?.Id ?? "unknown", started.Elapsed);
            Components.RotationNote(rotations);

            var writer = new TranscriptWriter(AnsiConsole.Console);
            writer.Append(firstText!.DeltaText);

            if (!firstText.IsFinal)
            {
                while (await iter.MoveNextAsync())
                {
                    var c = iter.Current;

                    // The engine abandoned this attempt; everything shown so far belongs to it.
                    if (c.IsAttemptRestart) { writer.Reset(); rotations++; continue; }

                    if (!string.IsNullOrEmpty(c.DeltaText)) writer.Append(c.DeltaText);
                    if (c.IsFinal) break;
                }
            }

            writer.Complete();
            AnsiConsole.WriteLine();
        }
        catch (OperationCanceledException) when (turnCts.IsCancellationRequested)
        {
            // Filtered on our own token so an upstream deadline isn't mislabelled "cancelled".
            AnsiConsole.WriteLine();
            Components.HintLine("Stopped.");
            AnsiConsole.WriteLine();
        }
        catch (ChatException ex)
        {
            ShowChatError(ex);
        }
        finally
        {
            _engine.OnRotation -= CountRotation;
            Volatile.Write(ref _turnCts, null);
            turnCts.Dispose();

            // Status() hides the cursor, so an aborted turn must restore it — but only when there
            // is a real console. Redirected, the legacy backend reaches for a handle that isn't
            // there and throws "The handle is invalid", killing the app after a successful reply.
            if (ConsoleLayout.Rich)
            {
                try { AnsiConsole.Cursor.Show(); } catch (IOException) { }
            }
        }
    }

    /// <summary>
    /// Maps an error onto copy the user can act on. Raw <see cref="ChatErrorKind"/> names and raw
    /// exception messages never reach the screen — they were the most developer-tool-looking thing
    /// in the app, and they told the user nothing about what to do next.
    /// </summary>
    private static void ShowChatError(ChatException ex)
    {
        var (severity, title, detail, next) = ex.Kind switch
        {
            ChatErrorKind.AuthFailure => (
                Severity.Danger,
                "Your key was refused",
                "OpenRouter did not accept the saved key. It may have been revoked or replaced.",
                $"Type [{Theme.Brand}]/reset[/] to sign in again. This also erases your chat history."),

            ChatErrorKind.QuotaExhausted => (
                Severity.Danger,
                "This key is out of credit",
                "OpenRouter reports no remaining allowance for this key.",
                "Add credit at https://openrouter.ai, or wait for your free allowance to renew."),

            ChatErrorKind.NetworkDown => (
                Severity.Warn,
                "Can't reach OpenRouter",
                ex.Message,
                "Check your internet connection and send the message again."),

            ChatErrorKind.TransientRateLimit => (
                Severity.Warn,
                "Every free model is busy right now",
                ex.Message,
                $"Wait a moment and send your message again, or type [{Theme.Brand}]/models[/] to pick a different one."),

            ChatErrorKind.InvalidRequest => (
                Severity.Danger,
                "The model refused this message",
                "It may be too long for the model's context, or the model may no longer exist.",
                $"Try a shorter message, or type [{Theme.Brand}]/models[/] to pick a different model."),

            _ => (
                Severity.Warn,
                "That didn't go through",
                ex.Message,
                $"Send it again, or type [{Theme.Brand}]/models[/] to try a different model."),
        };

        Components.StatusCard(severity, title, detail, next);
    }

    private static void ClearAndShowChatHeader() => Components.HomeHeader();

    /// <summary>
    /// Past turns are rendered entirely grey and indented, so the resumed history reads as inert
    /// rather than as part of the live conversation.
    /// </summary>
    private void ShowResumeRecapIfAny()
    {
        var nonSystem = _engine.Turns.Where(t => t.Role != ChatMessage.SystemRole).ToList();
        if (nonSystem.Count == 0) return;

        Components.HintLine("Picking up where you left off.");
        AnsiConsole.WriteLine();

        foreach (var t in nonSystem.TakeLast(2))
        {
            var label = t.Role == ChatMessage.UserRole ? Environment.UserName : "OpenKey AI";
            var flat = t.Content.ReplaceLineEndings(" ").Trim();
            var preview = flat.Length > 70 ? flat[..70] + Glyphs.Ellipsis : flat;
            AnsiConsole.MarkupLine(
                $"  [{Theme.Muted}]{Markup.Escape(label.PadRight(12))} {Markup.Escape(preview)}[/]");
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

        Components.Banner();

        // No acronyms on the first screen a new user sees. "DPAPI" was the product's second
        // sentence; what matters to them is that the key stays on this PC.
        AnsiConsole.MarkupLine("Welcome to OpenKey. Chat with capable AI models for free, with no subscription.");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            "You need a free OpenRouter key once. OpenKey encrypts it for your Windows account and keeps it");
        AnsiConsole.MarkupLine("on this PC. It is never sent anywhere except OpenRouter.");
        AnsiConsole.WriteLine();

        const string OAuthChoice = "Sign in with my browser";
        const string PasteChoice = "Paste a key I already have";

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            // No "(attempt 1/3)" counter: showing a retry budget before anything has failed
            // manufactures anxiety. Retries are surfaced only after a failure.
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title(Components.PickerTitle("How would you like to connect?"))
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
                    Components.HintLine("Sign-in cancelled.");
                    continue;
                }
                catch (OAuthPortInUseException)
                {
                    Components.StatusCard(
                        Severity.Warn,
                        "Another app is using the sign-in port",
                        "OpenKey needs port 3000 for a moment to receive the browser sign-in.",
                        "Paste a key instead, or close the other app and try again.");
                    string? pasteKey;
                    try { pasteKey = AcquireKeyViaPaste(); }
                    catch (Exception) { continue; }
                    if (!string.IsNullOrEmpty(pasteKey) && await ValidateAndSaveAsync(pasteKey, ct))
                        return true;
                    continue;
                }
                catch (ChatException ex)
                {
                    Components.StatusCard(
                        Severity.Warn,
                        "Sign-in didn't complete",
                        ex.Message,
                        "Try again, or choose to paste a key instead.");
                    continue;
                }

                switch (outcome.Kind)
                {
                    case OAuthOutcomeKind.KeyAcquired:
                        key = outcome.Key;
                        break;
                    case OAuthOutcomeKind.UserChosePaste:
                        try { key = AcquireKeyViaPaste(); }
                        catch (Exception) { continue; }
                        break;
                    case OAuthOutcomeKind.UserCanceled:
                    default:
                        Components.HintLine("Sign-in cancelled.");
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
                Components.HintLine("No key was entered.");
                continue;
            }

            if (await ValidateAndSaveAsync(key, ct)) return true;
        }

        return false;
    }

    private async Task<OAuthOutcome> AcquireKeyViaOAuthAsync(CancellationToken ct)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Opening your browser to sign in to OpenRouter.");
        AnsiConsole.WriteLine();
        Components.HintLine("Press P to paste a key instead, or Esc to cancel.");

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
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Paste your OpenRouter key below. It won't appear as you type.");
        Components.HintLine("You can create one at https://openrouter.ai/keys");
        AnsiConsole.WriteLine();

        return AnsiConsole.Prompt(
            new TextPrompt<string>("Key: ")
                .Secret()
                .Validate(k => string.IsNullOrWhiteSpace(k)
                    ? ValidationResult.Error($"[{Theme.Danger}]Paste a key to continue, or press Ctrl+C to go back.[/]")
                    : ValidationResult.Success()));
    }

    private async Task<bool> ValidateAndSaveAsync(string key, CancellationToken ct)
    {
        var tmp = new OpenRouterProvider(_http, () => key);
        try
        {
            IReadOnlyList<ModelInfo>? models = null;
            await AnsiConsole.Status()
                .Spinner(Glyphs.Spinner)
                .SpinnerStyle(new Style(Color.Grey))
                .StartAsync($"[{Theme.Muted}]Checking your key[/]", async _ =>
                {
                    models = await tmp.ListModelsAsync(ct);
                });

            if (models is null || models.Count == 0)
            {
                Components.StatusCard(
                    Severity.Warn,
                    "No models came back",
                    "The key worked, but OpenRouter returned an empty model list.",
                    "This is usually temporary. Try again in a moment.");
                return false;
            }

            _keyStore.Save(key);
            Components.SuccessLine("Key saved. You're ready to chat.");
            AnsiConsole.WriteLine();
            return true;
        }
        catch (ChatException ex) when (ex.Kind == ChatErrorKind.AuthFailure)
        {
            Components.StatusCard(
                Severity.Danger,
                "That key wasn't accepted",
                "OpenRouter rejected it. It may be mistyped, revoked, or from a different service.",
                "Check the key at https://openrouter.ai/keys and try again.");
            return false;
        }
        catch (ChatException ex) when (ex.Kind == ChatErrorKind.NetworkDown)
        {
            Components.StatusCard(
                Severity.Warn,
                "Can't reach OpenRouter",
                ex.Message,
                "OpenKey can save the key now and check it the first time you chat.");

            if (AnsiConsole.Confirm("Save the key and continue?", defaultValue: false))
            {
                _keyStore.Save(key);
                return true;
            }
            return false;
        }
        catch (ChatException ex)
        {
            Components.StatusCard(
                Severity.Warn,
                "Couldn't check the key",
                ex.Message,
                "Try again, or paste a different key.");
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
            Components.StatusCard(
                Severity.Warn,
                "Some files couldn't be removed",
                $"OpenKey cleared what it could from {_paths.RootDir}, but something is holding the rest.",
                "Close any other copy of OpenKey and try again.");
        }

        Components.SuccessLine("Everything was cleared.");
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
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("OpenKey needs a key to work, so it will close now.");
        Components.HintLine("Start it again when you're ready to sign in.");
        _exiting = true;
    }

    /// <summary>
    /// Reads one chat line. Uses <see cref="Console.ReadLine"/> rather than a Spectre prompt for
    /// three reasons: it gets the Windows console's native line editor (arrows, Home/End, word
    /// jump, F7 history) which Spectre's reader does not implement; it does not throw when output
    /// is redirected, which Spectre prompts now do; and a multi-line paste leaves its remaining
    /// lines in the driver buffer where they can be drained instead of being executed as commands.
    /// </summary>
    private static string? ReadUserLine()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Markup(UserPrompt);

        var first = Console.ReadLine();

        // A real console echoes the typed line and its newline; a redirected stdin does not, so
        // without this the next output continues on the prompt row.
        if (Console.IsInputRedirected) AnsiConsole.WriteLine();

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
