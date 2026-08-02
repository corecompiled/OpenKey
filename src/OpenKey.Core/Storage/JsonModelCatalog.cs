using System.Text.Json;
using OpenKey.Core.AppPaths;
using OpenKey.Core.Providers;

namespace OpenKey.Core.Storage;

public sealed class JsonModelCatalog : IModelCatalog
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly IAppPaths _paths;
    private readonly IChatProvider _provider;

    private CacheEnvelope? _memCache;

    public JsonModelCatalog(IAppPaths paths, IChatProvider provider)
    {
        _paths = paths;
        _provider = provider;
    }

    public async Task<IReadOnlyList<ModelInfo>> GetFreeModelsAsync(CancellationToken ct)
    {
        var cache = _memCache ?? LoadFromDisk();
        if (cache is not null && DateTimeOffset.UtcNow - cache.FetchedAt < CacheTtl)
        {
            _memCache = cache;
            return cache.Models;
        }

        await RefreshAsync(ct);
        return _memCache!.Models;
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        var models = await _provider.ListModelsAsync(ct);
        var free = models.Where(m => m.IsFree).ToList();
        var env = new CacheEnvelope(DateTimeOffset.UtcNow, free);
        _memCache = env;
        SaveToDisk(env);
    }

    public void ClearCache()
    {
        _memCache = null;
        var path = _paths.ModelsCacheFile;
        if (File.Exists(path)) File.Delete(path);
    }

    private CacheEnvelope? LoadFromDisk()
    {
        var path = _paths.ModelsCacheFile;
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, OpenKeyJsonContext.Default.CacheEnvelope);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private void SaveToDisk(CacheEnvelope env)
    {
        _paths.EnsureRoot();
        var path = _paths.ModelsCacheFile;
        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp))
        {
            JsonSerializer.Serialize(stream, env, OpenKeyJsonContext.Default.CacheEnvelope);
        }
        File.Move(tmp, path, overwrite: true);
    }

    // internal, not private: OpenKeyJsonContext must be able to name it.
    internal sealed record CacheEnvelope(DateTimeOffset FetchedAt, IReadOnlyList<ModelInfo> Models);
}
