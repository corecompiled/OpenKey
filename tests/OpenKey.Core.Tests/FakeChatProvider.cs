using System.Runtime.CompilerServices;
using OpenKey.Core.Providers;

namespace OpenKey.Core.Tests;

/// <summary>
/// Scriptable <see cref="IChatProvider"/>. Its absence is why <see cref="Engine.ChatEngine"/> — the
/// whole retry and rotation state machine — had no coverage at all.
/// </summary>
internal sealed class FakeChatProvider : IChatProvider
{
    private readonly Queue<Attempt> _attempts = new();

    public string Id => "fake";
    public string DisplayName => "Fake";

    public List<string> ModelsCalled { get; } = new();
    public IReadOnlyList<ModelInfo> Models { get; set; } = Array.Empty<ModelInfo>();

    private sealed record Attempt(IReadOnlyList<ChatChunk> Chunks, ChatException? Error, bool ErrorBeforeAnyChunk);

    /// <summary>Queues an attempt that streams the given text and finishes cleanly.</summary>
    public FakeChatProvider ThenSucceeds(params string[] deltas)
    {
        var chunks = deltas.Select(d => new ChatChunk(d, false, null)).ToList();
        chunks.Add(new ChatChunk(string.Empty, true, "stop"));
        _attempts.Enqueue(new Attempt(chunks, null, false));
        return this;
    }

    /// <summary>Queues an attempt that streams text and then fails part-way, as a stall would.</summary>
    public FakeChatProvider ThenFailsMidStream(ChatErrorKind kind, params string[] deltas)
    {
        var chunks = deltas.Select(d => new ChatChunk(d, false, null)).ToList();
        _attempts.Enqueue(new Attempt(chunks, new ChatException(kind, $"fake {kind}"), false));
        return this;
    }

    /// <summary>Queues an attempt that fails before producing anything.</summary>
    public FakeChatProvider ThenFails(ChatErrorKind kind)
    {
        _attempts.Enqueue(new Attempt(Array.Empty<ChatChunk>(), new ChatException(kind, $"fake {kind}"), true));
        return this;
    }

    /// <summary>Queues an attempt whose stream ends with no final chunk at all.</summary>
    public FakeChatProvider ThenEndsWithoutFinalChunk(params string[] deltas)
    {
        var chunks = deltas.Select(d => new ChatChunk(d, false, null)).ToList();
        _attempts.Enqueue(new Attempt(chunks, null, false));
        return this;
    }

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct) => Task.FromResult(Models);

    public async IAsyncEnumerable<ChatChunk> StreamChatAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ModelsCalled.Add(request.Model);

        if (_attempts.Count == 0)
            throw new ChatException(ChatErrorKind.TransientServer, "fake ran out of scripted attempts");

        var attempt = _attempts.Dequeue();

        if (attempt.ErrorBeforeAnyChunk && attempt.Error is not null) throw attempt.Error;

        foreach (var chunk in attempt.Chunks)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return chunk;
        }

        if (attempt.Error is not null) throw attempt.Error;
    }
}
