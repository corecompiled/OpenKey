using OpenKey.Core.AppPaths;

namespace OpenKey.Windows;

/// <summary>
/// Where OpenKey keeps its files: <c>%APPDATA%\OpenKey\</c> by default.
/// <para>
/// Set <c>OPENKEY_HOME</c> to point it somewhere else. That exists so the app can be exercised
/// against a scratch directory instead of real conversations — there was previously no way to run
/// it without touching the only copy of someone's chat history, which is a bad property for a tool
/// whose own tests involve deleting chats.
/// </para>
/// <para>
/// The key stays DPAPI-encrypted for the current Windows account wherever the folder lives, so a
/// redirected home is not a way to make the key portable. See docs/05-persistence-and-reset.md.
/// </para>
/// </summary>
public sealed class WindowsAppPaths : IAppPaths
{
    public const string HomeVariable = "OPENKEY_HOME";

    public string RootDir { get; } = Resolve();

    internal static string Resolve(string? overrideValue = null)
    {
        var home = overrideValue ?? Environment.GetEnvironmentVariable(HomeVariable);

        // Whitespace is treated as unset rather than as a path: an empty variable left over from a
        // shell script should fall back to the real location, not resolve to the current directory.
        return string.IsNullOrWhiteSpace(home)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenKey")
            : Path.GetFullPath(home.Trim());
    }
}
