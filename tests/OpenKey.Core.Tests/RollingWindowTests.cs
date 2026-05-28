using OpenKey.Core.Engine;
using OpenKey.Core.Providers;
using Xunit;

namespace OpenKey.Core.Tests;

public sealed class RollingWindowTests
{
    [Fact]
    public void EstimateTokensScalesWithContentLength()
    {
        var msgs = new[]
        {
            new ChatMessage(ChatMessage.SystemRole, "you are helpful"),
            new ChatMessage(ChatMessage.UserRole, new string('x', 400)),
        };

        var est = ChatEngine.EstimateTokens(msgs);

        // Rough: (4 + 15)/4 + 4 + (4 + 400)/4 + 4  ≈ 8 + 105 = 113
        Assert.InRange(est, 100, 130);
    }

    [Fact]
    public void IsTransientCoversRetryableKinds()
    {
        Assert.True(ChatEngine.IsTransient(ChatErrorKind.TransientRateLimit));
        Assert.True(ChatEngine.IsTransient(ChatErrorKind.TransientServer));
        Assert.True(ChatEngine.IsTransient(ChatErrorKind.NetworkDown));
        Assert.True(ChatEngine.IsTransient(ChatErrorKind.MalformedResponse));
    }

    [Fact]
    public void IsTransientExcludesFatalKinds()
    {
        Assert.False(ChatEngine.IsTransient(ChatErrorKind.AuthFailure));
        Assert.False(ChatEngine.IsTransient(ChatErrorKind.QuotaExhausted));
    }
}
