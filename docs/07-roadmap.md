# 07 — Roadmap

What ships after Phase 1, in priority order, with enough design notes that future-you (or future-Claude) can pick up without re-discovering.

## Tier ladder + sort rule

All new roadmap / QoL items must be slotted by this ladder. Lowest applicable tier wins. Within a tier, order by user value.

| Tier | Meaning | Examples |
|------|---------|----------|
| 1 | Exe-blocking (must ship for Phase 1) | core REPL, OpenRouter integration, rotation, key storage, `/reset` |
| 2 | Exe polish (Phase 1.1 / 1.2) | `/export`, token counter, update checker, multi-key |
| 3 | Current-UI features within an existing host | new slash commands, console theme toggle |
| 4 | New providers (no UI change) | Claude Code provider, Anthropic provider |
| 5 | New UI surfaces | GUI (Avalonia), PWA, Android APK |

**One canonical home per item.** If an item doesn't fit any tier cleanly, drop it into **Quick Wins** at the bottom of this file. Never silently duplicate. See also the doc-hygiene checklist in `CLAUDE.md`.

## Phase 1.1 — QoL polish

Small additive features that don't change the architecture.

- **`/history`** — Spectre paginated view of current session turns. Read from `_engine.Turns`.
- **`/export <path>`** — write current session to a markdown file at `<path>`. Format:
  ```markdown
  # OpenKey session — 2026-05-27 14:00 UTC
  **Model:** meta-llama/llama-3.3-70b-instruct:free

  ## user
  hi

  ## assistant
  Hello! ...
  ```
- **`/new`** — clear in-memory turns (keep config and key). Effectively `_engine.NewSessionAsync()`.
- **`/stop`** — cancel a running reply without Ctrl+C. Ctrl+C already works; nothing on screen says so.
- **`/retry`** — resend the last message, typically after a rotation or an error card.

`/about`, `/models`, the version banner and the active-model indicator shipped in Phase 1 and are
documented in [`02-phase1-build.md`](02-phase1-build.md#acceptance-checklist-phase-1-done). They
were listed here by mistake: the tier ladder puts them at Tier 1, and the lowest applicable tier
wins. The active-model indicator landed as the `OpenKey AI · <model> · <elapsed>` reply header
rather than a per-line prefix.

## Phase 1.2 — QoL deeper

Features that touch storage or DI but stay backward-compatible.

- **Token counter** — estimate tokens per turn and show running total in status line. Use a real tokenizer NuGet (e.g., `Tiktoken` for OpenAI-family models, `MicrosoftDeepDev.Tokenizer` for cross-model).
- **`/config`** — interactive Spectre menu to edit `config.json` (preferred model order, max_tokens, theme).
- **Update checker** — on launch, query `https://api.github.com/repos/corecompiled/OpenKey/releases/latest`. If newer, Spectre yellow notice with download URL. **Do not auto-download.**
- **Theme toggle** — `/theme dark|light|mono`. Stored in `config.json`. Spectre styles parameterized.
- **Multi-key support**:
  - `/key add <name>` — add another OpenRouter key under a label
  - `/key list` / `/key use <name>` / `/key remove <name>`
  - Rotation policy can spread requests across keys when one rate-limits at the *account* level (different signal from per-model rate limit)
  - `key.bin` becomes `keys.bin` containing a JSON map `{name: encryptedKey}`. Migration path: if `key.bin` exists, import as `default` and rename.
- **Logging** — opt-in via `config.json`. Daily file at `%APPDATA%\OpenKey\logs\YYYY-MM-DD.log`. Never log API keys or full message bodies (just metadata: model, turn count, error kind, latency).
- **`/clear` (alias of /new)** and other ergonomic shortcuts.

## Phase 2 — GUI

**Goal:** same chat capabilities, windowed.

**Choice: Avalonia** (XAML, MIT, cross-platform, native-feeling on Windows).

- Why not WPF: Windows-only, older toolkit, less momentum.
- Why not WinUI 3: more setup friction (project templates change frequently in 2025/2026), packaging awkwardness.
- Why not Electron/web tech: defeats the lightweight portable-exe ethos.

**Project layout addition:**

```
src\OpenKey.Gui\
    OpenKey.Gui.csproj          (Sdk: Microsoft.NET.Sdk; AvaloniaUseCompiledBindingsByDefault=true)
    App.axaml                    Avalonia app shell
    MainWindow.axaml             chat surface
    ViewModels\ChatViewModel.cs  binds to OpenKey.Core.ChatEngine
```

**Key reuse principle:** `OpenKey.Core` and `OpenKey.Providers.OpenRouter` are referenced unchanged. `ChatEngine` is the boundary — the GUI binds an `IAsyncEnumerable<ChatChunk>` to a `TextBox`/`ItemsControl` exactly as the console renders it.

**New publish target:** Avalonia can also publish single-file self-contained. Same flags as Phase 1's `dotnet publish` line, swap project path to `src/OpenKey.Gui/OpenKey.Gui.csproj`. Output size: ~50–80 MB.

**Phase 2 ships when:** GUI feature-parity with Phase 1.2 console + GUI-specific QoL (mouse selection, copy code blocks, syntax highlighting via Avalonia.HtmlRenderer or Markdig + AvaloniaEdit).

## Phase 3 — Tool use / function calling

**Goal:** AI can call local tools.

**Interface changes:**

```csharp
public interface IChatProvider
{
    // ... existing ...

    // Phase 3 additions
    bool SupportsTools => false;   // default; OpenRouterProvider overrides true for compatible models

    IAsyncEnumerable<ChatChunk> StreamChatAsync(
        ChatRequest request,
        IReadOnlyList<ToolSpec>? tools,        // NEW
        CancellationToken ct);
}

public sealed record ChatChunk(
    string DeltaText,
    bool IsFinal,
    string? FinishReason,
    ToolCall? ToolCall = null);    // NEW

public sealed record ToolSpec(
    string Name,
    string Description,
    JsonElement ParametersSchema); // JSON schema

public sealed record ToolCall(
    string Id,
    string Name,
    string ArgumentsJson);
```

**Backward compatibility:** New params have defaults. Existing callers unchanged.

**Built-in tools** (gated by user consent — first call prompts "Allow `read_file`? [y/N]"):
- `read_file(path)` — read text file. Allow-list of dirs configured in `config.json`.
- `write_file(path, content)` — write/overwrite. Stricter allow-list.
- `web_fetch(url)` — HTTP GET, max 1MB, text-only.
- `run_shell(command)` — **off by default**, opt-in via config + per-call confirm.

**Sandbox model:** `config.json` extended with `tools.allowedRoots: ["C:\\Users\\Patron\\projects"]` and per-tool enable flags. Any tool call outside allowed roots → automatic deny + audit log.

**Loop integration:** When a model emits a `ToolCall`, `ChatEngine` pauses streaming, executes the tool (Spectre spinner + prompt for consent if first-time), appends a `role: "tool"` message with the result, and re-invokes the provider with the updated message list.

## Phase 4 — Local RAG

**Goal:** "drop a file in this folder, AI can reference it".

**New project:** `src\OpenKey.Rag\OpenKey.Rag.csproj`

**Storage:** Local SQLite via `Microsoft.Data.Sqlite` + a vector ext.
- Option A: SQLite + `sqlite-vec` (small native lib, bundled).
- Option B: LiteDB + brute-force cosine sim (no native deps). Simpler, fine up to ~10k chunks.

Phase 4 v1 = Option B (no native dep simplifies single-file publish). Migrate to sqlite-vec if scale demands.

**Embeddings:** Use OpenRouter's embedding endpoint (`/api/v1/embeddings`) with a free embedding model. Falls back to a small ONNX model bundled with the exe if no free embedding model available.

**Commands:**
- `/index <path>` — chunk the file/folder (1k-char chunks, 200-char overlap), embed, store
- `/forget <path-or-id>` — remove entries
- `/sources` — list indexed files

**Integration with ChatEngine:** new sidecar `IContextSource`. Before each user turn, query top-K (default 4) chunks by cosine, prepend as a `system` message:

```
[from indexed sources]
--- C:\notes\plan.md (chunk 3) ---
<content>
--- C:\notes\meeting.md (chunk 1) ---
<content>
```

**Privacy:** All data stays local. Embeddings are computed via OpenRouter (so chunks leave the machine briefly for embedding) — document this clearly.

## Phase 5 — Claude Code provider integration

**Goal:** chat with Claude through OpenKey, either via Claude Code subprocess (zero API cost, uses user's Claude subscription) or via Anthropic API directly.

This is **the** reason we built the `IChatProvider` abstraction up front.

### Option A — Subprocess via `claude` CLI

New project: `src\OpenKey.Providers.ClaudeCode\ClaudeCodeProvider.cs`

```csharp
public sealed class ClaudeCodeProvider : IChatProvider
{
    public string Id => "claude-code";
    public string DisplayName => "Claude (via Claude Code CLI)";

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ModelInfo>>(new[]
        {
            // NOTE: IsFree here would mean "no incremental cost to this user", which is NOT what
            // the flag means elsewhere — IModelCatalog filters on it to build the free-model list,
            // so setting it true would surface paid models in the /models picker and violate the
            // no-paid-models rule. Phase 5 needs a separate notion of "already paid for", not a
            // reuse of IsFree. Resolve before implementing.
            new ModelInfo("claude-opus-4-7",   "Claude Opus 4.7",   200_000, IsFree: false),
            new ModelInfo("claude-sonnet-4-6", "Claude Sonnet 4.6", 200_000, IsFree: false),
        });

    public async IAsyncEnumerable<ChatChunk> StreamChatAsync(ChatRequest req,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var prompt = FlattenMessages(req.Messages);
        var psi = new ProcessStartInfo("claude")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(prompt);
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add(req.Model);

        using var proc = Process.Start(psi)!;
        string? line;
        while ((line = await proc.StandardOutput.ReadLineAsync(ct)) != null)
        {
            // parse stream-json events: text deltas → ChatChunk
            var evt = JsonSerializer.Deserialize<ClaudeStreamEvent>(line);
            if (evt is { Type: "text_delta", Text: var t }) yield return new ChatChunk(t, false, null);
            if (evt is { Type: "message_stop" }) yield return new ChatChunk("", true, "stop");
        }
    }
}
```

**Detection:** check `claude` is on PATH (`Process.Start("claude", "--version")`). If not, gray out the provider in `/provider` listing.

**No API key needed** for this path — auth happens inside Claude Code, not in OpenKey.

### Option B — Anthropic SDK direct

New project: `src\OpenKey.Providers.Anthropic\AnthropicProvider.cs`

NuGet: `Anthropic.SDK` (community) or hand-rolled HTTP — same shape as `OpenRouterProvider` but with Anthropic's `/v1/messages` endpoint and SSE format.

Requires a separate API key. Stored under the multi-key system from Phase 1.2 with label `"anthropic"`.

### Selection UX

New command `/provider`:

```
/provider                  → show current provider + available
/provider openrouter       → switch
/provider claude-code      → switch (greyed if `claude` not on PATH)
/provider anthropic        → switch (greyed if no anthropic key configured)
```

`ChatEngine` resolves `IChatProvider` dynamically from a `IProviderRegistry` instead of a single DI singleton (small refactor — keep current behavior as default registry impl).

### Why this is painless

Because the seam was right from Phase 1:
- `OpenKey.Core` knows nothing about OpenRouter
- Each provider is a leaf project, leaf DI registration
- Adding Claude Code in Phase 5 = add a project, add a DI line, add a command branch. No refactor of `ChatEngine`, no refactor of `ConsoleHost` / GUI.

## Phase 6 — PWA

**Goal:** same chat capabilities, in a browser. Installable as a Progressive Web App (Add to Home Screen / desktop install).

**Stack:**
- TypeScript + React (or Svelte) — preference deferred to start of Phase 6
- Service worker for offline shell (app loads without network; calls obviously need network)
- IndexedDB for persistence (mirrors `%APPDATA%` schema field-for-field — see `01-architecture.md` § "Storage translation table")
- Web Crypto API (AES-GCM) for key encryption, **passphrase-derived** (PBKDF2 from a user passphrase set on first run)

**Why passphrase instead of DPAPI-equivalent:** browsers have no per-user OS-bound crypto. The closest is the platform `CredentialManagement` / `WebAuthn` APIs, but support is uneven. Passphrase-based AES-GCM is simpler, portable, and explicitly user-controlled. Document the threat model clearly: a passphrase-protected key is weaker than DPAPI, but stronger than plaintext.

**OpenRouter calls** go straight from the browser using the same wire format documented in `03-openrouter-integration.md`. OpenRouter is CORS-friendly for browser clients.

**Rotation policy** reuses `04-model-rotation.md` verbatim. Same cooldown table, same retry classes.

**First-run flow** mirrors `05-persistence-and-reset.md`:
1. Prompt for OpenRouter API key
2. Prompt for passphrase (used to encrypt the key in IndexedDB)
3. Validate by calling `/api/v1/models`
4. Persist encrypted key

**`/reset` semantics:** delete the IndexedDB database, drop service-worker caches, re-run first-run flow in the same tab.

**Project layout addition** (new repo or sibling folder under OpenKey):
```
src/OpenKey.Pwa/
    src/contract/        ports of OpenKey.Core models (TS)
    src/providers/openrouter.ts
    src/engine/chat.ts
    src/storage/indexeddb.ts
    src/ui/...
    public/manifest.webmanifest
    public/service-worker.ts
```

**Phase 6 ships when** PWA passes the same Phase 1 acceptance checklist (in `02-phase1-build.md`) translated to web equivalents: streaming token render, rotation on rate-limit, persisted session resume, `/reset` works.

## Phase 7 — Android APK

**Goal:** ship OpenKey as an installable Android app.

**Approach A (recommended): Trusted Web Activity (TWA) via Bubblewrap.** Wrap the Phase 6 PWA in a thin Android shell. Output is a small APK (~2 MB) that runs the PWA inside a Chrome custom tab. Near-zero extra code beyond what Phase 6 already produces.

**Approach B (fallback): Capacitor.** If TWA limitations bite (e.g., we want native file access, native push, deeper OS integration), switch to Capacitor for more native control. Larger APK, more native code, but same PWA codebase.

**Storage:**
- App-private files at `Context.getFilesDir()/openkey/`
- Key encrypted with AES-GCM, with the AES key wrapped by **Android Keystore** (hardware-backed where available). This is the closest analogue to DPAPI on Android — per-app per-device, no user passphrase needed.

**OpenRouter calls, rotation policy, persistence schema:** same contract docs.

**Phase 7 deliverable:** signed APK + Play Store-ready bundle. Distribution channels: direct APK download, F-Droid (if we open-source), Play Store (requires developer account).

**Phase 7 ships when** Android APK passes the Phase 1 acceptance checklist translated for mobile: streaming render in app, rotation, persistence across app kill, `/reset`.

## Quick Wins

Flat list of small, non-blocking improvements. Pick off opportunistically. Items here are explicitly **not** on the tier ladder — they're standalone polish that doesn't sequence-block anything else.

Both seed items are resolved — see [`../BACKLOG.md`](../BACKLOG.md) for detail.

- ~~Colour-blind-friendly palette check~~ — done. Colour is never the only signal, and the `mono`
  palette is the standing test: a unit test asserts no hue survives it, so anything that starts
  depending on colour alone fails the build.
- ~~Richer banner artwork~~ — **deliberately not done.** The console design principle is that calm
  beats decorative, and the banner is the first thing a non-technical user sees. Art would
  contradict the design it is meant to serve.

**Execution order and current state live in [`../BACKLOG.md`](../BACKLOG.md).** This file says what
and why; the backlog says what state each item is in and what happens next.

Add new entries here only when they truly don't belong in a phase. If an idea even *might* fit a tier, put it in the tier.

## Design guardrails (revisit every phase)

Before any new phase merges:

1. **Does Core depend on anything provider-specific?** Must be no.
2. **Does the Console host know about HTTP or OpenRouter?** Must be no.
3. **Does adding a provider require modifying existing providers?** Must be no.
4. **Is `%APPDATA%\OpenKey\` still recoverable via a single `/reset`?** Must be yes.
5. **Does the exe still publish as a single file?** Must be yes (or we changed product principles deliberately).

If a phase violates any of these, stop and redesign before shipping.
