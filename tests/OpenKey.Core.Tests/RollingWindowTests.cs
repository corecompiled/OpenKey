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
        Assert.True(ChatEngine.IsTransient(ChatErrorKind.MalformedResponse));
    }

    [Fact]
    public void IsTransientExcludesFatalKinds()
    {
        Assert.False(ChatEngine.IsTransient(ChatErrorKind.AuthFailure));
        Assert.False(ChatEngine.IsTransient(ChatErrorKind.QuotaExhausted));
    }

    [Fact]
    public void NetworkDownDoesNotRotate()
    {
        // With no route to the provider every model fails identically, so rotating would burn all
        // five attempts and leave every model cooling down for an outage none of them caused.
        Assert.False(ChatEngine.IsTransient(ChatErrorKind.NetworkDown));
    }

    [Fact]
    public void NetworkDownIsNotBlamedOnTheModel()
    {
        Assert.False(ChatEngine.IsModelFault(ChatErrorKind.NetworkDown));
        Assert.True(ChatEngine.IsModelFault(ChatErrorKind.TransientRateLimit));
        Assert.True(ChatEngine.IsModelFault(ChatErrorKind.TransientServer));
    }

    [Fact]
    public void InvalidRequestIsNotRetried()
    {
        // The request is the problem, not the model; an identical retry elsewhere cannot succeed.
        Assert.False(ChatEngine.IsTransient(ChatErrorKind.InvalidRequest));
    }
}
