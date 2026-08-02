using OpenKey.Core.AppPaths;
using OpenKey.Core.Providers;
using OpenKey.Core.Storage;
using Xunit;

namespace OpenKey.Core.Tests;

public sealed class StorageTests
{
    [Fact]
    public async Task SessionRoundTrips()
    {
        using var paths = new TempAppPaths();
        var store = new JsonSessionStore(paths);

        var snap = new SessionSnapshot("model-a", DateTimeOffset.UtcNow, new[]
        {
            new ChatMessage(ChatMessage.SystemRole, "sys"),
            new ChatMessage(ChatMessage.UserRole, "hi"),
            new ChatMessage(ChatMessage.AssistantRole, "hello — with an em-dash and 你好"),
        });

        await store.SaveAsync(snap, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("model-a", loaded!.ModelId);
        Assert.Equal(3, loaded.Turns.Count);
        Assert.Equal(snap.Turns[2].Content, loaded.Turns[2].Content);
    }

    [Fact]
    public async Task CorruptSessionIsQuarantinedRatherThanCrashing()
    {
        using var paths = new TempAppPaths();
        var store = new JsonSessionStore(paths);
        var file = ((IAppPaths)paths).SessionFile;
        await File.WriteAllTextAsync(file, "{ this is not json");

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Null(loaded);
        Assert.False(File.Exists(file));
        Assert.NotEmpty(Directory.GetFiles(paths.RootDir, "session.json.broken-*"));
    }

    [Fact]
    public async Task SavingIsBestEffortWhenTheTargetCannotBeWritten()
    {
        // A full disk or a read-only roaming profile used to crash the app after a reply had been
        // generated but before it was shown.
        using var paths = new TempAppPaths();
        var store = new JsonSessionStore(paths);

        // A directory where the session file belongs makes File.Create fail.
        Directory.CreateDirectory(((IAppPaths)paths).SessionFile);

        var snap = new SessionSnapshot("m", DateTimeOffset.UtcNow, Array.Empty<ChatMessage>());
        await store.SaveAsync(snap, CancellationToken.None);   // must not throw
    }

    [Fact]
    public async Task AnEmptyFreeModelListIsNeverCached()
    {
        // Caching an empty list pinned "no models" for the full 24h TTL, and because every launch
        // then found a valid-but-empty cache the app stayed broken until %APPDATA% was deleted.
        using var paths = new TempAppPaths();
        var provider = new FakeChatProvider
        {
            Models = new[] { new ModelInfo("paid", "Paid", 8000, false) },
        };
        var catalog = new JsonModelCatalog(paths, provider);

        await Assert.ThrowsAsync<ChatException>(() => catalog.RefreshAsync(CancellationToken.None));
        Assert.False(File.Exists(((IAppPaths)paths).ModelsCacheFile));
    }

    [Fact]
    public async Task FreeModelsAreCachedAndReadBack()
    {
        using var paths = new TempAppPaths();
        var provider = new FakeChatProvider
        {
            Models = new[]
            {
                new ModelInfo("free-1", "Free One", 8000, true),
                new ModelInfo("paid-1", "Paid One", 8000, false),
            },
        };

        var catalog = new JsonModelCatalog(paths, provider);
        var models = await catalog.GetFreeModelsAsync(CancellationToken.None);

        Assert.Single(models);
        Assert.Equal("free-1", models[0].Id);
        Assert.True(File.Exists(((IAppPaths)paths).ModelsCacheFile));

        // A second catalog over the same directory must read the cache rather than the provider.
        var reread = await new JsonModelCatalog(paths, new FakeChatProvider()).GetFreeModelsAsync(
            CancellationToken.None);
        Assert.Single(reread);
        Assert.Equal("free-1", reread[0].Id);
    }
}
