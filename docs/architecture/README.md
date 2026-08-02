# Architecture reference

Explanatory companion to [`../01-architecture.md`](../01-architecture.md), which stays the
normative contract. Nothing here overrides it: where the two disagree, `01` wins and this folder is
wrong.

These documents describe the C# desktop implementation as it actually is. Where a future browser or
Android port must match, that requirement lives in the contract docs, not here.

| Document | Covers |
|---|---|
| [01-layers.md](01-layers.md) | The three projects, why the split exists, what enforces it |
| [02-request-lifecycle.md](02-request-lifecycle.md) | One message end to end, keystroke to rendered reply |
| [03-rotation-engine.md](03-rotation-engine.md) | Model selection, cooldowns, what counts as a model's fault |
| [04-storage-and-crypto.md](04-storage-and-crypto.md) | `%APPDATA%` layout, DPAPI, atomic writes, corruption recovery |
| [05-provider-layer.md](05-provider-layer.md) | OpenRouter wire format, SSE framing, deadlines, error mapping |
| [06-console-host.md](06-console-host.md) | Streaming render, theming, capability degradation, cancellation |
| [07-error-taxonomy.md](07-error-taxonomy.md) | Every `ChatErrorKind`: cause, retry policy, what the user sees |
| [08-decisions.md](08-decisions.md) | Decisions and explicit rejections, with the evidence |

## The shape of it

```
        ConsoleHost ──────────► CommandRouter        (src/OpenKey)
             │                        │
             │ TranscriptWriter       │
             ▼                        ▼
        ┌──────────────────────────────────┐
        │           ChatEngine             │        (src/OpenKey.Core)
        │  retry · rotate · trim · persist │
        └──────────────────────────────────┘
             │            │            │
             ▼            ▼            ▼
       IChatProvider  IRotation   ISessionStore
             │         Policy     IModelCatalog
             │                    IKeyStore
             ▼
      OpenRouterProvider                            (src/OpenKey.Providers.OpenRouter)
             │
             ▼
        api.openrouter.ai
```

Dependencies point inward. `OpenKey.Core` names no host and no provider, and has no package
references at all — that absence is what actually enforces the rule, not a convention.

## If you read one thing

[`08-decisions.md`](08-decisions.md). It records what was tried, what was rejected, and why —
including several approaches that look obviously correct and are not.
