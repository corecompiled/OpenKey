using OpenKey.Core.Providers;

namespace OpenKey.Core.Storage;

public sealed record SessionSnapshot(
    string ModelId,
    DateTimeOffset StartedAt,
    IReadOnlyList<ChatMessage> Turns);

public interface ISessionStore
{
    Task<SessionSnapshot?> LoadAsync(CancellationToken ct);
    Task SaveAsync(SessionSnapshot snap, CancellationToken ct);
    void Clear();
}
