using System.Runtime.CompilerServices;
using OpenKey.Core.Providers;
using OpenKey.Core.Storage;

namespace OpenKey.Core.Engine;

public sealed class ChatEngine
{
    private const int MaxAttempts = 5;
    private const int ResponseTokenReserve = 1024;
    private const string DefaultSystemPrompt = "You are a helpful assistant.";

    private readonly IChatProvider _provider;
    private readonly IRotationPolicy _rotation;
    private readonly IModelCatalog _catalog;
    private readonly ISessionStore _sessions;

    private readonly List<ChatMessage> _turns = new();

    public ChatEngine(
        IChatProvider provider,
        IRotationPolicy rotation,
        IModelCatalog catalog,
        ISessionStore sessions)
    {
        _provider = provider;
        _rotation = rotation;
        _catalog = catalog;
        _sessions = sessions;
    }

    public ModelInfo? ActiveModel { get; private set; }

    public string? PreferredModelId { get; set; }

    public IReadOnlyList<ChatMessage> Turns => _turns;

    public event Action<string>? OnRotation;

    public async Task ResumeAsync(CancellationToken ct)
    {
        var snap = await _sessions.LoadAsync(ct);
        if (snap is null)
        {
            ResetTurnsToSystemOnly();
            return;
        }

        _turns.Clear();
        _turns.AddRange(snap.Turns);
        if (_turns.Count == 0 || _turns[0].Role != ChatMessage.SystemRole)
            _turns.Insert(0, new ChatMessage(ChatMessage.SystemRole, DefaultSystemPrompt));
    }

    public Task NewSessionAsync(CancellationToken ct)
    {
        ResetTurnsToSystemOnly();
        _sessions.Clear();
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ChatChunk> SendAsync(
        string userText,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var userTurn = new ChatMessage(ChatMessage.UserRole, userText);
        _turns.Add(userTurn);
        var userTurnIndex = _turns.Count - 1;
        var succeeded = false;

        // try/finally — legal around `yield`, unlike try/catch — so that a failed or cancelled
        // turn does not leave its user message stranded in history to be persisted later.
        try
        {
            ChatException? lastError = null;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

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
                var request = new ChatRequest(model.Id, messages);

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
        await _sessions.SaveAsync(
            new SessionSnapshot(model.Id, DateTimeOffset.UtcNow, _turns.ToArray()),
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

        while (EstimateTokens(trimmed) > max && trimmed.Count > 2)
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
