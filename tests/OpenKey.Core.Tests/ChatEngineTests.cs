using OpenKey.Core.AppPaths;
using OpenKey.Core.Engine;
using OpenKey.Core.Providers;
using OpenKey.Core.Storage;
using Xunit;

namespace OpenKey.Core.Tests;

/// <summary>
/// Covers the retry/rotation state machine, which had no tests before because no
/// <see cref="IChatProvider"/> fake existed.
/// </summary>
public sealed class ChatEngineTests
{
    private static readonly ModelInfo ModelA = new("model-a", "Model A", 8000, true);
    private static readonly ModelInfo ModelB = new("model-b", "Model B", 8000, true);

    /// <summary>
    /// Mirrors how the host starts up. ResumeAsync is what seeds the system prompt, so skipping it
    /// would test an engine in a state the app never actually reaches.
    /// </summary>
    private static async Task<(ChatEngine Engine, FakeChatProvider Provider, TempAppPaths Paths)> BuildAsync(
        params ModelInfo[] models)
    {
        var paths = new TempAppPaths();
        var provider = new FakeChatProvider { Models = models };
        var rotation = new RotationPolicy(paths);
        var catalog = new JsonModelCatalog(paths, provider);
        var sessions = new JsonChatStore(paths);
        var config = new JsonConfigStore(paths);
        var engine = new ChatEngine(provider, rotation, catalog, sessions, config);
        await engine.ResumeAsync(CancellationToken.None);
        return (engine, provider, paths);
    }

    private static async Task<string> DrainAsync(ChatEngine engine, string text)
    {
        var sb = new System.Text.StringBuilder();
        await foreach (var chunk in engine.SendAsync(text, CancellationToken.None))
        {
            if (chunk.IsAttemptRestart) sb.Clear();
            sb.Append(chunk.DeltaText);
        }
        return sb.ToString();
    }

    [Fact]
    public async Task StreamsChunksAndPersistsTheTurn()
    {
        var (engine, provider, paths) = await BuildAsync(ModelA);
        using var _ = paths;
        provider.ThenSucceeds("Hello", " world");

        var text = await DrainAsync(engine, "hi");

        Assert.Equal("Hello world", text);
        Assert.Equal(3, engine.Turns.Count);              // system + user + assistant
        Assert.Equal("Hello world", engine.Turns[^1].Content);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(paths.RootDir, "chats"), "*.json"));
    }

    [Fact]
    public async Task PersistsTheTurnEvenWhenTheConsumerStopsAtTheFinalChunk()
    {
        // The natural way to consume this stream — and what the console host does — is to stop as
        // soon as IsFinal arrives. That disposes the iterator at the yield, so any bookkeeping
        // placed after it silently never runs: the reply was shown but never saved, and the
        // model's success was never recorded. Draining to completion hides the bug, so this test
        // deliberately breaks early.
        var (engine, provider, paths) = await BuildAsync(ModelA);
        using var _ = paths;
        provider.ThenSucceeds("persisted");

        await foreach (var chunk in engine.SendAsync("hi", CancellationToken.None))
        {
            if (chunk.IsFinal) break;
        }

        Assert.Equal(3, engine.Turns.Count);                      // system + user + assistant
        Assert.Equal("persisted", engine.Turns[^1].Content);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(paths.RootDir, "chats"), "*.json"));
    }

    [Fact]
    public async Task RotatesToAnotherModelOnTransientFailure()
    {
        var (engine, provider, paths) = await BuildAsync(ModelA, ModelB);
        using var _ = paths;
        provider.ThenFails(ChatErrorKind.TransientRateLimit).ThenSucceeds("recovered");

        var text = await DrainAsync(engine, "hi");

        Assert.Equal("recovered", text);
        Assert.Equal(new[] { "model-a", "model-b" }, provider.ModelsCalled);
    }

    [Fact]
    public async Task SignalsRestartSoPartialTextFromAFailedAttemptIsDiscarded()
    {
        var (engine, provider, paths) = await BuildAsync(ModelA, ModelB);
        using var _ = paths;

        // First model emits real text, then dies. Without the restart signal the consumer would
        // concatenate both attempts and show the answer twice.
        provider.ThenFailsMidStream(ChatErrorKind.TransientServer, "The capital of France is Par")
                .ThenSucceeds("The capital of France is Paris.");

        var sawRestart = false;
        var sb = new System.Text.StringBuilder();
        await foreach (var chunk in engine.SendAsync("capital of france", CancellationToken.None))
        {
            if (chunk.IsAttemptRestart) { sawRestart = true; sb.Clear(); continue; }
            sb.Append(chunk.DeltaText);
        }

        Assert.True(sawRestart);
        Assert.Equal("The capital of France is Paris.", sb.ToString());
        Assert.Equal("The capital of France is Paris.", engine.Turns[^1].Content);
    }

    [Fact]
    public async Task DoesNotRotateWhenTheNetworkIsDown()
    {
        var (engine, provider, paths) = await BuildAsync(ModelA, ModelB);
        using var _ = paths;
        provider.ThenFails(ChatErrorKind.NetworkDown).ThenSucceeds("never reached");

        var ex = await Assert.ThrowsAsync<ChatException>(() => DrainAsync(engine, "hi"));

        Assert.Equal(ChatErrorKind.NetworkDown, ex.Kind);
        Assert.Single(provider.ModelsCalled);          // stopped instead of burning every model
    }

    [Fact]
    public async Task DoesNotRotateOnAnInvalidRequest()
    {
        var (engine, provider, paths) = await BuildAsync(ModelA, ModelB);
        using var _ = paths;
        provider.ThenFails(ChatErrorKind.InvalidRequest).ThenSucceeds("never reached");

        var ex = await Assert.ThrowsAsync<ChatException>(() => DrainAsync(engine, "hi"));

        Assert.Equal(ChatErrorKind.InvalidRequest, ex.Kind);
        Assert.Single(provider.ModelsCalled);
    }

    [Fact]
    public async Task RemovesTheUserTurnWhenTheRequestFails()
    {
        var (engine, provider, paths) = await BuildAsync(ModelA);
        using var _ = paths;
        provider.ThenFails(ChatErrorKind.AuthFailure);

        await Assert.ThrowsAsync<ChatException>(() => DrainAsync(engine, "this must not stick"));

        // Only the system prompt survives; a failed turn must not be persisted on the next success.
        Assert.Single(engine.Turns);
        Assert.Equal(ChatMessage.SystemRole, engine.Turns[0].Role);
    }

    [Fact]
    public async Task RemovesTheUserTurnWhenCancelled()
    {
        var (engine, provider, paths) = await BuildAsync(ModelA);
        using var _ = paths;
        provider.ThenSucceeds("a", "b", "c");

        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _chunk in engine.SendAsync("cancel me", cts.Token))
            {
                cts.Cancel();
            }
        });

        Assert.Single(engine.Turns);
    }

    [Fact]
    public async Task SurfacesAFriendlyErrorWhenNoModelsAreAvailable()
    {
        // Previously RotationPolicy threw a bare InvalidOperationException here, which nothing
        // upstream caught, so an empty model list crashed the app.
        var (engine, _, paths) = await BuildAsync();
        using var __ = paths;

        var ex = await Assert.ThrowsAsync<ChatException>(() => DrainAsync(engine, "hi"));

        Assert.Contains("free models", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TreatsAStreamThatEndsWithoutAFinalChunkAsAFailure()
    {
        var (engine, provider, paths) = await BuildAsync(ModelA, ModelB);
        using var _ = paths;
        provider.ThenEndsWithoutFinalChunk("truncated").ThenSucceeds("complete");

        var text = await DrainAsync(engine, "hi");

        Assert.Equal("complete", text);
    }

    [Fact]
    public async Task PinnedModelIsPreferred()
    {
        var (engine, provider, paths) = await BuildAsync(ModelA, ModelB);
        using var _ = paths;
        engine.PreferredModelId = "model-b";
        provider.ThenSucceeds("ok");

        await DrainAsync(engine, "hi");

        Assert.Equal("model-b", provider.ModelsCalled[0]);
    }
}
