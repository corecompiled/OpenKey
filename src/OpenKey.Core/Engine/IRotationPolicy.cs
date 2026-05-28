using OpenKey.Core.Providers;

namespace OpenKey.Core.Engine;

public interface IRotationPolicy
{
    Task<ModelInfo> PickAsync(IReadOnlyList<ModelInfo> candidates, CancellationToken ct);
    void MarkFailure(string modelId, ChatErrorKind kind, string? retryAfterHint);
    void MarkSuccess(string modelId);
    void Clear();
}
