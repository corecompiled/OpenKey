# Provider layer

`OpenRouterProvider` is the only code that knows what OpenRouter looks like. Wire format details
are normative in [`../03-openrouter-integration.md`](../03-openrouter-integration.md).

## The interface

```csharp
public interface IChatProvider
{
    string Id { get; }
    string DisplayName { get; }
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct);
    IAsyncEnumerable<ChatChunk> StreamChatAsync(ChatRequest request, CancellationToken ct);
}
```

`Id` and `DisplayName` are read by nothing today. They exist for a provider picker that does not
exist yet — see [`08-decisions.md`](08-decisions.md).

The key arrives as `Func<string?>`, not `string`, so a `/reset` that replaces it takes effect
without rebuilding the DI container.

## Deadlines are per-read, not per-response

This is the single most important thing in this file.

`HttpClient.Timeout` bounds the **entire** operation including reading the response body — even
under `HttpCompletionOption.ResponseHeadersRead`. For a streaming endpoint that is exactly wrong: a
slow model producing a long but perfectly healthy reply gets aborted mid-stream, the abort surfaces
as a cancellation that looks like a network fault, rotation moves on, and the next model dies the
same way at the same deadline. On a slow model the app could never succeed.

So `HttpClient.Timeout` is `Timeout.InfiniteTimeSpan`, and the provider applies its own:

| Deadline | Bounds |
|---|---|
| `FirstTokenTimeout` (45s) | Getting response headers, and the wait for the first SSE line |
| `StallTimeout` (30s) | The wait for each subsequent line |
| `ModelListTimeout` (30s) | The whole (non-streaming) `/models` call |

Each read gets a linked CTS with `CancelAfter`. What is being measured is the wait for the *next*
byte, which is what actually distinguishes a slow model from a dead connection. A model that
streams steadily for ten minutes is fine; one that goes quiet for thirty seconds is not.

The catch filters on `!ct.IsCancellationRequested` so our own deadline is never reported as the
user cancelling.

## SSE framing

Lines are read one at a time. Blank lines and `:` comments are skipped; only `data: ` payloads are
parsed.

```
data: {"choices":[{"delta":{"content":"Hel"}}]}
data: {"choices":[{"delta":{"content":"lo"}}]}
data: {"choices":[{"delta":{},"finish_reason":"stop"}]}
data: [DONE]
```

### `[DONE]` without a finish reason

Some models close the stream with `[DONE]` and never send `finish_reason`. Reporting `null` there
made the engine treat a complete reply as unfinished: it discarded the text, cooled the model down,
and retried elsewhere — punishing a model that had answered correctly.

The provider now remembers the last `finish_reason` it saw and emits `lastFinishReason ?? "stop"`.
`"stop"` is the right default: the server closed the stream cleanly, which is what a normal
completion looks like.

## Free-model detection

A model is free if its id ends in `:free`, or if both `pricing.prompt` and `pricing.completion`
parse to zero.

*Parse*, not string-match. The original compared against `"0"`, `"0.0"` and `"0.00"` exactly, so
the moment OpenRouter formatted a price as `"0.000000"` a free model was silently classified as
paid and vanished from `/models`. Comparing formatted numbers as text is a bug waiting for someone
else's formatter to change.

## Error mapping

| HTTP | Kind | Retried? |
|---|---|---|
| 401, 403 | `AuthFailure` | no |
| 402 | `QuotaExhausted` | no |
| 400, 404, 422 | `InvalidRequest` | no |
| 408 | `TransientServer` | yes |
| 429 | `TransientRateLimit` | yes (honours `Retry-After`) |
| 5xx | `TransientServer` | yes |
| other | `MalformedResponse` | yes |

400/404/422 were previously `MalformedResponse`, which is retryable — so a request no model could
accept was resent five times, cooling down five models on the way.

## Hostile responses

`ListModelsAsync` guards both the JSON parse and the `data` property lookup.

A captive portal — hotel, airport, café Wi-Fi — answers *any* request with its own login page and
HTTP 200. Unguarded, that threw an unhandled `JsonException` during first run, and with no
top-level handler the window closed on the same frame as the stack trace. For software distributed
on a USB stick this is a likely first experience, not an edge case. It now maps to `NetworkDown`
with copy that names the actual cause: you may still need to sign in to this Wi-Fi.

An in-stream `error` object raises a `ChatException` carrying the server's message.
