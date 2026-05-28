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
        _turns.Add(new ChatMessage(ChatMessage.UserRole, userText));

        ChatException? lastError = null;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var candidates = await _catalog.GetFreeModelsAsync(ct);

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

            if (thisAttemptError is null && finishReason is not null)
            {
                _rotation.MarkSuccess(model.Id);
                var assistantText = assistantBuilder.ToString();
                _turns.Add(new ChatMessage(ChatMessage.AssistantRole, assistantText));
                await _sessions.SaveAsync(
                    new SessionSnapshot(model.Id, DateTimeOffset.UtcNow, _turns.ToArray()),
                    ct);
                yield break;
            }

            if (thisAttemptError is not null)
            {
                _rotation.MarkFailure(model.Id, thisAttemptError.Kind, thisAttemptError.RetryAfterHint);
                OnRotation?.Invoke($"{model.Id} → {thisAttemptError.Kind}");

                if (!IsTransient(thisAttemptError.Kind))
                {
                    lastError = thisAttemptError;
                    break;
                }

                lastError = thisAttemptError;
                continue;
            }

            // No error but stream ended without IsFinal — treat as malformed
            var malformed = new ChatException(
                ChatErrorKind.MalformedResponse,
                "Stream ended without final chunk.");
            _rotation.MarkFailure(model.Id, malformed.Kind, null);
            lastError = malformed;
        }

        throw lastError ?? new ChatException(
            ChatErrorKind.TransientServer,
            "All free models failed after retries.");
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

    internal static bool IsTransient(ChatErrorKind k) =>
        k is ChatErrorKind.TransientRateLimit
            or ChatErrorKind.TransientServer
            or ChatErrorKind.NetworkDown
            or ChatErrorKind.MalformedResponse;
}
