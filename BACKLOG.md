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

### Tier 2 and Tier 3 backlog, plus roadmap Quick Wins — 2026-08-03

- **`/new`** — start a fresh conversation, keeping the key. Was the most conspicuous missing verb:
  clearing history previously meant `/reset`, which also deleted the key.
- **`/retry`** — resend the last message. Routed back through the host so a resend takes exactly
  the same path as a typed message.
- **`/history`**, **`/export [path]`** (defaults to a timestamped file on the Desktop),
  **`/copy`** (via `clip.exe` — a console app has no clipboard API without a UI framework).
- **`/theme default|dark|light|mono`**, persisted.
- **`config.json`** — `IConfigStore` / `JsonConfigStore`, matching the shape already documented in
  `docs/05`. A pinned model now survives a restart, which it never could before because there was
  nowhere to store it. Hand-edited values are normalised rather than trusted.
- **Real tokenizer** — `ITokenCounter` in Core (so Core keeps its zero package references) with a
  cl100k-backed implementation in the host. Vocabulary embedded, not downloaded: OpenKey must work
  on first run behind a captive portal and makes no network call except to OpenRouter.
- **OAuth port fallback** — four known callback ports tried in order instead of only 3000. Fixed
  URLs, not random ones, since OpenRouter 409s on a varying callback.
- **Whole-turn budget** — two minutes across all attempts, checked *between* attempts only: a reply
  that is actively arriving is working, however long it has taken.
- **Link URLs escaped** rather than bracket-filtered, which used to silently drop the target of any
  URL containing a bracket.
- **Accessibility pass** — verified colour is never the only signal (`✓`/`✗` differ, error cards
  name the problem in their title), with the `mono` palette as the standing test and a unit test
  asserting no hue survives it.

Two items were resolved differently from how they were written, both noted here because the
deviation is the point:

- **`/stop` was not added.** A command cannot work while a reply streams — the app is not reading a
  prompt — and Ctrl+C already cancels correctly. The real gap was that nothing said so, so the fix
  is a one-off `(Ctrl+C to stop)` hint. A key-watcher was considered and rejected: it would swallow
  type-ahead, and people routinely start composing the next message while a reply arrives.
- **Banner artwork was not added.** The roadmap asked for richer ASCII art; the console design
  principle is that calm beats decorative, and the banner is the first thing a non-technical user
  sees. Adding art would contradict the design it is supposed to serve.

Also fixed en route: `Microsoft.ML.Tokenizers` 2.0.0 pulls in `Microsoft.Bcl.Memory` 9.0.4, which
carries a known high-severity advisory (GHSA-73j8-2gch-69rq). NuGet audit failed the build; pinned
forward to 10.0.10.

### v0.1.0 — 2026-05-28

First release. See [`CHANGELOG.md`](CHANGELOG.md#010--2026-05-28).

---

## Next up

Ordered by user value within tier. Lowest tier wins.

Tier 2 and Tier 3 are complete — see **Done** above. What remains is Tier 4, which is Phase 5 work
and a step change in scope rather than more polish.

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

- ~~**NativeAOT**~~ — **done.** 43 MB → 10.8 MB, ~0.16 s startup, nothing extracted to temp. See
  [`docs/architecture/08-decisions.md`](docs/architecture/08-decisions.md).
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
