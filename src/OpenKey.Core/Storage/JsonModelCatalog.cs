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

        if (free.Count == 0)
        {
            // Never cache an empty list. Doing so pinned "no models" for the full 24h TTL, and
            // since every launch then found a valid-but-empty cache, the app stayed broken until
            // someone deleted %APPDATA%\OpenKey by hand.
            throw new ChatException(
                ChatErrorKind.TransientServer,
                "OpenRouter didn't return any free models. This is usually temporary.");
        }

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
        // Best-effort: the cache is a speed optimisation, so a full disk or a read-only roaming
        // profile must not take down a turn that already succeeded.
        try
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep running on the in-memory cache.
        }
    }

    // internal, not private: OpenKeyJsonContext must be able to name it.
    internal sealed record CacheEnvelope(DateTimeOffset FetchedAt, IReadOnlyList<ModelInfo> Models);
}
