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

### Message bubbles, waiting indicator, scratch home — 2026-08-04

- **User turns hug their content and anchor right**, capped at 560 of the 720 column; replies stay
  full width on the left. They previously stretched to the column, so one wide code fence in one
  reply inflated every short message in the conversation.
- **A thinking indicator** for the gap between sending and the first token, which on a busy free
  model is where the wait actually is. Three dots on a staggered opacity animation — render-only, so
  a long wait costs no layout passes. Deliberately **not** the mark spinning: animating the logo
  would permanently tie it to "busy", which the identity notes rule out. The caret now shows only
  while tokens arrive (`IsTyping`), since a caret blinking at nothing reads as a stall.
- **`OPENKEY_HOME`** overrides the storage root. Added after a test script deleted real chat files:
  there was no way to exercise the app without pointing it at the only copy of someone's history.
  The key remains DPAPI-encrypted per Windows account wherever the root lives, so this is not a
  route to a portable key.
- Fixed: **"Try again" duplicated the message.** The engine drops a failed turn from its history but
  the transcript keeps it, so the resend appended a second copy. Pre-existing, but invisible while
  Retry was a header button and unavoidable once it moved onto the failed message itself.

### Light and mono corrected against measurements — 2026-08-04

Both found by looking at the running app and sampling pixels, not by reading the palette.

- **Light was faint because of one token.** Body text measured 15.3:1, but everything using `Muted`
  — header buttons, the sidebar caption, chat counts, the composer hint — measured **5.01–5.08:1**
  against the sunken beige. AA, but the floor, and applied to most of the chrome; the same role in
  dark measured 6.9–7.5. `Muted` is now 7.2:1 minimum and the surfaces lost most of their yellow
  while keeping the 1.175 plane separation. Dark and mono got the same role lifted for parity.
- **Mono's code block had no plane.** `CodeSurface` sat **1.048** against `Surface` — invisible — so
  a snippet did not read as a block at all, while three of the six token roles clustered at the top
  of the luminance range and comments sat at 4.7:1. The block is now 1.10 from the page with an
  evenly spaced ramp, and code fences take `LineStrong` rather than the divider hairline in every
  theme.
- **The chat list now spans the window height**, with the composer confined to the conversation
  column. This also deletes the gutter binding added earlier the same day: the composer inherits the
  correct offset from its column, so there is nothing left to keep in sync.

### Header actions relocated to where they act — 2026-08-04

The header held a 260px model picker plus four `ghost` buttons of identical weight, so the
second-most-frequent action looked the same as the yearly one. Reviewed by the UI specialist against
the user's own proposals; two accepted as given, one accepted with a correction, one rejected.

- **New chat → head of the chat list**, where the chat it creates appears. Not duplicated: the
  header keeps a stand-in bound to `!ShowChats`, so there is never a second copy and never none, and
  `Ctrl+N` always has a visible affordance. Above the `ListBox`, not inside it — a `ListBoxItem`
  would take selection and fight the `SelectedChat` setter, which opens a chat on assignment.
- **Copy → per reply, revealed on hover.** Right-click was rejected: turns are `SelectableTextBlock`,
  so right-click already belongs to text selection, and a custom `ContextMenu` would work on the
  padding but not the prose — the same dead-zone failure already fixed once in the sidebar. Fixed a
  real bug on the way: the header button copied the *latest* reply regardless of which one you were
  looking at.
- **Retry → "Try again" on the unanswered message.** A failed or stopped send deletes its empty
  reply, so the transcript ending on your own turn is a reliable signal. It belongs there rather than
  in the status bar because the status bar is dismissible and the draft box has already been cleared
  — the transcript is the only place the text still exists. "Regenerate on the last reply" was
  rejected: `RetryAsync` appends a new pair rather than replacing one, so the label would lie, and
  real regeneration needs engine support.
- **Model picker → composer hint row**, left-aligned to the reading column. It is an input to the
  next send, not a toolbar setting. Not the `⋯` menu, which is per-app rather than per-message.

Open: turn-action buttons are 34px per the house floor, while `CodeBlockView`'s Copy is 26px. The
two should agree; which way is a judgement call and is not decided here.

### Composer re-anchored to the reading column — 2026-08-04

The composer lives in the root grid, so unlike the transcript it never sat inside the sidebar's
column and never inherited its offset. Left-aligned, its content started at x=24 while replies start
at x=260 — a 236px gap, under the chat list rather than under the conversation.

Fixed with a gutter `Panel` bound to `#Scroller.Bounds.X`, so the composer tracks the transcript's
own offset rather than recomputing the sidebar width. It follows a splitter drag and collapses to 0
when the chat list is hidden, and the two cannot disagree. Verified via UI Automation: composer
input and transcript card both at 260 logical.

The 720 reading cap stays on both surfaces. Deliberately not full-bleed: a full-width input would
reflow every message on send, would become the largest object on screen while carrying the least
content, and would drift the Send button ~930px from the last character typed.

### Inline formatting in the GUI — 2026-08-03

Bold, italic, bold-italic, inline code and links render instead of being flattened to plain text.
Markdig does the parsing in both hosts, so the console and the GUI cannot drift on what counts as
emphasis.

- `MarkdownBlock` keeps a list of `InlineSpan` runs alongside its plain `Text`. `Text` stays the
  source for copy and export — a pasted transcript should not carry styling the destination cannot
  honour.
- Styles are flags and are OR-ed down the tree, because markdown nests: `***x***` is bold wrapping
  italic, and reassigning would drop the outer one.
- Run colours bind as `DynamicResource` rather than resolving once, so a theme switch repaints an
  already-displayed transcript.
- List markers are their own span, so `- **Done**` does not embolden the bullet.

### Local AOT publish repaired — 2026-08-03

Two independent faults, both now fixed; `dotnet publish` with no wrapper and no extra flags
produces the AOT binary again. Details in [`docs/06`](docs/06-build-and-distribute.md).

1. The C++ workload was never installed. A `link.exe` from an unrelated Visual Studio made it look
   present, but it shipped with no import libraries and no Windows SDK. Installed VS 2022 Build
   Tools with `Microsoft.VisualStudio.Workload.VCTools --includeRecommended`.
2. `vswhere.exe` was not on `PATH`. `vcvarsall.bat` calls it internally, recovers when it fails,
   but prints to stderr on the way — and MSBuild merges stderr into the probe's captured output, so
   the compiler spliced the error text into the linker path and ran a command starting
   `'vswhere.exe' is not recognized...`. Adding the Installer directory to `PATH` silences it.

`publish\OpenKey.exe` (11.6 MB) and `publish\OpenKeyApp.exe` (26.5 MB) are current, both carrying
the new icon, both launch-tested.

### Visual system, control states, identity — 2026-08-03

- **Palette rebuilt.** Every previous token was an unmodified Tailwind swatch; the new ramps are
  chosen for this app. Governing rule across all four themes: chrome recedes, content advances,
  overlays float — the old palette did the reverse, which is why the window never resolved into
  planes. Light gained four real surfaces ending in true white (the old `Surface` and
  `SurfaceRaised` were 3 L\* apart — a 1.07:1 step, invisible), dark moved off navy so the accent
  has somewhere to stand, and `mono` gained six separated luminance steps (it previously collapsed
  Body, Brand, Ok and CodeType onto one identical grey, proving nothing).
- **Control interaction states.** New `Styles/Controls.axaml` at *application* scope — the button
  classes used to live in `MainWindow.Styles`, so both dialogs rendered as raw Fluent, which was
  the single largest reason the app looked half-styled. Hover, pressed, disabled, and a
  keyboard-only `:focus-visible` ring for every button, list row, and the model dropdown.
- Two verified bugs behind "the buttons feel placed, not designed": Fluent's `ControlTheme` sets
  the template part directly, so the primary button lost its brand colour on hover, and the
  destructive menu item lost its red exactly on hover. Every colour state now targets the part.
- **Resizable sidebar** — `GridSplitter`, 180–360px, hairline that lights up on hover, no layout
  shift while dragging. Width is not persisted yet (see *Next up*).
- **Turn differentiation** — your message on a raised card, the reply on the page. Both speakers
  previously used the accent colour, so it meant "a name is here" rather than "this is the AI".
- **Reading column** capped at 720px, explicit line heights, 34px minimum hit targets. The status
  dismiss button was ~14px — the smallest control in the app, and the one you press when something
  has already gone wrong.
- **Identity**: the mark, the wordmark lockup, and `assets/openkey.ico` on both executables. Not a
  key — 1Password, Bitwarden, KeePass and Keeper all own key marks in the same 16px taskbar slot,
  so a key would read "password manager", and it inverts the promise besides. Generated by
  `tools/make-icon.ps1`; see [`docs/06`](docs/06-build-and-distribute.md).
- **Your name** — `/name` in the console, **⋯ → Your name…** in the GUI. Not asked at first run:
  that screen already asks for a key, and the Windows account name is right almost every time.
  Display only, never sent to a model, never in an export. `userName` in
  [`docs/05`](docs/05-persistence-and-reset.md).

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

### Phase 2 — Avalonia GUI — 2026-08-03

Shipped to the roadmap's bar: parity with the console feature set plus mouse selection, copy
buttons on code blocks, and syntax highlighting. `OpenKeyApp.exe` builds AOT and ships from the
same tag as the console.

`OpenKey.Windows` was extracted so both hosts share DPAPI, app paths, OAuth and the tokenizer.
`OpenKey.Core` still has zero package references.

### v0.1.0 — 2026-05-28

First release. See [`CHANGELOG.md`](CHANGELOG.md#010--2026-05-28).

---

## Next up

Ordered by user value within tier. Lowest tier wins.

Tier 2, Tier 3 and Tier 5's Phase 2 (the GUI) are complete — see **Done** above. What remains is
Tier 4, which is Phase 5 work and a step change in scope rather than more polish.

Smaller GUI follow-ups, none blocking:

- Window size and position persistence, **and sidebar width**. Deliberately skipped: `config.json`'s
  shape is a contract surface documented in `docs/05`, it is shared with the console host which has
  no sidebar, and GUI-only layout values do not belong in it without a decision. The likely answer
  is a separate `window.json` that is explicitly *not* a contract surface — but that still changes
  the `%APPDATA%\OpenKey\` layout, so it needs sign-off first.
- Social preview card (`assets/openkey-social.png`, 1280×640) for the GitHub repo settings. The
  mark and colour are settled; only the export remains. Not embedded in the README — a large
  centred logo above a heading GitHub already renders reads as self-important.
- Per-message copy buttons, in addition to the toolbar's copy-last and the per-code-block copy.

### Project rename — name to be decided

**Highly probable**: "OpenKey" is being replaced. Nothing to do until the new name is chosen, but
recording the surface area now, because it is much wider than a find-and-replace and some of it
cannot be renamed silently.

Code and build:
- `OpenKey.Core`, `OpenKey.Windows`, `OpenKey.Providers.OpenRouter`, `OpenKey.Gui` project and
  assembly names; `OpenKeyApp` is the GUI `AssemblyName` and appears in `avares://` URIs
- Root namespaces, `InternalsVisibleTo`, the three test projects
- `OpenKey.sln`, `Directory.Build.props`, both `ApplicationIcon` paths, `app.manifest`
- `OpenKeyConfig`, `OpenKeyJsonContext`, `GuiTheme`, and the `OpenKey AI` speaker label

**Contract surfaces — these carry a migration cost, not just a rename:**
- `%APPDATA%\OpenKey\` is the storage root. Renaming it strands every existing user's key, chats
  and settings unless a migration copies the old directory forward — the same shape as the
  `session.json` → `chats\` migration already in `JsonChatStore`.
- `key.bin` is DPAPI-encrypted per Windows account, so it **can** be moved by a local migration but
  can never be regenerated from elsewhere. Get this right or people lose their key.
- `docs/05-persistence-and-reset.md` documents the layout and is normative.

Outside the repo, and not all of it under our control:
- GitHub repo name and every `corecompiled/OpenKey` URL in docs, `UpdateChecker`'s hardcoded
  releases endpoint, CI and release workflows
- The Scoop manifest `packaging/scoop/openkey.json`, its package id and the `openkey` bin alias —
  renaming breaks existing installs' update path
- Published release titles and assets; `SECURITY.md`, `LICENSE`, `CONTRIBUTING.md`
- The mark itself is a stylised **K** — see the identity notes in **Done** above. A new name
  starting with a different letter invalidates the logo's whole rationale, so the rename and the
  mark need deciding together, not in sequence.

Sequence when it happens: pick the name → decide the storage migration → rename code and docs →
rename the repo and fix the update endpoint → republish Scoop. The update checker is the sharp edge:
if it is renamed before a release exists under the new name, installed copies stop seeing updates.

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
