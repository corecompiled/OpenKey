using OpenKey.Core.Providers;

namespace OpenKey.Gui.ViewModels;

/// <summary>
/// An entry in the model picker.
/// <para>
/// A wrapper rather than binding <see cref="ModelInfo"/> directly, because the list needs one item
/// that is <em>not</em> a model: "Automatic". Without it, picking a model was a one-way door —
/// nothing in the window could hand the choice back to rotation, which is the default and the
/// right setting for most people.
/// </para>
/// </summary>
public sealed record ModelChoice(string Label, string? Detail, ModelInfo? Model)
{
    public bool IsAutomatic => Model is null;

    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    public static ModelChoice Automatic { get; } =
        new("Automatic", "Best available, switches when one is busy", null);

    public static ModelChoice For(ModelInfo model) =>
        new(model.DisplayName, FormatContext(model.ContextLength), model);

    private static string FormatContext(int contextLength) =>
        contextLength <= 0 ? string.Empty
        : contextLength >= 1000 ? $"{contextLength / 1000}k context"
        : $"{contextLength} context";
}
