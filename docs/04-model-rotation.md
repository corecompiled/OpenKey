# 04 — Model Rotation

How OpenKey picks a free model, when it gives up, and how it recovers.

## Policy summary

> Try the preferred model. On a transient failure, mark it on cooldown and try the next preferred model. Never retry an auth failure. Persist cooldown state across runs.

## State per model

```csharp
public sealed class ModelState
{
    public string ModelId { get; init; } = "";
    public int FailureCount { get; set; }
    public DateTimeOffset? CooldownUntil { get; set; }
    public ChatErrorKind? LastErrorKind { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
}
```

Persisted to `%APPDATA%\OpenKey\rotation.state.json` after every update.

## Preferred order

Default ordering algorithm (run once per session, after catalog refresh):

1. Take all `IsFree == true` models from catalog.
2. Sort by `ContextLength` descending.
3. Cap to top 8.
4. Result = preferred order list.

Optionally overridable via `%APPDATA%\OpenKey\config.json`:

```json
{
  "preferredModels": [
    "meta-llama/llama-3.3-70b-instruct:free",
    "google/gemini-2.0-flash-exp:free",
    "qwen/qwen-2.5-72b-instruct:free"
  ]
}
```

If `preferredModels` set, use it verbatim (intersected with currently-available free list).

## `PickAsync` algorithm

```
1. let now = DateTimeOffset.UtcNow
2. for each modelId in preferredOrder:
     state = states.GetOrAdd(modelId)
     if state.CooldownUntil == null || state.CooldownUntil <= now:
         return the corresponding ModelInfo
3. // all on cooldown — return the one with the soonest CooldownUntil
4. if soonest still in the future:
     sleep until that time (cap 30s; if more, surface error to user)
5. return that model
```

## Cooldown durations

Base cooldowns by `ChatErrorKind`:

| Kind                  | Base cooldown | Notes |
|-----------------------|---------------|-------|
| `TransientRateLimit`  | 60 s          | If `Retry-After` header present, use `max(header, 60s)`. |
| `TransientServer`     | 30 s          | |
| `NetworkDown`         | 15 s          | Not really model-specific, but cheap to apply. |
| `QuotaExhausted`      | 1 h           | OpenRouter daily-quota signals. |
| `MalformedResponse`   | 10 s          | Usually transient; aggressive retry. |
| `AuthFailure`         | never retry   | Mark `CooldownUntil = DateTimeOffset.MaxValue`, surface to user. |

### Exponential backoff

For repeated failures (`FailureCount` increments), multiply base by `2^FailureCount`, capped at **5 minutes**. The cap dominates quickly: a nominal 1 hour cooldown is clamped to 5 minutes like everything else.

```csharp
var multiplier = Math.Min(1 << Math.Min(state.FailureCount, 8), 64);
var cooldown = TimeSpan.FromSeconds(baseSeconds * multiplier);
if (cooldown > TimeSpan.FromMinutes(5)) cooldown = TimeSpan.FromMinutes(5);
state.CooldownUntil = DateTimeOffset.UtcNow + cooldown;
```

### Recovery

On `MarkSuccess`, reset `FailureCount = 0`, clear `CooldownUntil`, set `LastErrorKind = null`.

## Retry loop in `ChatEngine.SendAsync`

```csharp
const int MaxAttempts = 5;
for (int attempt = 1; attempt <= MaxAttempts; attempt++)
{
    var candidates = await _catalog.GetFreeModelsAsync(ct);
    var model = await _rotation.PickAsync(candidates, ct);
    ActiveModel = model;

    // NOTE: Core never writes to the console. Rotation is reported by the host, once, after the
    // fact — see "User feedback" below.

    try
    {
        await foreach (var chunk in _provider.StreamChatAsync(
            new ChatRequest(model.Id, BuildMessages()), ct))
        {
            yield return chunk;
            if (chunk.IsFinal) { _rotation.MarkSuccess(model.Id); yield break; }
        }
    }
    catch (ChatException ex) when (IsTransient(ex.Kind))
    {
        _rotation.MarkFailure(model.Id, ex.Kind, ex.RetryAfterHint);
        OnRotation?.Invoke($"{model.Id} → {ex.Kind}");   // host counts these; it does not print them
        continue;
    }
    catch (ChatException ex)
    {
        _rotation.MarkFailure(model.Id, ex.Kind, ex.RetryAfterHint);
        throw;   // fatal — surface to user
    }
}
throw new ChatException(ChatErrorKind.TransientServer, "All models failed after retries.");
```

Where:
```csharp
static bool IsTransient(ChatErrorKind k) =>
    k is ChatErrorKind.TransientRateLimit
       or ChatErrorKind.TransientServer
       or ChatErrorKind.NetworkDown
       or ChatErrorKind.MalformedResponse;
```

## Mid-stream failure handling

If `StreamChatAsync` yields some chunks then throws:

1. Do **not** yield the partial assistant content as a final turn.
2. Emit a `ChatChunk` with `IsAttemptRestart` set **before** any text from the next attempt. Core
   does not touch the console; the consumer clears what it has drawn. Without this signal a
   mid-reply rotation renders the answer twice concatenated while the saved session stores it once.
3. Re-enter the retry loop with the *same* user-turn history (assistant turn not yet appended).
4. The next model gets a fresh start with the same prompt.

This means assistant turns are only appended to `_turns` after `IsFinal == true`.

## Rolling-window trim

Before each attempt, trim `_turns` so that the **estimated** token count fits within `model.ContextLength - reservedForResponse`.

Phase 1 estimation (no tokenizer dep): `tokens ≈ totalChars / 4`. Reserve 1024 tokens for response.

```csharp
List<ChatMessage> BuildMessages()
{
    var max = model.ContextLength - 1024;
    var trimmed = new List<ChatMessage>(_turns);

    // Always keep system message (turns[0] if role == "system") and last user turn.
    while (EstimateTokens(trimmed) > max && trimmed.Count > 2)
    {
        // Remove second message (first non-system) iteratively.
        int dropIdx = trimmed[0].Role == "system" ? 1 : 0;
        trimmed.RemoveAt(dropIdx);
    }
    return trimmed;
}

static int EstimateTokens(IEnumerable<ChatMessage> msgs) =>
    msgs.Sum(m => (m.Role.Length + m.Content.Length) / 4 + 4);   // +4 framing overhead
```

Phase 1.2 can swap in a real tokenizer (`Tiktoken`-equivalent) without changing this interface.

## User feedback

Rotation emits **no output of its own**. `ChatEngine` raises `OnRotation` and the host decides what,
if anything, to show.

The console shows a single grey line above the reply, and only when rotation actually occurred:

```
Moved past 2 busy models.
```

Three rules, all learned the hard way:

- **Not a warning.** Rotation working correctly is the feature doing its job. Yellow per-attempt
  lines made a healthy app look broken.
- **Not during the reply.** Writing mid-stream corrupts the spinner and the streamed text, because
  both own the cursor.
- **No model ids and no `ChatErrorKind` names.** The model that answered is already in the reply
  header; the ones that didn't are not the user's problem.

Failures that stop the turn are rendered as error cards by the host — see
[`architecture/07-error-taxonomy.md`](architecture/07-error-taxonomy.md) for the copy per kind.

## Persistence of rotation state

`rotation.state.json`:

```json
{
  "states": {
    "meta-llama/llama-3.3-70b-instruct:free": {
      "modelId": "meta-llama/llama-3.3-70b-instruct:free",
      "failureCount": 2,
      "cooldownUntil": "2026-05-27T14:32:18Z",
      "lastErrorKind": "TransientRateLimit",
      "lastUsedAt": "2026-05-27T14:30:00Z"
    }
  }
}
```

Loaded on startup, saved after every `MarkFailure`/`MarkSuccess`. Cooldowns persist across exits — if you hit a rate limit and close the app, the cooldown still applies next launch.

`/reset` clears this file along with everything else.
