using OpenKey.Core.Providers;

namespace OpenKey.Core.Storage;

public interface IModelCatalog
{
    Task<IReadOnlyList<ModelInfo>> GetFreeModelsAsync(CancellationToken ct);
    Task RefreshAsync(CancellationToken ct);
    void ClearCache();
}
