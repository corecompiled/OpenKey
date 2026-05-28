# 03 — OpenRouter Integration

Wire-level details for `OpenRouterProvider : IChatProvider`. Authoritative external reference: <https://openrouter.ai/docs>.

## Base URL

`https://openrouter.ai/api/v1`

## Required headers

| Header | Value | Notes |
|--------|-------|-------|
| `Authorization` | `Bearer <api-key>` | Loaded from `IKeyStore` |
| `Content-Type` | `application/json` | POSTs only |
| `HTTP-Referer` | `https://github.com/<user>/openkey` or `https://openkey.local` | OpenRouter uses this for app analytics; can be any URL we own/control. Use a fixed app constant. |
| `X-Title` | `OpenKey` | Shown in OpenRouter dashboard |

Define once in `OpenRouterProvider`:

```csharp
private static class H
{
    public const string Referer = "https://openkey.local";
    public const string Title = "OpenKey";
}
```

## Endpoint 1 — `GET /models`

Returns the full model catalog. Pricing fields identify free tier.

Response shape (relevant fields only):

```json
{
  "data": [
    {
      "id": "meta-llama/llama-3.3-70b-instruct:free",
      "name": "Llama 3.3 70B Instruct (free)",
      "context_length": 131072,
      "pricing": {
        "prompt": "0",
        "completion": "0",
        "request": "0",
        "image": "0"
      }
    },
    ...
  ]
}
```

**Free-tier filter:**

```csharp
bool IsFree(JsonElement model) =>
    model.GetProperty("pricing").GetProperty("prompt").GetString() == "0" &&
    model.GetProperty("pricing").GetProperty("completion").GetString() == "0";
```

Also commonly the `id` ends in `:free` — use *both* signals; `:free` suffix alone is sufficient when present.

Map to `ModelInfo`:

```csharp
new ModelInfo(
    Id: model.GetProperty("id").GetString()!,
    DisplayName: model.GetProperty("name").GetString() ?? model.GetProperty("id").GetString()!,
    ContextLength: model.GetProperty("context_length").GetInt32(),
    IsFree: true);
```

## Endpoint 2 — `POST /chat/completions` (streaming)

Request body:

```json
{
  "model": "meta-llama/llama-3.3-70b-instruct:free",
  "messages": [
    {"role": "system", "content": "You are a helpful assistant."},
    {"role": "user", "content": "hello"}
  ],
  "stream": true,
  "max_tokens": 2048,
  "temperature": 0.7
}
```

Set `stream: true`. Set `max_tokens` defensively (default 2048; user can override later). Omit `temperature` to use model default.

### Reading the SSE stream

```csharp
using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat/completions")
{
    Content = JsonContent.Create(body)
};
req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
req.Headers.Add("HTTP-Referer", H.Referer);
req.Headers.Add("X-Title", H.Title);

using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
ThrowIfHttpError(resp);    // see "Error mapping" below

using var stream = await resp.Content.ReadAsStreamAsync(ct);
using var reader = new StreamReader(stream);

string? line;
while ((line = await reader.ReadLineAsync(ct)) != null)
{
    if (line.Length == 0) continue;                 // event separator
    if (line.StartsWith(":")) continue;             // SSE comment / keepalive
    if (!line.StartsWith("data: ")) continue;
    var payload = line.Substring("data: ".Length);
    if (payload == "[DONE]")
    {
        yield return new ChatChunk("", IsFinal: true, FinishReason: null);
        yield break;
    }

    using var doc = JsonDocument.Parse(payload);
    var choice = doc.RootElement.GetProperty("choices")[0];
    var delta = choice.GetProperty("delta");
    var text = delta.TryGetProperty("content", out var c) ? c.GetString() : null;
    var finish = choice.TryGetProperty("finish_reason", out var f) && f.ValueKind != JsonValueKind.Null
                 ? f.GetString() : null;
    if (!string.IsNullOrEmpty(text))
        yield return new ChatChunk(text!, IsFinal: false, FinishReason: null);
    if (finish != null)
        yield return new ChatChunk("", IsFinal: true, FinishReason: finish);
}
```

### Chunk shape

Each `data:` line is a JSON object:

```json
{
  "id": "gen-...",
  "model": "meta-llama/llama-3.3-70b-instruct:free",
  "choices": [
    {
      "index": 0,
      "delta": { "content": " world" },
      "finish_reason": null
    }
  ]
}
```

First chunk for a stream usually has `delta.role = "assistant"` and no `content`. Skip.
Last real chunk has `finish_reason` set (`"stop"`, `"length"`, `"content_filter"`, ...) and may or may not have content.
After last real chunk, OpenRouter sends `data: [DONE]`.

### Partial-line buffering

`StreamReader.ReadLineAsync` handles `\n` and `\r\n` correctly across socket buffer boundaries — no manual buffering needed.

## Error mapping

`ThrowIfHttpError(HttpResponseMessage resp)`:

| HTTP | Kind | Notes |
|------|------|-------|
| 401  | `AuthFailure`        | Bad/missing key. Fatal — surface to user, suggest `/reset`. |
| 402  | `QuotaExhausted`     | Paid model attempted or credit exhausted. Should not happen for free models, but defensively map here. |
| 403  | `AuthFailure`        | Key disabled. Fatal. |
| 408  | `TransientServer`    | Request timeout. |
| 429  | `TransientRateLimit` | Read `Retry-After` header (seconds) if present → pass as `retryAfter`. |
| 5xx  | `TransientServer`    | All 500/502/503/504. |
| `HttpRequestException` / `SocketException` | `NetworkDown` | DNS, connection refused, etc. |
| `TaskCanceledException` (not user-cancel)   | `TransientServer` | Server-side timeout. |
| `JsonException` mid-stream                   | `MalformedResponse` | Skip the bad chunk only if we want best-effort; otherwise fail the turn. |

Throw `ChatException(kind, message, retryAfter, inner)`.

OpenRouter also returns JSON error bodies on non-2xx:

```json
{ "error": { "code": 429, "message": "Rate limited", "metadata": { "raw": "..." } } }
```

Read `error.message` for the exception message when available.

## Rate-limit headers

OpenRouter returns:
- `X-RateLimit-Limit`
- `X-RateLimit-Remaining`
- `X-RateLimit-Reset` (unix epoch seconds)
- `Retry-After` (seconds, on 429)

Use `Retry-After` for cooldown when present; otherwise apply default cooldowns from `04-model-rotation.md`.

## Why these defaults

- `HTTP-Referer` and `X-Title` are *optional but recommended* by OpenRouter — they let your app appear in their dashboards and may affect free-tier prioritization.
- `HttpCompletionOption.ResponseHeadersRead` is critical: without it, `SendAsync` buffers the full body, defeating streaming.
- Read line-by-line (not byte-by-byte) — SSE is line-oriented, so `ReadLineAsync` is the simplest correct primitive.

## OAuth / PKCE (key acquisition)

OpenRouter exposes a PKCE flow so apps can mint a user-scoped API key without the user copy-pasting one. **Contract surface** — same wire applies on PWA (Phase 6) and Android (Phase 7).

### Step 1 — Generate PKCE pair

- `code_verifier` — cryptographically random URL-safe string, 43–128 chars (RFC 7636 §4.1). Allowed characters: `[A-Za-z0-9\-._~]`.
- `code_challenge` — `BASE64URL-NO-PAD(SHA256(ASCII(code_verifier)))` (RFC 7636 §4.2).
- `code_challenge_method` — `"S256"` (literal).

Reference test vector (RFC 7636 Appendix B):
```
verifier  = dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk
challenge = E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM
```

### Step 2 — Open browser to auth URL

```
https://openrouter.ai/auth
  ?callback_url=<url-encoded local callback>
  &code_challenge=<challenge>
  &code_challenge_method=S256
```

The `callback_url` must be reachable by the user's browser. **Use a fixed URL — not an ephemeral port.** OpenRouter docs explicitly recommend `http://localhost:3000` for local-first apps. Varying the callback per attempt causes server-side `409 Failed to create or update app while creating auth code` because OpenRouter upserts an "app" record keyed by callback URL.

Desktop impl: bind `HttpListener` to `http://localhost:3000/callback/` and pass `callback_url=http://localhost:3000/callback`. If port 3000 is already in use on the user's machine, surface a clear error and fall back to paste (no auto-retry on a different port).

For PWA: this is the app's own origin route. For Android: deep-link or in-app browser intercept.

### Step 3 — Capture the callback

OpenRouter redirects the browser to `<callback_url>?code=<one-time-code>` after the user signs in and approves. The desktop listener should respond with a small HTML page ("you can return to OpenKey now") and shut down.

### Step 4 — Exchange the code

```
POST https://openrouter.ai/api/v1/auth/keys
Content-Type: application/json

{
  "code": "<one-time-code>",
  "code_verifier": "<verifier>",
  "code_challenge_method": "S256"
}
```

Response body:
```json
{ "key": "sk-or-v1-..." , "user_id": "..." }
```

Persist `key` exactly like a pasted key — DPAPI on desktop, platform-equivalent on PWA/Android (see `05-persistence-and-reset.md`).

### Error mapping

| HTTP | Kind | Notes |
|------|------|-------|
| 400  | `MalformedResponse` | Bad code / verifier mismatch. User must retry the flow. |
| 401  | `AuthFailure`        | Code expired or already used. Retry the flow. |
| 409  | `TransientServer`    | Conflict creating/updating app record. **Auto-retry once after 750 ms** before surfacing. |
| 5xx  | `TransientServer`    | Show friendly error, offer paste fallback. |

### Why PKCE not pure OAuth

PKCE avoids the need for a client secret. OpenKey ships its source / binaries publicly; storing a client secret would be pointless. PKCE binds the code to the originating session via the verifier, which is the standard for public clients.
