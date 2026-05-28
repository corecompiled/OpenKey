# 05 — Persistence and Reset

Everything OpenKey writes to disk, where, why, and how `/reset` undoes it.

## Storage root

```
%APPDATA%\OpenKey\
```

Expands to `C:\Users\<username>\AppData\Roaming\OpenKey\`.

Resolve in code:

```csharp
public static string RootDir =>
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenKey");
```

Auto-create on first write: `Directory.CreateDirectory(RootDir);`

## File inventory

| File | Format | Purpose | Wiped by `/reset` |
|------|--------|---------|-------------------|
| `key.bin` | DPAPI-encrypted bytes | The OpenRouter API key | yes |
| `session.json` | JSON | Last conversation (resume on launch) | yes |
| `models.cache.json` | JSON | `/models` snapshot + `fetchedAt` | yes |
| `rotation.state.json` | JSON | Per-model `ModelState` table | yes |
| `config.json` | JSON | Preferences (preferred model order, theme) | yes |

No log files written in Phase 1. (Phase 1.2 may add `logs\YYYY-MM-DD.log`.)

## `key.bin` — DPAPI encryption

### Save

```csharp
public void Save(string apiKey)
{
    var plain = Encoding.UTF8.GetBytes(apiKey);
    var cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
    Directory.CreateDirectory(AppData.RootDir);
    File.WriteAllBytes(Path.Combine(AppData.RootDir, "key.bin"), cipher);
}

private static readonly byte[] Entropy =
    Encoding.UTF8.GetBytes("OpenKey/v1/key-entropy");
```

### Load

```csharp
public string? Load()
{
    var path = Path.Combine(AppData.RootDir, "key.bin");
    if (!File.Exists(path)) return null;
    try
    {
        var cipher = File.ReadAllBytes(path);
        var plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }
    catch (CryptographicException) { return null; }   // copied across user accounts or corrupted
}
```

### Properties

- **User-scoped**: `DataProtectionScope.CurrentUser` — only the same Windows user account on the same machine can decrypt. Copying `key.bin` to another machine or user makes it useless.
- **No password**: zero friction on launch.
- **Per-user provisioning**: aligned with the decision in 00-overview — if the USB exe is run by a different user, `Load()` returns null → first-run flow re-runs.
- **Entropy constant**: not a secret, just versioning. If we change the entropy in v2, old keys become un-decryptable, forcing re-entry. This is intended.

## `session.json`

```json
{
  "modelId": "meta-llama/llama-3.3-70b-instruct:free",
  "startedAt": "2026-05-27T14:00:00Z",
  "turns": [
    { "role": "system",    "content": "You are a helpful assistant.", "ts": "2026-05-27T14:00:00Z" },
    { "role": "user",      "content": "hi",                            "ts": "2026-05-27T14:00:05Z" },
    { "role": "assistant", "content": "Hello! ...",                    "ts": "2026-05-27T14:00:06Z" }
  ]
}
```

### Save policy

After every successful turn (assistant `IsFinal == true`). Atomic write:

```csharp
var tmp = path + ".tmp";
await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(snap, JsonOpts), ct);
File.Move(tmp, path, overwrite: true);
```

### Load + recap

On startup, if `session.json` exists and parses cleanly:

1. Load turns into `ChatEngine._turns`.
2. Print "[grey]Resumed session from <relative time>. Last 2 turns:[/]"
3. Render last 2 turns (truncate content to 200 chars each).
4. Continue REPL.

If parse fails: rename to `session.json.broken-<timestamp>` and start fresh (don't lose the broken file in case of bug).

## `models.cache.json`

```json
{
  "fetchedAt": "2026-05-27T13:00:00Z",
  "models": [ /* ModelInfo[] */ ]
}
```

TTL: 24 hours. If `now - fetchedAt > 24h`, `IModelCatalog.GetFreeModelsAsync` calls `RefreshAsync`.

`/reset` deletes the cache. `IModelCatalog.ClearCache()` also exposed for tests.

## `rotation.state.json`

See `04-model-rotation.md` § "Persistence of rotation state".

## `config.json`

```json
{
  "preferredModels": [],
  "theme": "default",
  "maxTokens": 2048
}
```

If absent, defaults apply (empty list ⇒ use catalog-derived order). Phase 1 doesn't expose a UI to edit this — user edits the file manually. Phase 1.2 adds `/config`.

## First-run flow (detailed)

Two acquisition paths produce the same outcome — an encrypted `key.bin`. The user picks one from a Spectre `SelectionPrompt`.

```
ConsoleHost.EnsureFirstRunAsync():
    if (_keyStore.HasKey()) return;

    PrintWelcome();

    for (int attempt = 1; attempt <= 3; attempt++)
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("How do you want to provide your OpenRouter key?")
                .AddChoices(
                    "Sign in with browser (OAuth/PKCE) — recommended",
                    "I already have a key — paste it"));

        // OAuth path also shows a hint "[p] paste, [c] cancel" and watches Console.ReadKey
        // in parallel with the listener, so the user can switch paths or abort without restart.
        string? key = choice.StartsWith("Sign in")
            ? await AcquireViaOAuthAsync(ct)        // see OpenRouterOAuth, wire in 03-openrouter-integration.md
            : AcquireViaPasteAsync();               // Secret() TextPrompt

        if (string.IsNullOrEmpty(key))
        {
            AnsiConsole.MarkupLine($"[red]✗ no key acquired[/] (attempt {attempt}/3)");
            continue;
        }

        // Validate by calling /models with this key
        var tmpProvider = new OpenRouterProvider(_http, () => key);
        try
        {
            var models = await tmpProvider.ListModelsAsync(ct);
            if (models.Count == 0)
                throw new ChatException(ChatErrorKind.MalformedResponse, "Empty model list.");
            _keyStore.Save(key);
            AnsiConsole.MarkupLine("[green]✓ key validated and saved.[/]");
            // The host calls AnsiConsole.Clear() + reprints banner + commands hint
            // right after EnsureFirstRunAsync returns, before the chat REPL opens.
            return;
        }
        catch (ChatException ex) when (ex.Kind == ChatErrorKind.AuthFailure)
        {
            AnsiConsole.MarkupLine($"[red]✗ invalid key[/] ({attempt}/3)");
        }
        catch (ChatException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ validation failed:[/] {ex.Message}");
            if (AnsiConsole.Confirm("Save anyway and try later?"))
            {
                _keyStore.Save(key); return;
            }
        }
    }
    AnsiConsole.MarkupLine("[red]Too many failed attempts. Exiting.[/]");
    Environment.Exit(1);
```

Notes:
- The PKCE wire format lives in `03-openrouter-integration.md` § "OAuth / PKCE". This doc only describes the user-facing flow + persistence.
- Secret prompt masks input on the paste path.
- We validate using a *temporary* provider that takes a literal key, not the KeyStore — because the key isn't saved yet.
- Both paths converge on the same `_keyStore.Save(key)`. Storage schema is identical, so the cross-UI contract holds regardless of acquisition path.

## `/reset` semantics

```
CommandRouter handles "/reset":
    confirmed = AnsiConsole.Confirm("Wipe all OpenKey data and start fresh?");
    if (!confirmed) return;

    // 1. drop in-memory state
    _engine.NewSessionAsync(ct);
    _rotation.Clear();

    // 2. delete the whole directory
    try { Directory.Delete(AppData.RootDir, recursive: true); }
    catch (DirectoryNotFoundException) { /* ok */ }

    AnsiConsole.MarkupLine("[green]✓ reset complete.[/]");

    // 3. re-run first-run flow in same process
    await _host.EnsureFirstRunAsync(ct);
    await _catalog.RefreshAsync(ct);
```

After `/reset`:
- API key gone → must re-enter on next prompt
- Session gone → fresh conversation
- Model cache gone → refetched
- Rotation state gone → all models reset to clean

## Resilience to corruption

For each JSON file:
- Wrap deserialize in try/catch
- On failure, rename to `<name>.broken-<unix-ts>` and proceed with defaults
- Log a Spectre yellow warning so the user knows

This means a corrupted `session.json` never blocks launch.
