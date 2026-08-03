using System.Text.Json.Serialization.Metadata;

namespace OpenKey.Core.Storage;

/// <summary>
/// Exposes the generated type metadata to tests. The context itself stays internal because nothing
/// outside Core should be serialising these shapes, but tests need to write a fixture file in the
/// exact format the store will read.
/// </summary>
public static class OpenKeyJsonContextAccessor
{
    public static JsonTypeInfo<SessionSnapshot> Session => OpenKeyJsonContext.Default.SessionSnapshot;

    public static JsonTypeInfo<Chat> Chat => OpenKeyJsonContext.Default.Chat;
}
