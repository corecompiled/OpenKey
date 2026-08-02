using System.Text.Json;
using OpenKey.Core.AppPaths;

namespace OpenKey.Core.Storage;

public sealed class JsonSessionStore : ISessionStore
{
    private readonly IAppPaths _paths;

    public JsonSessionStore(IAppPaths paths) => _paths = paths;

    public async Task<SessionSnapshot?> LoadAsync(CancellationToken ct)
    {
        var path = _paths.SessionFile;
        if (!File.Exists(path)) return null;

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(stream, OpenKeyJsonContext.Default.SessionSnapshot, ct);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            var broken = path + ".broken-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            try { File.Move(path, broken); } catch { /* best-effort */ }
            return null;
        }
    }

    public async Task SaveAsync(SessionSnapshot snap, CancellationToken ct)
    {
        _paths.EnsureRoot();
        var path = _paths.SessionFile;
        var tmp = path + ".tmp";

        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, snap, OpenKeyJsonContext.Default.SessionSnapshot, ct);
        }
        File.Move(tmp, path, overwrite: true);
    }

    public void Clear()
    {
        var path = _paths.SessionFile;
        if (File.Exists(path)) File.Delete(path);
    }
}
