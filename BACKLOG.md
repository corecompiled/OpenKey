# Backlog

**What this file is for.** `docs/07-roadmap.md` says *what* is planned and *why*, organised by
tier. This file says *what state each item is in* and *what order it happens in*. Entries here link
to the roadmap rather than restating it — one canonical home per fact.

Tier numbers refer to the ladder in [`docs/07-roadmap.md`](docs/07-roadmap.md#tier-ladder--sort-rule).

---

## Done

### Production readiness pass — 2026-08-03

- Dependency upgrade: Spectre.Console 0.49.1 → 0.57.2, Markdig, Microsoft.Extensions, test
  packages. SDK pinned via `global.json`; `win-arm64` added.
- Publish profile moved from README prose into `OpenKey.csproj`.
- `System.Text.Json` source generation for everything persisted and every request body.
- 14 correctness defects fixed — see [`CHANGELOG.md`](CHANGELOG.md) for the user-facing list.
- Console rebuilt: block-level streaming, `Theme`/`Glyphs`/`Components`, error cards, sentence-case
  voice, ASCII glyph fallback, exit hold.
- Test suite 11 → 65, including the first coverage `ChatEngine` has ever had.
- Docs: `docs/architecture/`, user guide, testing guide, all known doc/code contradictions
  resolved.
- Repo hygiene: `LICENSE` (MIT), `CHANGELOG.md`, `CONTRIBUTING.md`, `SECURITY.md`, this file, CI
  and release workflows.

### v0.1.0 — 2026-05-28

First release. See [`CHANGELOG.md`](CHANGELOG.md#010--2026-05-28).

---

## Next up

Ordered by user value within tier. Lowest tier wins.

### Tier 2 — exe polish

1. **`/new`** — start a fresh conversation without erasing the key. Today the only way to clear
   history is `/reset`, which also deletes the key and forces a new sign-in. The most conspicuous
   missing verb in the app.
2. **`/stop`** — cancel a running reply without Ctrl+C. Ctrl+C works, but nothing on screen says so.
3. **`/retry`** — resend the last message, typically after a rotation or an error card.
4. **`config.json`** — `IAppPaths.ConfigFile` is declared and never read or written, so nothing the
   user chooses survives a restart. This is why a pinned model lasts only until the app closes.
   Prerequisite for `/theme` and for a persisted model preference.
5. **`/export <path>`** — write the conversation to a markdown file.
6. **`/history`** — paged view of the current conversation.

### Tier 2 — robustness

7. **OAuth port fallback** — port 3000 is fixed and has no alternative; if another app holds it the
   only route is pasting a key. The port cannot simply be randomised (OpenRouter rejects a varying
   callback), so this needs a documented set of registered ports.
8. **Retry budget across a whole turn** — attempts are capped at five, but each carries its own
   deadline, so a pathological case can still run long.
9. **Escape link URLs in markdown** — `MarkdownConsoleRenderer` filters `[` and `]` out of URLs
   instead of escaping them, so a link containing brackets silently loses its target. Not a
   security issue (literals are escaped), but it is wrong.

### Tier 3 — current UI

10. **`/theme dark|light|mono`** — needs `config.json` first. The palette already lives in one place
    (`Ui/Theme.cs`), so this is mostly persistence.
11. **`/copy`** — copy the last reply to the clipboard.
12. **Token counter** — real tokenizer rather than the current `chars / 4` estimate, which drives
    context trimming.

### Tier 4 — providers

13. **Anthropic provider** — see [roadmap Phase 5](docs/07-roadmap.md#phase-5--claude-code-provider-integration).
    Worth building *before* the Claude Code subprocess provider: `IChatProvider.Id` and
    `DisplayName` are currently never read by anything, so the multi-provider seam has never been
    exercised. A second real provider is what proves the abstraction is right.
14. **Adopt `Microsoft.Extensions.AI` beneath `IChatProvider`** — `IChatClient` would supply
    tool-calling middleware for Phase 3 and a wide provider ecosystem. It must sit *under* our
    interface, never replace it: it is .NET-only and a browser or Android port could not implement
    it. Rationale in
    [`docs/architecture/08-decisions.md`](docs/architecture/08-decisions.md).

---

## Watching

Not scheduled; revisit when the trigger fires.

- **NativeAOT** — would cut the binary from ~42 MB to roughly 15–20 MB, remove the extract-to-temp
  step on first run, and start faster. All three matter for the USB story. The old blocker
  (Spectre reflection) is gone as of 0.55, and JSON source generation has landed, so the remaining
  cost is measuring what the analyzers still report. `IsAotCompatible` is already on and the tree
  is warning-clean.
- **`System.Net.ServerSentEvents`** — would replace the hand-rolled SSE reader. Preview-only today
  (`11.0.0-preview.6`); adopt when it ships stable.
- **Bracketed paste** — a more robust multi-line paste than the current timing heuristic. Needs a
  custom input reader and is Windows-Terminal-only, so it is not worth it yet.

---

## Deliberately not doing

Recorded so they are not proposed again. Full reasoning in
[`docs/architecture/08-decisions.md`](docs/architecture/08-decisions.md).

- `LiveDisplay` for the transcript — it destroys scrollback.
- `IHttpClientFactory` or Polly — transport retries would corrupt rotation's cooldown accounting.
- `Microsoft.Extensions.Hosting` — a REPL does not need a generic host.
- Paid models in any 1.x release.
- Telemetry, in any phase.
- An auto-update installer. Check-and-notify only.
