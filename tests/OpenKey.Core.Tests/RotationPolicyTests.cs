using OpenKey.Core.Engine;
using OpenKey.Core.Providers;
using Xunit;

namespace OpenKey.Core.Tests;

public sealed class RotationPolicyTests
{
    private static readonly ModelInfo A = new("model-a", "A", 8192, IsFree: true);
    private static readonly ModelInfo B = new("model-b", "B", 8192, IsFree: true);
    private static readonly ModelInfo C = new("model-c", "C", 8192, IsFree: true);

    [Fact]
    public async Task PicksFirstWhenNoFailures()
    {
        using var paths = new TempAppPaths();
        var policy = new RotationPolicy(paths);

        var pick = await policy.PickAsync(new[] { A, B, C }, default);

        Assert.Equal(A.Id, pick.Id);
    }

    [Fact]
    public async Task SkipsFailedModelOnNextPick()
    {
        using var paths = new TempAppPaths();
        var policy = new RotationPolicy(paths);

        policy.MarkFailure(A.Id, ChatErrorKind.TransientRateLimit, null);

        var pick = await policy.PickAsync(new[] { A, B, C }, default);

        Assert.Equal(B.Id, pick.Id);
    }

    [Fact]
    public async Task AuthFailureMakesModelEffectivelyUnreachable()
    {
        using var paths = new TempAppPaths();
        var policy = new RotationPolicy(paths);

        policy.MarkFailure(A.Id, ChatErrorKind.AuthFailure, null);

        // Only A available, and A's cooldown is forever
        await Assert.ThrowsAsync<ChatException>(
            async () => await policy.PickAsync(new[] { A }, default));
    }

    [Fact]
    public async Task RotationStatePersistsAcrossInstances()
    {
        using var paths = new TempAppPaths();
        var policy1 = new RotationPolicy(paths);
        policy1.MarkFailure(A.Id, ChatErrorKind.TransientRateLimit, null);

        var policy2 = new RotationPolicy(paths);
        var pick = await policy2.PickAsync(new[] { A, B }, default);

        Assert.Equal(B.Id, pick.Id);
    }

    [Fact]
    public async Task ClearRemovesAllCooldowns()
    {
        using var paths = new TempAppPaths();
        var policy = new RotationPolicy(paths);

        policy.MarkFailure(A.Id, ChatErrorKind.TransientRateLimit, null);
        policy.MarkFailure(B.Id, ChatErrorKind.TransientRateLimit, null);
        policy.Clear();

        var pick = await policy.PickAsync(new[] { A, B }, default);

        Assert.Equal(A.Id, pick.Id);
    }
}
