using System.Text.Json.Serialization;

namespace OpenKey.OAuth;

/// <summary>
/// Body for <c>POST /api/v1/auth/keys</c>, the PKCE code-for-key exchange documented in
/// docs/03-openrouter-integration.md. Named rather than anonymous so it can be source-generated.
/// </summary>
internal sealed record KeyExchangeRequest(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("code_verifier")] string CodeVerifier,
    [property: JsonPropertyName("code_challenge_method")] string CodeChallengeMethod);

[JsonSerializable(typeof(KeyExchangeRequest))]
internal sealed partial class OAuthJsonContext : JsonSerializerContext;
