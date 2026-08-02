using OpenKey.Core.AppPaths;
using OpenKey.Core.Engine;
using OpenKey.Core.Storage;
using Xunit;

namespace OpenKey.Core.Tests;

public sealed class ConfigStoreTests
{
    [Fact]
    public void MissingFileYieldsDefaults()
    {
        using var paths = new TempAppPaths();
        var config = new JsonConfigStore(paths).Load();

        Assert.Equal(OpenKeyConfig.DefaultTheme, config.Theme);
        Assert.Equal(OpenKeyConfig.DefaultMaxTokens, config.MaxTokens);
        Assert.Null(config.PinnedModel);
    }

    [Fact]
    public void RoundTripsThroughDisk()
    {
        using var paths = new TempAppPaths();
        new JsonConfigStore(paths).Save(
            OpenKeyConfig.Default.WithPinnedModel("vendor/model:free") with { Theme = "light" });

        var reread = new JsonConfigStore(paths).Load();

        Assert.Equal("vendor/model:free", reread.PinnedModel);
        Assert.Equal("light", reread.Theme);
    }

    [Fact]
    public void PinningNullClearsThePreference()
    {
        var pinned = OpenKeyConfig.Default.WithPinnedModel("a");
        Assert.Equal("a", pinned.PinnedModel);
        Assert.Null(pinned.WithPinnedModel(null).PinnedModel);
    }

    [Fact]
    public void HandEditedGarbageIsNormalisedRatherThanTrusted()
    {
        // Users are told they may edit this file, so every field is treated as untrusted.
        using var paths = new TempAppPaths();
        File.WriteAllText(((IAppPaths)paths).ConfigFile,
            """{"preferredModels":["good","","   "],"theme":"  LIGHT ","maxTokens":-5}""");

        var config = new JsonConfigStore(paths).Load();

        Assert.Equal(new[] { "good" }, config.PreferredModels);
        Assert.Equal("light", config.Theme);
        Assert.Equal(OpenKeyConfig.DefaultMaxTokens, config.MaxTokens);
    }

    [Fact]
    public void CorruptFileIsQuarantinedAndDefaultsApply()
    {
        using var paths = new TempAppPaths();
        var file = ((IAppPaths)paths).ConfigFile;
        File.WriteAllText(file, "not json at all");

        var config = new JsonConfigStore(paths).Load();

        Assert.Equal(OpenKeyConfig.DefaultTheme, config.Theme);
        Assert.False(File.Exists(file));
        Assert.NotEmpty(Directory.GetFiles(paths.RootDir, "config.json.broken-*"));
    }

    [Fact]
    public async Task PinningAModelSurvivesARestart()
    {
        // The whole point of the config store: a pin used to last only until the app closed.
        using var paths = new TempAppPaths();
        var provider = new FakeChatProvider { Models = new[] { new Providers.ModelInfo("m", "M", 8000, true) } };

        var first = new ChatEngine(
            provider, new RotationPolicy(paths), new JsonModelCatalog(paths, provider),
            new JsonSessionStore(paths), new JsonConfigStore(paths));
        first.PreferredModelId = "vendor/pinned:free";

        var second = new ChatEngine(
            provider, new RotationPolicy(paths), new JsonModelCatalog(paths, provider),
            new JsonSessionStore(paths), new JsonConfigStore(paths));
        await Task.CompletedTask;

        Assert.Equal("vendor/pinned:free", second.PreferredModelId);
    }
}

public sealed class TokenCounterTests
{
    [Fact]
    public void HeuristicScalesWithLength()
    {
        var counter = new HeuristicTokenCounter();
        Assert.True(counter.Count(new string('x', 400)) > counter.Count(new string('x', 40)));
    }

    [Fact]
    public void EmptyTextCostsNothing()
    {
        Assert.Equal(0, new HeuristicTokenCounter().Count(string.Empty));
    }

    [Fact]
    public void ConversationCountIncludesPerMessageOverhead()
    {
        ITokenCounter counter = new HeuristicTokenCounter();
        var messages = new[]
        {
            new Providers.ChatMessage(Providers.ChatMessage.UserRole, "hi"),
            new Providers.ChatMessage(Providers.ChatMessage.AssistantRole, "hello"),
        };

        // Strictly more than the raw text alone: role framing is not free.
        Assert.True(counter.Count(messages) > counter.Count("hi") + counter.Count("hello"));
    }
}
