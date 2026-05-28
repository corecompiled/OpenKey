namespace OpenKey.Core.Providers;

public interface IChatProvider
{
    string Id { get; }
    string DisplayName { get; }

    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct);

    IAsyncEnumerable<ChatChunk> StreamChatAsync(ChatRequest request, CancellationToken ct);
}
