# Changelog

Notable changes to OpenKey. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.1] — 2026-08-03

Two defects that shipped in 0.2.0, both found by using the app rather than reading it — plus a
much smaller, faster binary.

### Changed

- OpenKey is now a native binary: **11 MB instead of 43 MB**, starting in about a sixth of a
  second, with nothing unpacked to a temporary folder the first time you run it. All three matter
  most on a USB stick or someone else's machine.
- Installable with [Scoop](https://scoop.sh), which also avoids the "unrecognized app" prompt.

### Fixed

- **A mistyped or revoked key was accepted and saved.** OpenKey checked keys against an endpoint
  that doesn't require one, so any text passed. You'd see "Key saved. You're ready to chat", then
  every message would fail with advice to run `/reset` — which brought you back to the same screen.
  Keys are now genuinely verified before being saved.
- **Piped or scripted input crashed the app.** Anything needing a menu — choosing a model,
  confirming an erase, first-run setup — closed OpenKey with an error when input didn't come from a
  keyboard. Those now fall back to typing a number or a word.
- Choosing a model said the choice lasted "until you close OpenKey". It's remembered.

## [0.2.0] — 2026-08-03

### Added

- `/new` starts a fresh conversation while keeping you signed in. Previously the only way to clear
  history was `/reset`, which also deleted your key.
- `/retry` resends your last message; `/history` shows the conversation; `/copy` puts the last
  reply on the clipboard; `/export` saves it as markdown, defaulting to your Desktop.
- `/theme default|dark|light|mono`, remembered between runs. `mono` drops colour entirely for
  high-contrast setups or screenshots.
- Preferences are saved, so a model chosen with `/models` now survives a restart.
- Token counting uses a real tokenizer instead of a character estimate, so conversations are
  trimmed more accurately as they grow.
- Browser sign-in falls back across several local ports instead of giving up when one is taken.
- Replies stream as they arrive. Each completed markdown block is rendered styled, so code fences
  become panels and prose keeps its emphasis, while the still-arriving tail stays plain.
- Reply header showing which model answered and how long it took.
- Error cards that say what happened and what to do next, replacing raw error-kind names.
- `/models` shows model names and context sizes instead of raw ids.
- OpenKey holds the window open on exit and on failure when it was double-clicked, so parting
  messages and errors are actually readable.
- Line editing and history at the prompt, courtesy of the Windows console reader.
- Multi-line paste is kept as one message.
- MIT `LICENSE`, `CONTRIBUTING.md`, `SECURITY.md`, `BACKLOG.md`, and GitHub Actions for CI and
  releases.
- Architecture reference under `docs/architecture/`, a user guide, and a testing guide.

### Fixed

- Links whose address contained a bracket lost their target when displayed.
- A message that kept failing could retry for several minutes; it is now bounded, and a reply
  that is genuinely arriving is never cut off.
- **Long replies from slow models always failed.** The HTTP timeout covered reading the response
  body, so a healthy reply that took over 60 seconds was aborted, misread as a network fault, and
  retried on another model that failed the same way. Deadlines now bound the wait for the next
  token rather than the whole reply.
- **Replies were never saved.** Session persistence ran after the final chunk was yielded, and the
  console stops reading at that point, so the code never executed. No conversation was ever
  written to disk.
- **Resuming a conversation never worked.** `ChatMessage` had two constructors, so deserialization
  threw an error the session store did not catch.
- **Public Wi-Fi sign-in pages crashed the app.** A captive portal replies to any request with HTML
  and HTTP 200; parsing that threw, and with no top-level handler the window closed on the stack
  trace.
- **One Ctrl+C disabled the session.** A single cancellation source lived for the whole process, so
  after the first Ctrl+C every later message was cancelled before it started.
- **A mid-reply model switch showed the answer twice**, concatenated, while the saved history
  stored it once.
- **`/reset` could strand you.** Abandoning setup left OpenKey running with no key: every message
  failed, the error suggested `/reset`, and `/reset` returned to the same place.
- **An empty model list disabled the app for 24 hours** — it was cached with a full-day lifetime and
  then crashed on every launch, unrecoverable without deleting `%APPDATA%\OpenKey` by hand.
- Replies that ended without an explicit completion signal were discarded as broken and retried,
  even though they were complete.
- Free models priced as `0.000000` were misread as paid and hidden from `/models`.
- Cancelled and failed messages stayed in the conversation history and were saved later.
- A network outage put every model on cooldown, so OpenKey stayed broken after the network came
  back.
- Requests a model cannot accept are no longer retried across every other model.
- Disk failures during a save no longer crash the app after a reply has been generated.
- Inline code was styled so that it was invisible on light terminals and indistinguishable from
  body text on dark ones.
- Markdown blocks ran together with no spacing between them.
- Glyphs that render as boxes in the classic Windows console — including the spinner, which
  appeared on every message — now fall back to plain ASCII.
- Output wider than 100 columns no longer stretches code blocks across the whole screen.
- Redirecting output to a file no longer exits the app immediately.

### Changed

- Interface language moved to plain sentence case; no screen shows acronyms like DPAPI or OAuth,
  or internal error names.
- Model rotation is reported as one quiet line instead of a warning for each attempt.
- `/reset` states plainly that it erases the key *and* the conversation before asking to confirm.
- Dependencies updated: Spectre.Console 0.49.1 → 0.57.2, Markdig 1.2.0 → 1.3.2, and the
  Microsoft.Extensions and test packages to their current releases.
- Publish settings moved into the project file, so a plain `dotnet publish` produces the shipping
  binary.
- Windows on ARM (`win-arm64`) is built alongside `win-x64`.
- Test coverage grew from 11 tests to 84.
- A dependency carrying a known high-severity advisory (`Microsoft.Bcl.Memory` 9.0.4, pulled in
  transitively) was pinned forward before it could ship.

## [0.1.0] — 2026-05-28

### Added

- First release. Windows console chat client for free OpenRouter models.
- Browser sign-in (PKCE) or paste an existing key; the key is encrypted for your Windows account.
- Automatic rotation across free models when one is rate-limited, with cooldown tracking.
- Conversation history and model cache under `%APPDATA%\OpenKey\`.
- Commands: `/about`, `/models`, `/model`, `/cls`, `/help`, `/reset`, `/quit`.
- Single self-contained `.exe` that runs from a USB stick with nothing installed.

[Unreleased]: https://github.com/corecompiled/OpenKey/compare/v0.2.1...HEAD
[0.2.1]: https://github.com/corecompiled/OpenKey/releases/tag/v0.2.1
[0.2.0]: https://github.com/corecompiled/OpenKey/releases/tag/v0.2.0
[0.1.0]: https://github.com/corecompiled/OpenKey/releases/tag/v0.1.0
