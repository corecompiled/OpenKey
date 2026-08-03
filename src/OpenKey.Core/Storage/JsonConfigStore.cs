using System.Text.Json;
using OpenKey.Core.AppPaths;

namespace OpenKey.Core.Storage;

public sealed class JsonConfigStore : IConfigStore
{
    private readonly IAppPaths _paths;
    private OpenKeyConfig? _cached;

    public JsonConfigStore(IAppPaths paths) => _paths = paths;

    public OpenKeyConfig Current => _cached ??= Load();

    public OpenKeyConfig Load()
    {
        var path = _paths.ConfigFile;
        if (!File.Exists(path))
        {
            _cached = OpenKeyConfig.Default;
            return _cached;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var loaded = JsonSerializer.Deserialize(stream, OpenKeyJsonContext.Default.OpenKeyConfig);
            _cached = Normalize(loaded);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            // Preferences are not worth failing a launch over. Quarantine and carry on with
            // defaults, matching how a corrupt session is handled.
            try { File.Move(path, path + ".broken-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds()); }
            catch (Exception move) when (move is IOException or UnauthorizedAccessException) { }
            _cached = OpenKeyConfig.Default;
        }

        return _cached;
    }

    public void Save(OpenKeyConfig config)
    {
        _cached = Normalize(config);

        // Best-effort, like every store except the key: losing a preference must never take down a
        // working session.
        try
        {
            _paths.EnsureRoot();
            var path = _paths.ConfigFile;
            var tmp = path + ".tmp";
            using (var stream = File.Create(tmp))
            {
                JsonSerializer.Serialize(stream, _cached, OpenKeyJsonContext.Default.OpenKeyConfig);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// A hand-edited config is expected — docs tell users they may edit this file — so every field
    /// is treated as untrusted rather than assumed well-formed.
    /// </summary>
    private static OpenKeyConfig Normalize(OpenKeyConfig? config)
    {
        if (config is null) return OpenKeyConfig.Default;

        var models = config.PreferredModels?
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToArray() ?? Array.Empty<string>();

        var theme = string.IsNullOrWhiteSpace(config.Theme)
            ? OpenKeyConfig.DefaultTheme
            : config.Theme.Trim().ToLowerInvariant();

        var maxTokens = config.MaxTokens is > 0 and <= 200_000
            ? config.MaxTokens
            : OpenKeyConfig.DefaultMaxTokens;

        // WithUserName rather than passing it straight through: this file is hand-editable, so
        // the name gets the same trim and length cap as one typed into the app.
        return new OpenKeyConfig(models, theme, maxTokens, config.CheckForUpdates)
            .WithUserName(config.UserName);
    }
}
