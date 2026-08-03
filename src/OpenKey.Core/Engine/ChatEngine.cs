using System.Runtime.CompilerServices;
using OpenKey.Core.Providers;
using OpenKey.Core.Storage;

namespace OpenKey.Core.Engine;

public sealed class ChatEngine
{
    private const int MaxAttempts = 5;
    private const int ResponseTokenReserve = 1024;

    /// <summary>
    /// Ceiling on a single message, across every attempt.
    /// <para>
    /// The provider bounds each individual read, but five attempts could still stack into several
    /// minutes of a user staring at a spinner. This bounds the sum: once it is spent, the turn
    /// stops rotating and reports rather than starting another attempt.
    /// </para>
    /// </summary>
    private static readonly TimeSpan TurnBudget = TimeSpan.FromMinutes(2);
    private const string DefaultSystemPrompt = "You are a helpful assistant.";

    private readonly IChatProvider _provider;
    private readonly IRotationPolicy _rotation;
    private readonly IModelCatalog _catalog;
    private readonly IChatStore _chats;
    private readonly IConfigStore _config;
    private readonly ITokenCounter _tokens;

    private readonly List<ChatMessage> _turns = new();

    public ChatEngine(
        IChatProvider provider,
        IRotationPolicy rotation,
        IModelCatalog catalog,
        IChatStore chats,
        IConfigStore config,
        ITokenCounter? tokens = null)
    {
        _provider = provider;
        _rotation = rotation;
        _catalog = catalog;
        _chats = chats;
        _config = config;
        _tokens = tokens ?? new HeuristicTokenCounter();
        PreferredModelId = config.Current.PinnedModel;
    }

    public ModelInfo? ActiveModel { get; private set; }

    /// <summary>
    /// Model to try first, or null to let rotation choose. Persisted, so a pin survives a restart —
    /// it was previously session-scoped only because there was nowhere to store it.
    /// </summary>
    public string? PreferredModelId
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            _config.Save(_config.Current.WithPinnedModel(value));
        }
    }

    /// <summary>The last message the user sent, for <c>/retry</c>. Null before the first turn.</summary>
    public string? LastUserMessage { get; private set; }

    public IReadOnlyList<ChatMessage> Turns => _turns;

    public event Action<string>? OnRotation;

    public async Task ResumeAsync(CancellationToken ct)
    {
        var id = await _chats.MostRecentIdAsync(ct);
        if (id is null)
        {
            StartNewChat();
            return;
        }

        await OpenChatAsync(id, ct);
    }

    /// <summary>
    /// Starts a conversation without touching the previous one. It gets an id on first save, so an
    /// empty chat nobody used never reaches disk.
    /// </summary>
    public Task NewSessionAsync(CancellationToken ct)
    {
        StartNewChat();
        return Task.CompletedTask;
    }

    /// <summary>The conversation currently open, or null before anything has been said.</summary>
    public string? CurrentChatId { get; private set; }

    public string CurrentChatTitle { get; private set; } = Chat.Untitled;

    public Task<IReadOnlyList<ChatSummary>> ListChatsAsync(CancellationToken ct) => _chats.ListAsync(ct);

    public async Task<bool> OpenChatAsync(string id, CancellationToken ct)
    {
        var chat = await _chats.LoadAsync(id, ct);
        if (chat is null) return false;

        _turns.Clear();
        _turns.AddRange(chat.Turns);
        if (_turns.Count == 0 || _turns[0].Role != ChatMessage.SystemRole)
            _turns.Insert(0, new ChatMessage(ChatMessage.SystemRole, DefaultSystemPrompt));

        CurrentChatId = chat.Id;
        CurrentChatTitle = chat.Title;
        _createdAt = chat.CreatedAt;
        return true;
    }

    public async Task DeleteChatAsync(string id, CancellationToken ct)
    {
        await _chats.DeleteAsync(id, ct);

        // Deleting the chat you are looking at should leave you somewhere sensible, not staring at
        // a conversation that no longer exists.
        if (CurrentChatId != id) return;

        var next = await _chats.MostRecentIdAsync(ct);
        if (next is null || !await OpenChatAsync(next, ct)) StartNewChat();
    }

    public async Task RenameChatAsync(string id, string title, CancellationToken ct)
    {
        var chat = await _chats.LoadAsync(id, ct);
        if (chat is null) return;

        var clean = string.IsNullOrWhiteSpace(title) ? Chat.Untitled : title.Trim();
        if (clean.Length > Chat.MaxTitleLength) clean = clean[..Chat.MaxTitleLength];

        await _chats.SaveAsync(chat with { Title = clean }, ct);
        if (CurrentChatId == id) CurrentChatTitle = clean;
    }

    private void StartNewChat()
    {
        ResetTurnsToSystemOnly();
        CurrentChatId = null;
        CurrentChatTitle = Chat.Untitled;
        _createdAt = DateTimeOffset.UtcNow;
    }

    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;

    /// <summary>
    /// Puts a previous conversation back and re-persists it. Exists so a host can offer undo after
    /// clearing — destroying someone's conversation should be reversible, and a confirmation
    /// dialog interrupts everyone to protect against a rare mistake, where undo costs nothing
    /// until it is needed.
    /// </summary>
    public async Task RestoreTurnsAsync(IReadOnlyList<ChatMessage> turns, CancellationToken ct)
    {
        _turns.Clear();
        _turns.AddRange(turns);

        if (_turns.Count == 0 || _turns[0].Role != ChatMessage.SystemRole)
            _turns.Insert(0, new ChatMessage(ChatMessage.SystemRole, DefaultSystemPrompt));

        CurrentChatId ??= IChatStore.NewId();
        await PersistAsync(ActiveModel?.Id ?? string.Empty, ct);
    }

    public async IAsyncEnumerable<ChatChunk> SendAsync(
        string userText,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var userTurn = new ChatMessage(ChatMessage.UserRole, userText);
        _turns.Add(userTurn);
        LastUserMessage = userText;
        var userTurnIndex = _turns.Count - 1;
        var succeeded = false;

        // try/finally — legal around `yield`, unlike try/catch — so that a failed or cancelled
        // turn does not leave its user message stranded in history to be persisted later.
        try
        {
            ChatException? lastError = null;
            var startedAt = DateTimeOffset.UtcNow;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                // Checked between attempts, never mid-stream: a reply that is actively arriving is
                // working, however long it has taken, and cutting it off would waste it.
                if (attempt > 1 && DateTimeOffset.UtcNow - startedAt > TurnBudget)
                {
                    lastError ??= new ChatException(
                        ChatErrorKind.TransientServer,
                        "Gave up after trying several models.");
                    break;
                }

                var candidates = await _catalog.GetFreeModelsAsync(ct);

                if (candidates.Count == 0)
                {
                    // Guard before PickAsync, which throws a bare InvalidOperationException on an
                    // empty list — a type nothing upstream catches, so it reached the user as a crash.
                    lastError = new ChatException(
                        ChatErrorKind.TransientServer,
                        "No free models are available right now.");
                    break;
                }

                if (PreferredModelId is { } pref)
                {
                    var list = candidates.ToList();
                    var idx = list.FindIndex(m => m.Id == pref);
                    if (idx > 0)
                    {
                        var picked = list[idx];
                        list.RemoveAt(idx);
                        list.Insert(0, picked);
                        candidates = list;
                    }
                }

                ModelInfo model;
                try
                {
                    model = await _rotation.PickAsync(candidates, ct);
                }
                catch (ChatException ex)
                {
                    lastError = ex;
                    break;
                }

                // Tell the consumer to discard whatever the previous attempt yielded, before any
                // text from this one arrives. Without it a mid-reply rotation renders the answer
                // twice concatenated while the persisted session stores it once.
                if (attempt > 1)
                {
                    yield return new ChatChunk(
                        string.Empty, IsFinal: false, FinishReason: null, IsAttemptRestart: true);
                }

                ActiveModel = model;

                var assistantBuilder = new System.Text.StringBuilder();
                string? finishReason = null;
                ChatException? thisAttemptError = null;

                var messages = BuildMessagesForModel(model);
                var request = new ChatRequest(model.Id, messages, _config.Current.MaxTokens);

                IAsyncEnumerator<ChatChunk>? enumerator = null;
                try
                {
                    enumerator = _provider.StreamChatAsync(request, ct).GetAsyncEnumerator(ct);
                }
                catch (ChatException ex)
                {
                    thisAttemptError = ex;
                }

                if (enumerator is not null)
                {
                    try
                    {
                        while (true)
                        {
                            ChatChunk? chunk = null;
                            try
                            {
                                if (!await enumerator.MoveNextAsync()) break;
                                chunk = enumerator.Current;
                            }
                            catch (ChatException ex)
                            {
                                thisAttemptError = ex;
                                break;
                            }

                            if (chunk is null) break;

                            if (!string.IsNullOrEmpty(chunk.DeltaText))
                                assistantBuilder.Append(chunk.DeltaText);

                            if (chunk.IsFinal)
                            {
                                finishReason = chunk.FinishReason;

                                // Commit BEFORE yielding the final chunk. A consumer that stops
                                // enumerating as soon as it sees IsFinal — which is the natural
                                // way to consume this, and what the console host does — disposes
                                // the iterator at the yield, so anything after it never runs.
                                // Persisting afterwards meant the reply was shown but never saved
                                // and the model's success never recorded.
                                if (thisAttemptError is null && finishReason is not null)
                                {
                                    await CommitTurnAsync(model, assistantBuilder.ToString(), ct);
                                    succeeded = true;
                                }

                                yield return chunk;
                                break;
                            }

                            yield return chunk;
                        }
                    }
                    finally
                    {
                        await enumerator.DisposeAsync();
                    }
                }

                if (succeeded) yield break;

                if (thisAttemptError is not null)
                {
                    // Don't penalise a model for the user's network being down.
                    if (IsModelFault(thisAttemptError.Kind))
                    {
                        _rotation.MarkFailure(
                            model.Id, thisAttemptError.Kind, thisAttemptError.RetryAfterHint);
                        OnRotation?.Invoke($"{model.Id} → {thisAttemptError.Kind}");
                    }

                    lastError = thisAttemptError;
                    if (!IsTransient(thisAttemptError.Kind)) break;
                    continue;
                }

                // No error but stream ended without IsFinal — treat as malformed
                var malformed = new ChatException(
                    ChatErrorKind.MalformedResponse,
                    "Stream ended without final chunk.");
                _rotation.MarkFailure(model.Id, malformed.Kind, null);
                OnRotation?.Invoke($"{model.Id} → {malformed.Kind}");
                lastError = malformed;
            }

            throw lastError ?? new ChatException(
                ChatErrorKind.TransientServer,
                "All free models failed after retries.");
        }
        finally
        {
            // Reference check rather than value equality: two identical messages differ only by
            // timestamp, and removing the wrong one would silently corrupt history.
            if (!succeeded
                && userTurnIndex < _turns.Count
                && ReferenceEquals(_turns[userTurnIndex], userTurn))
            {
                _turns.RemoveAt(userTurnIndex);
            }
        }
    }

    /// <summary>
    /// Records a completed turn: the model succeeded, the assistant reply joins the conversation,
    /// and the session is written to disk.
    /// </summary>
    private async Task CommitTurnAsync(ModelInfo model, string assistantText, CancellationToken ct)
    {
        _rotation.MarkSuccess(model.Id);
        _turns.Add(new ChatMessage(ChatMessage.AssistantRole, assistantText));
        await PersistAsync(model.Id, ct);
    }

    /// <summary>
    /// Writes the open conversation. The id and title are assigned on the first save, so a chat
    /// only exists on disk once something was actually said in it.
    /// </summary>
    private async Task PersistAsync(string modelId, CancellationToken ct)
    {
        CurrentChatId ??= IChatStore.NewId();
        if (CurrentChatTitle == Chat.Untitled) CurrentChatTitle = Chat.TitleFrom(_turns);

        await _chats.SaveAsync(
            new Chat(CurrentChatId, CurrentChatTitle, modelId, _createdAt, DateTimeOffset.UtcNow, _turns.ToArray()),
            ct);
    }

    private void ResetTurnsToSystemOnly()
    {
        _turns.Clear();
        _turns.Add(new ChatMessage(ChatMessage.SystemRole, DefaultSystemPrompt));
    }

    private List<ChatMessage> BuildMessagesForModel(ModelInfo model)
    {
        var max = Math.Max(2048, model.ContextLength - ResponseTokenReserve);
        var trimmed = new List<ChatMessage>(_turns);

        while (_tokens.Count(trimmed) > max && trimmed.Count > 2)
        {
            int dropIdx = trimmed[0].Role == ChatMessage.SystemRole ? 1 : 0;
            trimmed.RemoveAt(dropIdx);
        }

        return trimmed;
    }

    internal static int EstimateTokens(IEnumerable<ChatMessage> msgs)
    {
        int total = 0;
        foreach (var m in msgs)
            total += (m.Role.Length + m.Content.Length) / 4 + 4;
        return total;
    }

    /// <summary>
    /// Whether a failure is worth retrying on a different model.
    /// <para>
    /// <see cref="ChatErrorKind.NetworkDown"/> is deliberately excluded: with no route to the
    /// provider, every model fails identically, so rotating burns all five attempts and leaves
    /// every model on cooldown for a fault that has nothing to do with any of them — the app
    /// would then still be broken after the network came back.
    /// </para>
    /// <para>
    /// <see cref="ChatErrorKind.InvalidRequest"/> is excluded because the request, not the model,
    /// is at fault; an identical retry elsewhere cannot succeed.
    /// </para>
    /// </summary>
    internal static bool IsTransient(ChatErrorKind k) =>
        k is ChatErrorKind.TransientRateLimit
            or ChatErrorKind.TransientServer
            or ChatErrorKind.MalformedResponse;

    /// <summary>
    /// Whether a failure should count against the model itself. A model is not at fault for the
    /// user's network being down, so cooling it down would penalise it for an unrelated outage.
    /// </summary>
    internal static bool IsModelFault(ChatErrorKind k) => k is not ChatErrorKind.NetworkDown;
}
