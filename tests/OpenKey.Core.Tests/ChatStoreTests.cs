using System.Text.Json;
using OpenKey.Core.AppPaths;
using OpenKey.Core.Providers;
using OpenKey.Core.Storage;
using Xunit;

namespace OpenKey.Core.Tests;

public sealed class ChatStoreTests
{
    private static Chat MakeChat(string id, string title, params string[] userMessages)
    {
        var turns = new List<ChatMessage> { new(ChatMessage.SystemRole, "sys") };
        foreach (var m in userMessages)
        {
            turns.Add(new ChatMessage(ChatMessage.UserRole, m));
            turns.Add(new ChatMessage(ChatMessage.AssistantRole, "ok"));
        }

        return new Chat(id, title, "model", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, turns);
    }

    [Fact]
    public async Task SavesAndReloadsAConversation()
    {
        using var paths = new TempAppPaths();
        var store = new JsonChatStore(paths);

        await store.SaveAsync(MakeChat("a1", "First", "hello"), CancellationToken.None);
        var loaded = await store.LoadAsync("a1", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("First", loaded!.Title);
        Assert.Equal(3, loaded.Turns.Count);
    }

    [Fact]
    public async Task ListsNewestFirstAndExcludesTheSystemPromptFromCounts()
    {
        using var paths = new TempAppPaths();
        var store = new JsonChatStore(paths);

        var older = MakeChat("a1", "Older", "one") with { UpdatedAt = DateTimeOffset.UtcNow.AddHours(-2) };
        await store.SaveAsync(older, CancellationToken.None);
        await store.SaveAsync(MakeChat("a2", "Newer", "two"), CancellationToken.None);

        var list = await store.ListAsync(CancellationToken.None);

        Assert.Equal(2, list.Count);
        Assert.Equal("Newer", list[0].Title);
        Assert.Equal(2, list[0].MessageCount);   // user + assistant, not the system prompt
    }

    [Fact]
    public async Task DeletingRemovesItFromTheList()
    {
        using var paths = new TempAppPaths();
        var store = new JsonChatStore(paths);

        await store.SaveAsync(MakeChat("a1", "Keep", "x"), CancellationToken.None);
        await store.SaveAsync(MakeChat("a2", "Drop", "y"), CancellationToken.None);
        await store.DeleteAsync("a2", CancellationToken.None);

        var list = await store.ListAsync(CancellationToken.None);
        Assert.Equal("Keep", Assert.Single(list).Title);
    }

    [Fact]
    public async Task MostRecentIsWhatOpensOnLaunch()
    {
        using var paths = new TempAppPaths();
        var store = new JsonChatStore(paths);

        Assert.Null(await store.MostRecentIdAsync(CancellationToken.None));

        await store.SaveAsync(MakeChat("a1", "Old", "x") with { UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1) }, CancellationToken.None);
        await store.SaveAsync(MakeChat("a2", "New", "y"), CancellationToken.None);

        Assert.Equal("a2", await store.MostRecentIdAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AStaleIndexIsRebuiltRatherThanTrusted()
    {
        // index.json is a cache. If it disagrees with the files on disk the files win, because a
        // cache that lies is worse than no cache.
        using var paths = new TempAppPaths();
        var store = new JsonChatStore(paths);
        await store.SaveAsync(MakeChat("a1", "Real", "x"), CancellationToken.None);

        var indexPath = Path.Combine(paths.RootDir, "chats", "index.json");
        await File.WriteAllTextAsync(indexPath,
            """{"chats":[{"id":"ghost","title":"Not real","updatedAt":"2026-01-01T00:00:00+00:00","messageCount":9}]}""");

        var list = await new JsonChatStore(paths).ListAsync(CancellationToken.None);

        Assert.Equal("Real", Assert.Single(list).Title);
    }

    [Fact]
    public async Task ACorruptIndexIsRebuilt()
    {
        using var paths = new TempAppPaths();
        var store = new JsonChatStore(paths);
        await store.SaveAsync(MakeChat("a1", "Survivor", "x"), CancellationToken.None);

        await File.WriteAllTextAsync(Path.Combine(paths.RootDir, "chats", "index.json"), "{ not json");

        var list = await new JsonChatStore(paths).ListAsync(CancellationToken.None);
        Assert.Equal("Survivor", Assert.Single(list).Title);
    }

    [Fact]
    public async Task OneCorruptChatDoesNotTakeTheOthersDown()
    {
        // The whole reason for a folder of files rather than one document.
        using var paths = new TempAppPaths();
        var store = new JsonChatStore(paths);
        await store.SaveAsync(MakeChat("good", "Fine", "x"), CancellationToken.None);

        await File.WriteAllTextAsync(Path.Combine(paths.RootDir, "chats", "bad.json"), "{ broken");
        File.Delete(Path.Combine(paths.RootDir, "chats", "index.json"));

        var list = await new JsonChatStore(paths).ListAsync(CancellationToken.None);

        Assert.Equal("Fine", Assert.Single(list).Title);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(paths.RootDir, "chats"), "bad.json.broken-*"));
    }

    [Fact]
    public async Task ClearRemovesEverything()
    {
        using var paths = new TempAppPaths();
        var store = new JsonChatStore(paths);
        await store.SaveAsync(MakeChat("a1", "Gone", "x"), CancellationToken.None);

        store.Clear();

        Assert.Empty(await store.ListAsync(CancellationToken.None));
    }
}

public sealed class ChatMigrationTests
{
    [Fact]
    public async Task APreHistorySessionBecomesTheFirstChat()
    {
        // Anyone upgrading has exactly one conversation in session.json. Losing it would be the
        // worst possible outcome of adding history.
        using var paths = new TempAppPaths();

        var snap = new SessionSnapshot("model-x", DateTimeOffset.UtcNow.AddDays(-1), new ChatMessage[]
        {
            new(ChatMessage.SystemRole, "sys"),
            new(ChatMessage.UserRole, "What is the capital of France?"),
            new(ChatMessage.AssistantRole, "Paris."),
        });

        await using (var stream = File.Create(((IAppPaths)paths).SessionFile))
        {
            await JsonSerializer.SerializeAsync(stream, snap, OpenKeyJsonContextAccessor.Session);
        }

        var list = await new JsonChatStore(paths).ListAsync(CancellationToken.None);

        var migrated = Assert.Single(list);
        Assert.Equal("What is the capital of France?", migrated.Title);
        Assert.Equal(2, migrated.MessageCount);
    }

    [Fact]
    public async Task TheOriginalSessionFileIsKeptNotDeleted()
    {
        using var paths = new TempAppPaths();
        var file = ((IAppPaths)paths).SessionFile;

        var snap = new SessionSnapshot("m", DateTimeOffset.UtcNow, new ChatMessage[]
        {
            new(ChatMessage.UserRole, "hello"),
            new(ChatMessage.AssistantRole, "hi"),
        });
        await using (var stream = File.Create(file))
        {
            await JsonSerializer.SerializeAsync(stream, snap, OpenKeyJsonContextAccessor.Session);
        }

        await new JsonChatStore(paths).ListAsync(CancellationToken.None);

        // Renamed rather than removed, so a failed migration can never be why someone lost the
        // only conversation they had.
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(file + ".migrated"));
    }

    [Fact]
    public async Task AnEmptySessionMigratesToNothingRatherThanAnEmptyChat()
    {
        using var paths = new TempAppPaths();
        var snap = new SessionSnapshot("m", DateTimeOffset.UtcNow, Array.Empty<ChatMessage>());
        await using (var stream = File.Create(((IAppPaths)paths).SessionFile))
        {
            await JsonSerializer.SerializeAsync(stream, snap, OpenKeyJsonContextAccessor.Session);
        }

        Assert.Empty(await new JsonChatStore(paths).ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ACorruptSessionIsLeftAloneRatherThanDiscarded()
    {
        using var paths = new TempAppPaths();
        var file = ((IAppPaths)paths).SessionFile;
        await File.WriteAllTextAsync(file, "{ not json at all");

        var list = await new JsonChatStore(paths).ListAsync(CancellationToken.None);

        Assert.Empty(list);
        Assert.True(File.Exists(file));   // still there for anyone who wants to recover it
    }

    [Fact]
    public async Task MigrationHappensOnceAndDoesNotDuplicate()
    {
        using var paths = new TempAppPaths();
        var snap = new SessionSnapshot("m", DateTimeOffset.UtcNow, new ChatMessage[]
        {
            new(ChatMessage.UserRole, "only once"),
            new(ChatMessage.AssistantRole, "ok"),
        });
        await using (var stream = File.Create(((IAppPaths)paths).SessionFile))
        {
            await JsonSerializer.SerializeAsync(stream, snap, OpenKeyJsonContextAccessor.Session);
        }

        await new JsonChatStore(paths).ListAsync(CancellationToken.None);
        var second = await new JsonChatStore(paths).ListAsync(CancellationToken.None);

        Assert.Single(second);
    }
}

public sealed class ChatTitleTests
{
    [Fact]
    public void TitleComesFromTheFirstUserMessage()
    {
        var title = Chat.TitleFrom(new ChatMessage[]
        {
            new(ChatMessage.SystemRole, "you are helpful"),
            new(ChatMessage.UserRole, "Plan a trip to Kyoto"),
            new(ChatMessage.AssistantRole, "Sure"),
        });

        Assert.Equal("Plan a trip to Kyoto", title);
    }

    [Fact]
    public void LongTitlesAreTrimmedWithAnEllipsis()
    {
        var title = Chat.TitleFrom(new[] { new ChatMessage(ChatMessage.UserRole, new string('x', 200)) });

        Assert.True(title.Length <= Chat.MaxTitleLength);
        Assert.EndsWith("…", title, StringComparison.Ordinal);
    }

    [Fact]
    public void NewlinesAndRunsOfSpacesCollapse()
    {
        var title = Chat.TitleFrom(new[] { new ChatMessage(ChatMessage.UserRole, "line one\n\n   line   two") });

        Assert.DoesNotContain('\n', title);
        Assert.DoesNotContain("  ", title, StringComparison.Ordinal);
    }

    [Fact]
    public void AChatWithNoUserMessageIsUntitled()
    {
        Assert.Equal(Chat.Untitled, Chat.TitleFrom(Array.Empty<ChatMessage>()));
        Assert.Equal(Chat.Untitled, Chat.TitleFrom(new[] { new ChatMessage(ChatMessage.SystemRole, "sys") }));
    }
}
