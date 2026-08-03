using OpenKey.Core.Providers;
using OpenKey.Gui.ViewModels;
using Xunit;

namespace OpenKey.Gui.Tests;

public sealed class ModelChoiceTests
{
    [Fact]
    public void AutomaticCarriesNoModel()
    {
        // The picker needs one entry that is not a model. Without it, choosing a model was a
        // one-way door — nothing in the window could hand the choice back to rotation.
        Assert.True(ModelChoice.Automatic.IsAutomatic);
        Assert.Null(ModelChoice.Automatic.Model);
        Assert.False(string.IsNullOrWhiteSpace(ModelChoice.Automatic.Label));
    }

    [Fact]
    public void AModelChoiceShowsItsNameNotItsId()
    {
        var choice = ModelChoice.For(new ModelInfo("vendor/some-model:free", "Some Model", 32768, true));

        Assert.Equal("Some Model", choice.Label);
        Assert.False(choice.IsAutomatic);
        Assert.Equal("vendor/some-model:free", choice.Model!.Id);
    }

    [Theory]
    [InlineData(32768, "32k context")]
    [InlineData(1000, "1k context")]
    [InlineData(512, "512 context")]
    public void ContextIsAbbreviatedForReadability(int length, string expected)
    {
        Assert.Equal(expected, ModelChoice.For(new ModelInfo("m", "M", length, true)).Detail);
    }

    [Fact]
    public void UnknownContextShowsNothingRatherThanZero()
    {
        var choice = ModelChoice.For(new ModelInfo("m", "M", 0, true));

        Assert.Equal(string.Empty, choice.Detail);
        Assert.False(choice.HasDetail);
    }
}
