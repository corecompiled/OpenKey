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
    }

    public ObservableCollection<MessageViewModel> Messages { get; } = new();

    /// <summary>Drives the empty-state prompt. A bare Count cannot bind to IsVisible.</summary>
    public bool IsConversationEmpty => Messages.Count == 0;

    public ObservableCollection<ModelInfo> Models { get; } = new();

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
            foreach (var m in models) Models.Add(m);
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

    public async Task NewConversationAsync()
    {
        if (IsBusy) return;
        await _engine.NewSessionAsync(CancellationToken.None);
        Messages.Clear();
        Show(StatusKind.Ok, "Started a new conversation. Your key is untouched.");
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
