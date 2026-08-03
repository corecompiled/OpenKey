using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Threading;
using OpenKey.Core.AppPaths;
using OpenKey.Core.Engine;
using OpenKey.Core.Providers;
using OpenKey.Core.Storage;
using OpenKey.Providers.OpenRouter;
using OpenKey.Windows.OAuth;

namespace OpenKey.Gui.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly ChatEngine _engine;
    private readonly IKeyStore _keys;
    private readonly IModelCatalog _catalog;
    private readonly IRotationPolicy _rotation;
    private readonly IConfigStore _config;
    private readonly IAppPaths _paths;
    private readonly HttpClient _http;

    private CancellationTokenSource? _turnCts;
    private string _draft = string.Empty;
    private bool _isBusy;
    private bool _needsKey;
    private string? _status;
    private StatusKind _statusKind;
    private string _keyInput = string.Empty;
    private bool _isSigningIn;

    public MainWindowViewModel(
        ChatEngine engine,
        IKeyStore keys,
        IModelCatalog catalog,
        IRotationPolicy rotation,
        IConfigStore config,
        IAppPaths paths,
        HttpClient http)
    {
        _engine = engine;
        _keys = keys;
        _catalog = catalog;
        _rotation = rotation;
        _config = config;
        _paths = paths;
        _http = http;

        Messages.CollectionChanged += (_, _) => Raise(nameof(IsConversationEmpty));

        StopCommand = new RelayCommand(Stop);
        ClearCommand = new RelayCommand(() => _ = ClearConversationAsync());
    }

    public ObservableCollection<MessageViewModel> Messages { get; } = new();

    /// <summary>Drives the empty-state prompt. A bare Count cannot bind to IsVisible.</summary>
    public bool IsConversationEmpty => Messages.Count == 0;

    public ObservableCollection<ModelChoice> Models { get; } = new();

    private ModelChoice? _selectedModel;

    /// <summary>
    /// Two-way bound so the picker reflects the saved choice on launch instead of showing a
    /// placeholder while a model is actually pinned.
    /// </summary>
    public ModelChoice? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (!Set(ref _selectedModel, value) || value is null) return;
            PinModel(value.Model);
        }
    }

    public string Version { get; } =
        (typeof(MainWindowViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "dev") is var v && v.IndexOf('+', StringComparison.Ordinal) > 0
                ? v[..v.IndexOf('+', StringComparison.Ordinal)]
                : v;

    public string DataDirectory => _paths.RootDir;

    public string Draft
    {
        get => _draft;
        set { if (Set(ref _draft, value)) Raise(nameof(CanSend)); }
    }

    /// <summary>True while a reply is in flight. Drives the send/stop button and input state.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            Raise(nameof(CanSend));
            Raise(nameof(IsIdle));
        }
    }

    public bool IsIdle => !_isBusy;

    public bool CanSend => !_isBusy && !string.IsNullOrWhiteSpace(_draft);

    /// <summary>Shows the sign-in panel instead of the chat view.</summary>
    public bool NeedsKey
    {
        get => _needsKey;
        private set { if (Set(ref _needsKey, value)) Raise(nameof(IsReady)); }
    }

    public bool IsReady => !_needsKey;

    public string KeyInput
    {
        get => _keyInput;
        set => Set(ref _keyInput, value);
    }

    public bool IsSigningIn
    {
        get => _isSigningIn;
        private set => Set(ref _isSigningIn, value);
    }

    public string? Status
    {
        get => _status;
        private set { if (Set(ref _status, value)) Raise(nameof(HasStatus)); }
    }

    public bool HasStatus => !string.IsNullOrEmpty(_status);

    public StatusKind StatusKind
    {
        get => _statusKind;
        private set => Set(ref _statusKind, value);
    }

    public string ActiveModel => _engine.ActiveModel?.Id ?? "Choosing automatically";

    public async Task InitializeAsync()
    {
        if (!_keys.HasKey() || string.IsNullOrEmpty(_keys.Load()))
        {
            NeedsKey = true;
            return;
        }

        NeedsKey = false;
        await _engine.ResumeAsync(CancellationToken.None);

        foreach (var turn in _engine.Turns.Where(t => t.Role != ChatMessage.SystemRole))
        {
            Messages.Add(new MessageViewModel(
                turn.Role == ChatMessage.UserRole ? Speaker.You : Speaker.Assistant,
                turn.Content));
        }

        await LoadModelsAsync();
    }

    public async Task LoadModelsAsync()
    {
        try
        {
            var models = await _catalog.GetFreeModelsAsync(CancellationToken.None);

            Models.Clear();
            Models.Add(ModelChoice.Automatic);
            foreach (var m in models) Models.Add(ModelChoice.For(m));

            // Reflect what is actually saved. Assigning the backing field directly avoids the
            // setter re-pinning the value we just read.
            var pinned = _engine.PreferredModelId;
            _selectedModel = pinned is null
                ? ModelChoice.Automatic
                : Models.FirstOrDefault(c => c.Model?.Id == pinned) ?? ModelChoice.Automatic;
            Raise(nameof(SelectedModel));
        }
        catch (ChatException ex)
        {
            Show(StatusKind.Warn, Friendly(ex));
        }
    }

    // ---- sign in -------------------------------------------------------------------------

    public async Task SignInWithBrowserAsync()
    {
        IsSigningIn = true;
        Show(StatusKind.Info, "Opening your browser to sign in to OpenRouter…");

        try
        {
            var oauth = new OpenRouterOAuth(_http);
            var key = await oauth.AcquireKeyAsync(_ => { }, CancellationToken.None);
            await ValidateAndSaveAsync(key);
        }
        catch (OAuthPortInUseException)
        {
            Show(StatusKind.Warn,
                "OpenKey needs a local port for a moment to receive the sign-in, and they're all in use. "
                + "Paste a key below instead, or close the other app and try again.");
        }
        catch (ChatException ex)
        {
            Show(StatusKind.Warn, Friendly(ex));
        }
        catch (OperationCanceledException)
        {
            Show(StatusKind.Info, "Sign-in cancelled.");
        }
        finally
        {
            IsSigningIn = false;
        }
    }

    public async Task UseTypedKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(KeyInput))
        {
            Show(StatusKind.Warn, "Paste a key to continue.");
            return;
        }

        IsSigningIn = true;
        try
        {
            await ValidateAndSaveAsync(KeyInput.Trim());
        }
        finally
        {
            IsSigningIn = false;
        }
    }

    private async Task ValidateAndSaveAsync(string key)
    {
        Show(StatusKind.Info, "Checking your key…");
        try
        {
            var probe = new OpenRouterProvider(_http, () => key);

            // Auth check first: /models is public and answers 200 for anyone, so on its own it
            // would accept any string as a valid key.
            await probe.ValidateKeyAsync(CancellationToken.None);
            var models = await probe.ListModelsAsync(CancellationToken.None);
            if (models.Count == 0)
            {
                Show(StatusKind.Warn, "The key worked, but OpenRouter returned no models. This is usually temporary.");
                return;
            }

            _keys.Save(key);
            KeyInput = string.Empty;
            Show(StatusKind.Ok, "Key saved. You're ready to chat.");
            NeedsKey = false;
            await InitializeAsync();
        }
        catch (ChatException ex)
        {
            Show(StatusKind.Warn, Friendly(ex));
        }
    }

    // ---- chatting ------------------------------------------------------------------------

    public async Task SendAsync()
    {
        if (!CanSend) return;

        var text = Draft.TrimEnd();
        Draft = string.Empty;
        Status = null;

        if (_clearedTurns is not null)
        {
            _clearedTurns = null;
            _clearedMessages = null;
            Raise(nameof(CanUndoClear));
        }

        Messages.Add(new MessageViewModel(Speaker.You, text));

        var reply = new MessageViewModel(Speaker.Assistant) { IsStreaming = true };
        Messages.Add(reply);

        var cts = new CancellationTokenSource();
        Volatile.Write(ref _turnCts, cts);
        IsBusy = true;

        var started = Stopwatch.StartNew();
        var rotations = 0;
        void CountRotation(string _) => rotations++;
        _engine.OnRotation += CountRotation;

        try
        {
            await foreach (var chunk in _engine.SendAsync(text, cts.Token))
            {
                if (chunk.IsAttemptRestart)
                {
                    // The engine abandoned that attempt; everything shown belongs to it.
                    rotations++;
                    await Dispatcher.UIThread.InvokeAsync(reply.Reset);
                    continue;
                }

                if (!string.IsNullOrEmpty(chunk.DeltaText))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        reply.Append(chunk.DeltaText);
                        reply.ModelId = _engine.ActiveModel?.Id;
                        reply.Elapsed = started.Elapsed;
                    });
                }

                if (chunk.IsFinal) break;
            }

            reply.Elapsed = started.Elapsed;
            reply.ModelId = _engine.ActiveModel?.Id;

            if (!reply.HasText)
                Show(StatusKind.Warn, "The model accepted the message but returned nothing. Try sending it again.");
            else if (rotations > 0)
                Show(StatusKind.Info, $"Moved past {rotations} busy model{(rotations == 1 ? "" : "s")}.");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (!reply.HasText) Messages.Remove(reply);
            Show(StatusKind.Info, "Stopped.");
        }
        catch (ChatException ex)
        {
            if (!reply.HasText) Messages.Remove(reply);
            Show(StatusKind.Danger, Friendly(ex));
        }
        finally
        {
            _engine.OnRotation -= CountRotation;
            reply.Complete();
            Volatile.Write(ref _turnCts, null);
            cts.Dispose();
            IsBusy = false;
            Raise(nameof(ActiveModel));
        }
    }

    public void Stop() => Volatile.Read(ref _turnCts)?.Cancel();

    // Commands exist only so the window's KeyBindings have something to bind to; every button
    // calls the methods directly.
    public System.Windows.Input.ICommand StopCommand { get; }

    public System.Windows.Input.ICommand ClearCommand { get; }

    /// <summary>Resends the last message. Goes through SendAsync so a retry takes the same path.</summary>
    public async Task RetryAsync()
    {
        if (IsBusy) return;

        if (_engine.LastUserMessage is not { } last)
        {
            Show(StatusKind.Info, "Nothing to retry yet — send a message first.");
            return;
        }

        Draft = last;
        await SendAsync();
    }

    public string? LastReply =>
        Messages.LastOrDefault(m => m.Speaker == Speaker.Assistant && m.HasText)?.Text;

    /// <summary>
    /// Renders the conversation as markdown, matching the console's <c>/export</c>. Returns null
    /// when there is nothing to write.
    /// </summary>
    public string? BuildExport()
    {
        var turns = Messages.Where(m => m.HasText).ToList();
        if (turns.Count == 0) return null;

        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("# OpenKey conversation").AppendLine();
        sb.Append(culture, $"Exported {DateTimeOffset.Now:yyyy-MM-dd HH:mm}").AppendLine();
        if (_engine.ActiveModel is { } m) sb.Append(culture, $"Model: {m.Id}").AppendLine();
        sb.AppendLine();

        foreach (var turn in turns)
        {
            var who = turn.IsFromUser ? "You" : "OpenKey AI";
            sb.Append(culture, $"## {who}").AppendLine().AppendLine();
            sb.AppendLine(turn.Text.TrimEnd()).AppendLine();
        }

        return sb.ToString();
    }

    public static string SuggestedExportName =>
        $"OpenKey-chat-{DateTimeOffset.Now:yyyy-MM-dd-HHmm}.md";

    // ---- theme ---------------------------------------------------------------------------

    /// <summary>Raised when the palette changes so the app can repaint. See GuiTheme.</summary>
    public event Action<string>? ThemeChanged;

    public string Theme => _config.Current.Theme;

    public IReadOnlyList<string> Themes { get; } = new[] { "default", "dark", "light", "mono" };

    public void SetTheme(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var wanted = name.Trim().ToLowerInvariant();
        if (wanted == _config.Current.Theme) return;

        _config.Save(_config.Current with { Theme = wanted });
        Raise(nameof(Theme));
        ThemeChanged?.Invoke(wanted);
    }

    // ---- about ---------------------------------------------------------------------------

    public IReadOnlyList<(string Label, string Value)> AboutRows => new[]
    {
        ("Version", Version),
        ("Answering with", _engine.ActiveModel?.Id ?? "Nothing yet — send a message"),
        ("Model choice", _engine.PreferredModelId ?? "Automatic"),
        ("Your data", _paths.RootDir),
        ("Key security", "Encrypted for your Windows account, stored on this PC only"),
        ("Developer", "Paolo Patron"),
    };

    public void NotifyStatus(StatusKind kind, string message) => Show(kind, message);

    private IReadOnlyList<ChatMessage>? _clearedTurns;
    private MessageViewModel[]? _clearedMessages;

    /// <summary>True while a cleared conversation can still be brought back.</summary>
    public bool CanUndoClear => _clearedTurns is not null;

    /// <summary>
    /// Clears the conversation, keeping the key.
    /// <para>
    /// This deletes the only conversation OpenKey stores, so it is offered with undo rather than
    /// behind a confirmation. A dialog interrupts everyone every time to guard against a mistake
    /// that is rare; undo costs nothing until the moment it is needed, and then it costs one
    /// click. The button is also labelled "Clear chat" rather than "New chat" — the latter implies
    /// the old conversation is still somewhere, and it is not.
    /// </para>
    /// </summary>
    public async Task ClearConversationAsync()
    {
        if (IsBusy) return;

        if (Messages.Count == 0)
        {
            Show(StatusKind.Info, "This chat is already empty.");
            return;
        }

        _clearedTurns = _engine.Turns.ToArray();
        _clearedMessages = Messages.ToArray();

        await _engine.NewSessionAsync(CancellationToken.None);
        Messages.Clear();

        Raise(nameof(CanUndoClear));
        Show(StatusKind.Ok, "Chat cleared. Your key is untouched.");
    }

    public async Task UndoClearAsync()
    {
        if (_clearedTurns is null || _clearedMessages is null) return;

        await _engine.RestoreTurnsAsync(_clearedTurns, CancellationToken.None);

        Messages.Clear();
        foreach (var m in _clearedMessages) Messages.Add(m);

        _clearedTurns = null;
        _clearedMessages = null;
        Raise(nameof(CanUndoClear));
        Show(StatusKind.Ok, "Chat restored.");
    }

    public void PinModel(ModelInfo? model)
    {
        _engine.PreferredModelId = model?.Id;
        Show(StatusKind.Ok, model is null
            ? "OpenKey will pick the best available model for each message."
            : $"Now using {model.DisplayName}.");
        Raise(nameof(ActiveModel));
    }

    /// <summary>
    /// Erases everything, matching the console's <c>/reset</c>. The view is responsible for
    /// confirming first — this states plainly what it costs but does not ask.
    /// </summary>
    public async Task ResetEverythingAsync()
    {
        await _engine.NewSessionAsync(CancellationToken.None);
        _rotation.Clear();
        _catalog.ClearCache();
        _keys.Clear();
        _config.Save(OpenKeyConfig.Default);

        Messages.Clear();
        Models.Clear();
        NeedsKey = true;
        Show(StatusKind.Ok, "Everything was erased. Sign in again to continue.");
    }

    // ---- status --------------------------------------------------------------------------

    private void Show(StatusKind kind, string message)
    {
        StatusKind = kind;
        Status = message;
    }

    public void DismissStatus() => Status = null;

    /// <summary>
    /// Same rule as the console: say what happened and what to do next. Raw error-kind names and
    /// exception text never reach the window.
    /// </summary>
    private static string Friendly(ChatException ex) => ex.Kind switch
    {
        ChatErrorKind.AuthFailure =>
            "OpenRouter refused the saved key. Use Erase everything to sign in again — that also clears your chat history.",
        ChatErrorKind.QuotaExhausted =>
            "This key is out of credit. Wait for your free allowance to renew, or add credit at openrouter.ai.",
        ChatErrorKind.NetworkDown =>
            ex.Message + " Check your connection and try again.",
        ChatErrorKind.TransientRateLimit =>
            "Every free model is busy right now. Wait a moment and send again, or pick a specific model.",
        ChatErrorKind.InvalidRequest =>
            "The model refused this message — it may be too long. Try a shorter one, or pick a different model.",
        _ => "That didn't go through. Send it again, or pick a different model.",
    };
}

public enum StatusKind
{
    Info,
    Ok,
    Warn,
    Danger,
}
