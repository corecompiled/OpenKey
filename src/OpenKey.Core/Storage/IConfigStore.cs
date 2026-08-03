namespace OpenKey.Core.Storage;

/// <summary>
/// User preferences, persisted to <c>config.json</c>. Shape is normative — see
/// docs/05-persistence-and-reset.md.
/// </summary>
/// <param name="PreferredModels">
/// Model ids to try first, in order. A single entry is how a pinned model is expressed; an empty
/// list means "let rotation choose", which is the default.
/// </param>
/// <param name="Theme">Palette name: <c>default</c>, <c>dark</c>, <c>light</c>, or <c>mono</c>.</param>
/// <param name="MaxTokens">Upper bound on reply length requested from the model.</param>
/// <param name="CheckForUpdates">
/// Whether to ask GitHub once at launch if a newer release exists. Notify only — nothing is ever
/// downloaded or installed automatically. This is the only request OpenKey makes to anywhere other
/// than OpenRouter, so it is declared here and can be switched off.
/// </param>
public sealed record OpenKeyConfig(
    IReadOnlyList<string> PreferredModels,
    string Theme,
    int MaxTokens,
    bool CheckForUpdates = true)
{
    public const string DefaultTheme = "default";
    public const int DefaultMaxTokens = 2048;

    public static OpenKeyConfig Default { get; } =
        new(Array.Empty<string>(), DefaultTheme, DefaultMaxTokens, CheckForUpdates: true);

    /// <summary>
    /// The pinned model, or null when rotation is free to choose. A view over
    /// <see cref="PreferredModels"/>, not a stored field — <c>JsonIgnore</c> keeps it out of the
    /// file, whose shape is normative in docs/05-persistence-and-reset.md.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? PinnedModel => PreferredModels.Count > 0 ? PreferredModels[0] : null;

    public OpenKeyConfig WithPinnedModel(string? modelId) =>
        this with
        {
            PreferredModels = modelId is null ? Array.Empty<string>() : new[] { modelId },
        };
}

public interface IConfigStore
{
    /// <summary>Current preferences. Never null — a missing or unreadable file yields defaults.</summary>
    OpenKeyConfig Current { get; }

    OpenKeyConfig Load();

    void Save(OpenKeyConfig config);
}
