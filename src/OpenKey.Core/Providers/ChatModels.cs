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

public sealed record ChatChunk(string DeltaText, bool IsFinal, string? FinishReason);

public sealed record ModelInfo(
    string Id,
    string DisplayName,
    int ContextLength,
    bool IsFree);
