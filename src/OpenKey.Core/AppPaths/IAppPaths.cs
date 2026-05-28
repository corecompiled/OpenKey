namespace OpenKey.Core.AppPaths;

public interface IAppPaths
{
    string RootDir { get; }

    string KeyFile => Path.Combine(RootDir, "key.bin");
    string SessionFile => Path.Combine(RootDir, "session.json");
    string ModelsCacheFile => Path.Combine(RootDir, "models.cache.json");
    string RotationStateFile => Path.Combine(RootDir, "rotation.state.json");
    string ConfigFile => Path.Combine(RootDir, "config.json");

    void EnsureRoot() => Directory.CreateDirectory(RootDir);
}
