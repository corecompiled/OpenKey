using System.Text.Json.Serialization;

namespace OpenKey.Providers.OpenRouter;

/// <summary>
/// Request bodies for the OpenRouter wire format documented in docs/03-openrouter-integration.md.
/// These are named types rather than anonymous objects so they can be source-generated: anonymous
/// types force reflection-based serialization, which is neither trim- nor AOT-safe.
/// </summary>
internal sealed record ChatCompletionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<WireMessage> Messages,
    [property: JsonPropertyName("stream")] bool Stream,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    // Omitted entirely when unset. Previously serialized as `"temperature": null` on every request.
    [property: JsonPropertyName("temperature")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? Temperature);

internal sealed record WireMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ChatCompletionRequest))]
internal sealed partial class OpenRouterJsonContext : JsonSerializerContext;
