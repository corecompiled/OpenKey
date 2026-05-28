# 01 — Architecture

## Design goal

Build Phase 1 such that Phases 2–5 require **adding code, not rewriting**. The `IChatProvider` seam is the most load-bearing decision in this doc.

## Layer diagram

```
┌──────────────────────────────────────────────────────────────┐
│  ConsoleHost (REPL loop, stream renderer, Spectre UI)         │  Phase 2 swaps this for AvaloniaHost
├──────────────────────────────────────────────────────────────┤
│  CommandRouter     ←  parses /reset /quit /model              │
├──────────────────────────────────────────────────────────────┤
│  ChatEngine        ←  turn buffer, rolling-window trim,       │
│                       provider invocation, rotation policy    │
├──────────────────────────────────────────────────────────────┤
│  IChatProvider     ←  STABLE INTERFACE (the seam)             │
│       ▲                                                       │
│       ├── OpenRouterProvider   (Phase 1)                      │
│       ├── ClaudeCodeProvider   (Phase 5 — subprocess `claude`)│
│       └── AnthropicProvider    (Phase 5 — SDK direct)         │
├──────────────────────────────────────────────────────────────┤
│  HttpClient / Process.Start / Anthropic.SDK                   │
└──────────────────────────────────────────────────────────────┘

Sidecars (injected into ChatEngine and ConsoleHost):
    IKeyStore        — DPAPI Protect/Unprotect
    ISessionStore    — JSON load/save of last conversation
    IModelCatalog    — cached list of available free models
    IRotationPolicy  — picks next model, tracks cooldowns
```

## `IChatProvider` contract

This is the seam. Everything downstream depends on this interface staying stable.

```csharp
namespace OpenKey.Core.Providers;

public interface IChatProvider
{
    string Id { get; }                       // "openrouter" | "claude-code" | "anthropic"
    string DisplayName { get; }

    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct);

    IAsyncEnumerable<ChatChunk> StreamChatAsync(
        ChatRequest request,
        CancellationToken ct);
}

public sealed record ChatRequest(
    string Model,
    IReadOnlyList<ChatMessage> Messages,
    int? MaxTokens = null,
    double? Temperature = null);

public sealed record ChatMessage(string Role, string Content);   // role: "system" | "user" | "assistant"

public sealed record ChatChunk(string DeltaText, bool IsFinal, string? FinishReason);

public sealed record ModelInfo(
    string Id,
    string DisplayName,
    int ContextLength,
    bool IsFree);
```

**No OpenRouter-specific types may leak into this interface or into `OpenKey.Core`.** SSE parsing, OpenRouter JSON shapes, and HTTP headers live in `OpenKey.Providers.OpenRouter` and stay there.

## Solution layout

```
C:\Users\Patron\OpenKey\
├── docs\                              ← this guide set
├── OpenKey.sln
└── src\
    ├── OpenKey\                       ← Console app, entry point, hosts UI
    │   └── OpenKey.csproj             (outputs OpenKey.exe)
    ├── OpenKey.Core\                  ← interfaces, models, ChatEngine, sidecars
    │   └── OpenKey.Core.csproj
    └── OpenKey.Providers.OpenRouter\  ← OpenRouter impl of IChatProvider
        └── OpenKey.Providers.OpenRouter.csproj
```

Future:
```
    ├── OpenKey.Gui\                       (Phase 2, Avalonia)
    ├── OpenKey.Providers.ClaudeCode\      (Phase 5)
    ├── OpenKey.Providers.Anthropic\       (Phase 5)
    └── OpenKey.Rag\                       (Phase 4)
```

## Dependency direction

```
OpenKey         ──→ OpenKey.Core
OpenKey         ──→ OpenKey.Providers.OpenRouter
OpenKey.Providers.OpenRouter ──→ OpenKey.Core
```

`OpenKey.Core` has **no** references to provider projects. Providers register themselves at host startup (DI).

## Data flow (one user turn)

```
user types "hello" + Enter
   │
ConsoleHost.ReadLineAsync
   │
CommandRouter.IsCommand("hello") → false
   │
ChatEngine.SendUserMessageAsync("hello", ct)
   │
   ├── append to turn buffer
   ├── rolling-window trim if near model.ContextLength
   ├── ask RotationPolicy.PickAsync(catalog) → returns ModelInfo
   └── loop:
         provider.StreamChatAsync(request, ct)
            │
            ├── on ChatChunk → ConsoleHost.RenderDelta(chunk.DeltaText)
            ├── on IsFinal=true → break, append assistant turn, persist session
            └── on exception → classify (see error taxonomy below)
                  ├── transient → RotationPolicy.MarkFailure(model, reason), pick next, retry
                  └── fatal → surface to user, abort turn
```

## Cancellation

- A single `CancellationToken` flows from `ConsoleHost` (linked to Ctrl+C handler via `Console.CancelKeyPress`) through `ChatEngine` into the provider's `StreamChatAsync`.
- Ctrl+C mid-stream cancels the HTTP request, discards the partial assistant message, leaves the user turn in the buffer, and returns to the prompt.
- A second Ctrl+C exits the app (Spectre confirm or hard exit — decided in Phase 1.1).

## Error taxonomy

Single enum in `OpenKey.Core`, all providers map their failures into this:

```csharp
public enum ChatErrorKind
{
    TransientRateLimit,   // 429
    TransientServer,      // 5xx, timeouts
    AuthFailure,          // 401, 403 → fatal, prompt /reset
    QuotaExhausted,       // 402 or provider-specific quota signal
    NetworkDown,          // DNS/socket failures
    MalformedResponse     // unparseable SSE chunk, etc.
}

public sealed class ChatException : Exception
{
    public ChatErrorKind Kind { get; }
    public string? RetryAfterHint { get; }   // populated from headers when present
    public ChatException(ChatErrorKind kind, string message, string? retryAfter = null, Exception? inner = null)
        : base(message, inner) { Kind = kind; RetryAfterHint = retryAfter; }
}
```

`RotationPolicy` reads `ChatErrorKind` to decide cooldown duration (see `04-model-rotation.md`).

## Extensibility seams (Phase 2–5 readiness)

| Phase | What plugs in | Where |
|-------|---------------|-------|
| 2 GUI | New host project (Avalonia) reuses `OpenKey.Core` unchanged | Replace `ConsoleHost` with `AvaloniaHost` in DI |
| 3 Tools | Extend `ChatRequest` with `IReadOnlyList<ToolSpec> Tools`, add `ToolCall` to `ChatChunk` | Backward-compatible extension; providers ignore if not supported |
| 4 RAG | New sidecar `IContextSource`, `ChatEngine` queries it before sending | Pure additive in Core |
| 5 Claude Code | New `IChatProvider` impls | New project + DI registration; no changes to Core/Host |

## Why this split

- **Console + Core stable**: Phase 2 GUI ships without touching chat logic.
- **Providers are leaf nodes**: adding a provider = one new project, one DI line.
- **Core has no I/O**: easy to unit test (mock `IChatProvider`, mock `IKeyStore`).
- **Sidecars are interfaces**: swap `SessionStore` from JSON to SQLite in Phase 4 without touching `ChatEngine`.

## Cross-UI contract

OpenKey will eventually ship on three surfaces: desktop console exe (Phase 1), desktop GUI (Phase 2), PWA (Phase 6), Android APK (Phase 7). The C# implementation here is the **reference impl**. Other impls are ports.

To keep all surfaces aligned, the `docs/` directory is the **language-agnostic contract**. The following sections are **normative** — any UI / language implementation must match them exactly:

| Contract surface | Defined in |
|------------------|------------|
| `IChatProvider` semantics (request/response shape, streaming, capabilities) | `01-architecture.md` § "IChatProvider contract" |
| Error taxonomy (`ChatErrorKind` values and meanings) | `01-architecture.md` § "Error taxonomy" |
| OpenRouter wire format (headers, request body, SSE parsing, model filter) | `03-openrouter-integration.md` |
| Rotation policy (cooldown durations, retry classes, exponential backoff cap) | `04-model-rotation.md` |
| Persistence schema (file/store names, JSON field shapes, first-run flow, `/reset` semantics) | `05-persistence-and-reset.md` |

### Storage translation table

Each surface translates the persistence schema to its native primitives, but **field names and semantics stay identical**:

| Concept | Desktop (Windows) | PWA (browser) | Android |
|---------|-------------------|---------------|---------|
| Root | `%APPDATA%\OpenKey\` | `IndexedDB` database `openkey` | `Context.getFilesDir()/openkey/` |
| Encrypted key (`key.bin`) | DPAPI (user scope) | Web Crypto AES-GCM, passphrase-derived | Android Keystore-backed AES-GCM |
| Session (`session.json`) | JSON file | IndexedDB object store `session` | JSON file |
| Model cache (`models.cache.json`) | JSON file | IndexedDB object store `models` | JSON file |
| Rotation state (`rotation.state.json`) | JSON file | IndexedDB object store `rotation` | JSON file |
| Config (`config.json`) | JSON file | IndexedDB object store `config` | JSON file |

### Change rule

Any change to a contract surface must update the **contract doc first**, then propagate to every impl. Adding a non-breaking field is the only exception — and even then, the contract doc gets updated in the same change.

A C# impl change that has no corresponding contract-doc change is a bug.
