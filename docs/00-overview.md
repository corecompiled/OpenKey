# 00 — OpenKey Overview

## Pitch

OpenKey is a click-and-play Windows console chat client for free LLMs available through OpenRouter. Double-click the `.exe`, paste an OpenRouter API key once, and start chatting. The app auto-rotates across free models when one rate-limits, persists the last session, and ships as a single self-contained binary that can live on a USB stick.

## Product principles

1. **Click-and-play.** No installer, no dependencies, no manual config. First launch handles setup interactively.
2. **Zero-config first run.** Anything required to start (key, model list, %APPDATA% layout) is created on demand.
3. **Portable.** Single `.exe`, no external files. Per-user state in `%APPDATA%\OpenKey\` is auto-created.
4. **Resettable.** `/reset` wipes everything and re-runs first-run flow in the same process.
5. **Free-tier first.** Only OpenRouter free models are surfaced in Phase 1. No billing concerns.
6. **Provider-agnostic core.** Phase 1 ships OpenRouter only, but the seam for Claude Code, Anthropic, and others is in place from day 1.
7. **Desktop `.exe` is the minimum deliverable.** PWA and Android come later and reuse the same contract docs (`docs/03`, `docs/04`, `docs/05`, error taxonomy in `docs/01`).

## Phase ladder

| Phase | Goal |
|-------|------|
| **1** | Console chat, OpenRouter free models, rotation, persist + resume last session, `/reset`, guided first-run (OAuth/PKCE + paste), `/help`, `/about`, `/models`, version banner, streaming reply render |
| **1.1** | QoL: `/new`, `/stop`, `/retry`, `/history`, `/export` |
| **1.2** | QoL: token counter, `/config` editor, update checker, theme toggle, multi-key support |
| **2** | GUI (Avalonia), same Core/providers |
| **3** | Tool use / function calling (file ops, web fetch, sandboxed) |
| **4** | Local RAG over user files/folders |
| **5** | Claude Code provider (subprocess or Anthropic SDK direct) |
| **6** | PWA (TypeScript) — same contract, browser surface |
| **7** | Android APK — PWA wrapped via Trusted Web Activity (Bubblewrap) or Capacitor |

## Non-goals (Phase 1)

- No paid models. Free-tier rotation only.
- No GUI. Console (cmd) only.
- No auth proxy / multi-user server.
- No telemetry. No phone-home. No update auto-installer (Phase 1.2 will *check* for updates, not apply them).
- No model fine-tuning or RAG.

## Success criteria (Phase 1)

- User double-clicks `OpenKey.exe`, picks "Sign in with browser" on first run, completes OpenRouter sign-in in the default browser, returns to a chat prompt — no copy-paste required.
- Paste-an-existing-key remains a one-keystroke fallback for users who already have a key.
- During the OAuth wait, the user can press `p` to switch to paste or `c`/Esc to cancel back to the menu — no app restart required.
- Both acquisition paths validate against OpenRouter `/models` before persisting.
- After the key is saved, the screen clears and the banner reprints before the chat REPL opens.
- REPL labels read `<your Windows username>:` and `OpenKey AI:` (uses `Environment.UserName`).
- When a message is sent, a `Thinking` spinner shows until the **first token**, then a
  `OpenKey AI · <model> · <elapsed>` header appears and the reply streams in. Each markdown block is
  repainted styled as it completes (bold, italic, code, headings, lists), while the still-arriving
  tail stays plain — styled means settled, raw means still coming. Superseded the earlier
  buffer-until-complete design, which showed nothing at all until the reply finished.
- Banner shows `OpenKey vX.Y.Z` inline on the Spectre `Rule`, with subline `Developed by Paolo Patron`. Data dir is surfaced via `/about`, not the banner.
- `/help` prints a Spectre table of all supported commands (`/about`, `/models`, `/model`, `/cls`, `/help`, `/reset`, `/quit`).
- `/about` prints a Spectre panel with version, data dir, active model, pinned model, developer.
- `/models` opens a Spectre `SelectionPrompt` over the free-model catalog (arrow keys, enter to select). Selection pins the model for subsequent requests until app restart; rotation still kicks in if the pinned model is rate-limited.
- On a forced rate-limit, the app rotates to the next free model and continues, noting it with a single quiet line ("Moved past 2 busy models."). No error, no per-attempt warnings.
- Closing and re-launching shows the previous session restored.
- `/reset` confirms, wipes `%APPDATA%\OpenKey\`, and re-runs first-run flow.

## Glossary

- **OpenRouter** — Aggregator at `openrouter.ai` that exposes many LLM APIs (free and paid) through one unified `/chat/completions` endpoint.
- **Free tier** — OpenRouter models with `pricing.prompt == "0"` and `pricing.completion == "0"`. Rate-limited but no cost.
- **SSE (Server-Sent Events)** — Streaming HTTP response format used for token-by-token output. Lines prefixed `data: `, terminated by `data: [DONE]`.
- **DPAPI** — Windows Data Protection API. `ProtectedData.Protect(...)` encrypts bytes such that only the current Windows user account can decrypt.
- **Rotation** — Strategy of swapping to a different model when the current one returns 429/5xx/quota errors.
- **Cooldown** — Time window during which a failed model is skipped before being retried.
- **Rolling window** — Strategy of dropping the oldest conversation turns when total tokens approach a model's context limit.
- **Provider** — Implementation of `IChatProvider`. Phase 1 = OpenRouter; Phase 5 adds Claude Code and/or Anthropic direct.
