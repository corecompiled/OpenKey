namespace OpenKey.Core.Providers;

public sealed record ChatRequest(
    string Model,
    IReadOnlyList<ChatMessage> Messages,
    int? MaxTokens = null,
    double? Temperature = null);

public sealed record ChatMessage(string Role, string Content, DateTimeOffset Timestamp)
{
    public ChatMessage(string role, string content) : this(role, content, DateTimeOffset.UtcNow) { }

    public const string SystemRole = "system";
    public const string UserRole = "user";
    public const string AssistantRole = "assistant";
}

/// <param name="IsAttemptRestart">
/// Set when the engine has abandoned a failed attempt and is starting over on another model.
/// Any text yielded before this point belongs to the discarded attempt: a consumer that is
/// accumulating deltas must clear its buffer, or a mid-reply rotation renders the answer twice
/// concatenated while the persisted session stores it once. Providers never set this.
/// </param>
public sealed record ChatChunk(
    string DeltaText,
    bool IsFinal,
    string? FinishReason,
    bool IsAttemptRestart = false);

public sealed record ModelInfo(
    string Id,
    string DisplayName,
    int ContextLength,
    bool IsFree);
