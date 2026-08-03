using OpenKey.Windows;
using Xunit;

namespace OpenKey.Tests;

/// <summary>
/// The override exists so the app can be run against a scratch directory. If it silently fell back
/// to the real location, a test run would operate on someone's actual conversations — which is
/// exactly the accident it was added to prevent.
/// </summary>
public sealed class AppPathsTests
{
    [Fact]
    public void UnsetFallsBackToAppData()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenKey");

        Assert.Equal(expected, WindowsAppPaths.Resolve(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankIsTreatedAsUnsetRatherThanAsAPath(string value)
    {
        // A leftover empty variable must not resolve to the current directory.
        Assert.Equal(WindowsAppPaths.Resolve(null), WindowsAppPaths.Resolve(value));
    }

    [Fact]
    public void AnOverrideIsUsedAndMadeAbsolute()
    {
        var target = Path.Combine(Path.GetTempPath(), "openkey-scratch");

        Assert.Equal(Path.GetFullPath(target), WindowsAppPaths.Resolve(target));
    }

    [Fact]
    public void SurroundingWhitespaceDoesNotBecomePartOfThePath()
    {
        var target = Path.Combine(Path.GetTempPath(), "openkey-scratch");

        Assert.Equal(Path.GetFullPath(target), WindowsAppPaths.Resolve($"  {target}  "));
    }
}
