# Storage and crypto

Everything OpenKey keeps lives under `%APPDATA%\OpenKey\`, typically
`C:\Users\<you>\AppData\Roaming\OpenKey`. Normative shapes are in
[`../05-persistence-and-reset.md`](../05-persistence-and-reset.md).

| File | Owner | Contents |
|---|---|---|
| `key.bin` | `DpapiKeyStore` | DPAPI-encrypted OpenRouter key |
| `session.json` | `JsonSessionStore` | Conversation history |
| `models.cache.json` | `JsonModelCatalog` | Free model list, 24-hour lifetime |
| `rotation.state.json` | `RotationPolicy` | Per-model cooldowns |
| `config.json` | *nobody yet* | Declared in `IAppPaths`, never read or written |

`config.json` is the reason a pinned model lasts only until you close the app: there is nowhere to
put a preference. It is on the backlog and blocks `/theme`.

## The key

`ProtectedData.Protect` with `DataProtectionScope.CurrentUser` and a fixed entropy string. The
result is decryptable only by the same Windows account on the same machine — copy `key.bin`
elsewhere and it is inert.

What DPAPI does *not* protect against is another process running as the same user; it can call
`Unprotect` exactly as OpenKey does. That is the accepted boundary for a single-user desktop app,
and it is stated in [`../../SECURITY.md`](../../SECURITY.md) rather than left implied.

Save failures here are **not** swallowed, unlike every other store. A key that silently fails to
persist means signing in again on every launch with no explanation, so `Save` raises a
`ChatException` naming the directory and the likely cause.

## Writes are atomic and best-effort

Every JSON write goes to `<file>.tmp` and is then `File.Move`d over the target, so a crash mid-write
cannot leave a half-written file where a valid one was.

Every write except the key is wrapped in a guard for `IOException` and `UnauthorizedAccessException`:

```csharp
try { /* write */ }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
```

This is not laziness. `SaveAsync` runs immediately after a reply has been generated but *before* it
is shown. An unguarded throw on a full disk or a read-only roaming profile loses the user a reply
they already waited for. Losing history is the lesser failure, and the reply still reaches the
screen.

## Corruption heals itself

`JsonSessionStore.LoadAsync` catches `JsonException` and `IOException`, renames the file to
`session.json.broken-<unix>`, and returns null. OpenKey starts with an empty conversation instead of
refusing to start, and the bad file is preserved for diagnosis rather than deleted.

## Serialization is source-generated

`OpenKeyJsonContext` covers `SessionSnapshot`, the model cache envelope, and the rotation envelope;
`OpenRouterJsonContext` and `OAuthJsonContext` cover request bodies. Reflection-based
serialization is neither trim- nor AOT-safe, and these shapes are an on-disk contract that changes
rarely and deliberately — exactly what source generation is for.

Anonymous request bodies had to become named records to make this work, which also removed a
`"temperature": null` that was being sent on every request.

### One trap worth knowing

`ChatMessage` carries `[JsonConstructor]`. It has a convenience constructor alongside the full one,
and `System.Text.Json` refuses to guess between two constructors — it throws
`NotSupportedException`, which is *not* a `JsonException` and therefore was not caught by the
corruption handler above.

The effect: restoring a saved conversation never worked in any build, and the failure was silent.
If you add a second constructor to a persisted type, annotate it.

## `/reset`

Clears in-memory state, clears each store, deletes the directory, then re-runs first-run setup.

If setup is then abandoned there is no key, and returning to the prompt would strand the user:
every message fails auth, the error suggests `/reset`, and `/reset` arrives back at the same place.
So OpenKey exits instead, saying why.
