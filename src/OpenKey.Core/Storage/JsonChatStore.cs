using System.Text.Json;
using OpenKey.Core.AppPaths;
using OpenKey.Core.Providers;

namespace OpenKey.Core.Storage;

/// <summary>
/// One JSON file per conversation under <c>chats\</c>, with <c>index.json</c> as a list cache.
/// <para>
/// A folder of files rather than one large document, so a corrupt write costs a single
/// conversation instead of all of them — the same reasoning behind quarantining a bad session
/// rather than refusing to start.
/// </para>
/// <para>
/// <c>index.json</c> is a <b>cache, never the source of truth</b>. If it is missing, stale or
/// unreadable it is rebuilt by reading the chat files. That keeps a fast sidebar without creating
/// a second thing that can disagree with reality.
/// </para>
/// </summary>
public sealed class JsonChatStore : IChatStore
{
    private readonly IAppPaths _paths;
    private bool _migrated;

    public JsonChatStore(IAppPaths paths) => _paths = paths;

    private string Dir => Path.Combine(_paths.RootDir, "chats");

    private string IndexFile => Path.Combine(Dir, "index.json");

    private string FileFor(string id) => Path.Combine(Dir, id + ".json");

    public async Task<IReadOnlyList<ChatSummary>> ListAsync(CancellationToken ct)
    {
        await MigrateIfNeededAsync(ct);

        var index = ReadIndex();
        if (index is not null) return index;

        var rebuilt = await RebuildIndexAsync(ct);
        WriteIndex(rebuilt);
        return rebuilt;
    }

    public async Task<Chat?> LoadAsync(string id, CancellationToken ct)
    {
        await MigrateIfNeededAsync(ct);

        var path = FileFor(id);
        if (!File.Exists(path)) return null;

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(stream, OpenKeyJsonContext.Default.Chat, ct);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            Quarantine(path);
            return null;
        }
    }

    public async Task SaveAsync(Chat chat, CancellationToken ct)
    {
        // Best-effort, like every store but the key: this runs right after a reply is generated
        // and before it is shown, so a full disk must not cost the user their answer.
        try
        {
            Directory.CreateDirectory(Dir);

            var path = FileFor(chat.Id);
            var tmp = path + ".tmp";
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, chat, OpenKeyJsonContext.Default.Chat, ct);
            }
            File.Move(tmp, path, overwrite: true);

            // Index follows the files; if this write fails the next List rebuilds it.
            var summaries = (await RebuildIndexAsync(ct));
            WriteIndex(summaries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        try
        {
            var path = FileFor(id);
            if (File.Exists(path)) File.Delete(path);
            WriteIndex(await RebuildIndexAsync(ct));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public async Task<string?> MostRecentIdAsync(CancellationToken ct)
    {
        var all = await ListAsync(ct);
        return all.Count > 0 ? all[0].Id : null;   // ListAsync is newest-first
    }

    public void Clear()
    {
        try
        {
            if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ---- migration ---------------------------------------------------------------------

    /// <summary>
    /// Turns a pre-history <c>session.json</c> into the first chat.
    /// <para>
    /// The original is renamed rather than deleted, so a failure here can never be the reason
    /// someone loses the only conversation they had.
    /// </para>
    /// </summary>
    private async Task MigrateIfNeededAsync(CancellationToken ct)
    {
        if (_migrated) return;
        _migrated = true;

        try
        {
            var legacy = _paths.SessionFile;
            if (!File.Exists(legacy) || Directory.Exists(Dir)) return;

            SessionSnapshot? snap;
            await using (var stream = File.OpenRead(legacy))
            {
                snap = await JsonSerializer.DeserializeAsync(
                    stream, OpenKeyJsonContext.Default.SessionSnapshot, ct);
            }

            if (snap?.Turns is { Count: > 0 })
            {
                var chat = new Chat(
                    IChatStore.NewId(),
                    Chat.TitleFrom(snap.Turns),
                    snap.ModelId,
                    snap.StartedAt,
                    snap.StartedAt,
                    snap.Turns);

                Directory.CreateDirectory(Dir);
                await SaveAsync(chat, ct);
            }
            else
            {
                Directory.CreateDirectory(Dir);
            }

            File.Move(legacy, legacy + ".migrated", overwrite: true);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // A conversation that cannot be migrated is left exactly where it is.
        }
    }

    // ---- index -------------------------------------------------------------------------

    private IReadOnlyList<ChatSummary>? ReadIndex()
    {
        if (!File.Exists(IndexFile)) return null;

        try
        {
            using var stream = File.OpenRead(IndexFile);
            var envelope = JsonSerializer.Deserialize(stream, OpenKeyJsonContext.Default.ChatIndex);
            if (envelope?.Chats is null) return null;

            // A cache that disagrees with the files is worse than no cache. Compare the actual
            // ids, not just how many there are — an index with the right *number* of wrong
            // entries would otherwise be trusted.
            if (!Directory.Exists(Dir)) return null;

            var onDisk = Directory.GetFiles(Dir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.Equals(name, "index", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.Ordinal);

            if (envelope.Chats.Count != onDisk.Count) return null;
            foreach (var summary in envelope.Chats)
            {
                if (!onDisk.Contains(summary.Id)) return null;
            }

            return envelope.Chats;
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<ChatSummary>> RebuildIndexAsync(CancellationToken ct)
    {
        if (!Directory.Exists(Dir)) return Array.Empty<ChatSummary>();

        var summaries = new List<ChatSummary>();

        foreach (var path in Directory.GetFiles(Dir, "*.json"))
        {
            if (string.Equals(Path.GetFileName(path), "index.json", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                await using var stream = File.OpenRead(path);
                var chat = await JsonSerializer.DeserializeAsync(stream, OpenKeyJsonContext.Default.Chat, ct);
                if (chat is null) continue;

                summaries.Add(new ChatSummary(
                    chat.Id,
                    string.IsNullOrWhiteSpace(chat.Title) ? Chat.Untitled : chat.Title,
                    chat.UpdatedAt,
                    chat.Turns.Count(t => t.Role != ChatMessage.SystemRole)));
            }
            catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
            {
                Quarantine(path);
            }
        }

        summaries.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));
        return summaries;
    }

    private void WriteIndex(IReadOnlyList<ChatSummary> summaries)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = IndexFile + ".tmp";
            using (var stream = File.Create(tmp))
            {
                JsonSerializer.Serialize(stream, new ChatIndex(summaries), OpenKeyJsonContext.Default.ChatIndex);
            }
            File.Move(tmp, IndexFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Quarantine(string path)
    {
        try { File.Move(path, path + ".broken-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>Cache of the chat list. Rebuildable from the chat files; never authoritative.</summary>
public sealed record ChatIndex(IReadOnlyList<ChatSummary> Chats);
