using OpenKey.Core.AppPaths;

namespace OpenKey.Core.Tests;

internal sealed class TempAppPaths : IAppPaths, IDisposable
{
    public string RootDir { get; }

    public TempAppPaths()
    {
        RootDir = Path.Combine(Path.GetTempPath(), "OpenKey.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(RootDir)) Directory.Delete(RootDir, recursive: true); }
        catch { /* best-effort */ }
    }
}
