using OpenKey.Core.AppPaths;

namespace OpenKey;

public sealed class WindowsAppPaths : IAppPaths
{
    public string RootDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenKey");
}
