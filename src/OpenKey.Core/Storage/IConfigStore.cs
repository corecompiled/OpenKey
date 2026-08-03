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
/// <param name="UserName">
/// What to call the person using OpenKey, shown as the label on their own messages. Null or blank
/// means "use the Windows account name", which is the default and needs no prompt.
/// <para>
/// <b>Display only.</b> It is never placed in a <c>ChatMessage</c>, never sent to a provider, and
/// never written into an export — exports say "You", so a shared transcript does not carry a name
/// the author did not choose to put in it.
/// </para>
/// </param>
/// <param name="CheckForUpdates">
/// Whether to ask GitHub once at launch if a newer release exists. Notify only — nothing is ever
/// downloaded or installed automatically. This is the only request OpenKey makes to anywhere other
/// than OpenRouter, so it is declared here and can be switched off.
/// </param>
public sealed record OpenKeyConfig(
    IReadOnlyList<string> PreferredModels,
    string Theme,
    int MaxTokens,
    bool CheckForUpdates = true,
    string? UserName = null)
{
    public const string DefaultTheme = "default";
    public const int DefaultMaxTokens = 2048;

    public static OpenKeyConfig Default { get; } =
        new(Array.Empty<string>(), DefaultTheme, DefaultMaxTokens, CheckForUpdates: true);

    /// <summary>
    /// The name to show on the user's own messages: their chosen name if they set one, otherwise
    /// the Windows account name. Resolved here rather than in each host so the console and the GUI
    /// cannot drift apart on it.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(UserName) ? Environment.UserName : UserName.Trim();

    /// <summary>
    /// Longest stored name. A name is a label on a transcript line, not a field anyone queries;
    /// past this the console prompt starts eating the input line.
    /// </summary>
    public const int MaxUserNameLength = 32;

    /// <summary>
    /// Blank clears the override and returns to the Windows account name. Trims and caps, so the
    /// same rules apply whether the name arrives from a dialog, from <c>/name</c>, or from someone
    /// hand-editing config.json.
    /// </summary>
    public OpenKeyConfig WithUserName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return this with { UserName = null };

        var trimmed = name.Trim();
        if (trimmed.Length > MaxUserNameLength) trimmed = trimmed[..MaxUserNameLength].TrimEnd();

        return this with { UserName = trimmed };
    }

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
