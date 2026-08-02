# Layers

Three projects. Dependencies point inward, and nothing points back out.

```
OpenKey  ─────────►  OpenKey.Core  ◄─────────  OpenKey.Providers.OpenRouter
(host)                (contracts)                      (transport)
```

## `OpenKey.Core` — the middle

Engine, rotation policy, storage implementations, and the contracts everything else agrees on.

**It has zero `PackageReference` entries.** Not "few" — zero. That is the enforcement mechanism for
CLAUDE.md's rule that Core must not depend on a host or a provider: there is no console library to
accidentally call, no HTTP client to reach for, no JSON attribute from a third party to leak into a
persisted shape. Adding a package here should feel like it needs justifying, because it does.

Targets `net10.0` — not `net10.0-windows`. This matters more than it looks: it is what keeps a
future browser or Android port from inheriting a Windows-only target framework through the shared
layer.

| Area | Types |
|---|---|
| Contracts | `IChatProvider`, `ChatRequest`, `ChatMessage`, `ChatChunk`, `ModelInfo`, `ChatErrorKind`, `ChatException` |
| Engine | `ChatEngine`, `IRotationPolicy`, `RotationPolicy`, `ITokenCounter` |
| Storage | `IKeyStore`, `ISessionStore`, `IModelCatalog`, `IConfigStore`, `JsonSessionStore`, `JsonModelCatalog`, `JsonConfigStore`, `OpenKeyJsonContext` |
| Paths | `IAppPaths` |

`IAppPaths` deserves a note. It has one required member, `RootDir`, and derives every filename from
it via default interface members. A test substitutes a temp directory by implementing a single
property, which is why storage tests need no filesystem mocking.

## `OpenKey.Providers.OpenRouter` — the outside edge

Everything that knows what OpenRouter's API looks like: URL shapes, headers, SSE framing, the
pricing fields that determine whether a model is free, and the mapping from HTTP status to
`ChatErrorKind`.

It references Core to implement `IChatProvider` and depends on nothing else. A second provider is a
sibling project, not a modification to this one.

## `OpenKey` — the host

The console: `ConsoleHost`, `CommandRouter`, the `Ui/` components, `MarkdownConsoleRenderer`, the
DPAPI key store, and the OAuth flow.

This is the only project that targets `net10.0-windows`, and the only one that references
Spectre.Console, Markdig, or DPAPI. `DpapiKeyStore` lives here rather than in Core precisely so
that Core can stay platform-neutral — a browser port supplies its own `IKeyStore` backed by Web
Crypto without Core changing at all.

Composition happens in `Program.cs`, explicitly, with no assembly scanning. One detail is load-
bearing: the provider receives `keys.Load` as a `Func<string?>` rather than a key value, so a
`/reset` that replaces the stored key takes effect immediately without rebuilding the container.

## Tests

| Project | Scope | Target |
|---|---|---|
| `OpenKey.Core.Tests` | Engine, rotation, storage | `net10.0` |
| `OpenKey.Tests` | Provider, console UI, PKCE | `net10.0-windows` |

Both reach internals through `InternalsVisibleTo`. The split follows the platform boundary: Core's
tests run anywhere Core does.

## Why this split earns its keep

The honest test of a layering scheme is whether it prevents anything. This one does:

- Rotation logic is testable without HTTP, because `ChatEngine` sees `IChatProvider` and never a
  socket. `FakeChatProvider` is 70 lines.
- The console can be rewritten — it was, wholesale — with no change to Core.
- Key storage swaps per platform because nothing above it knows what DPAPI is.

The part that is *not* yet proven is multi-provider. `IChatProvider.Id` and `DisplayName` exist and
are read by nothing. Until a second provider ships, treat that seam as designed but unexercised —
see [`08-decisions.md`](08-decisions.md).
