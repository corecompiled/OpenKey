using System.Text.Json.Serialization;
using OpenKey.Core.Engine;

namespace OpenKey.Core.Storage;

/// <summary>
/// Source-generated serialization for everything OpenKey persists under <c>%APPDATA%\OpenKey\</c>.
/// Reflection-based serialization is neither trim- nor AOT-safe, and the shapes here are the
/// on-disk contract described in docs/05-persistence-and-reset.md — they change rarely and
/// deliberately, which is exactly the case source generation is for.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SessionSnapshot))]
[JsonSerializable(typeof(OpenKeyConfig))]
[JsonSerializable(typeof(JsonModelCatalog.CacheEnvelope))]
[JsonSerializable(typeof(RotationPolicy.StateEnvelope))]
internal sealed partial class OpenKeyJsonContext : JsonSerializerContext;
