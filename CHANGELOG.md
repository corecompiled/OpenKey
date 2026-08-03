# Changelog

Notable changes to OpenKey. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **OpenKey tells you when a new version is out.** It checks once at launch and shows a quiet line
  with the link — nothing is ever downloaded or installed for you. Turn it off with
  `"checkForUpdates": false` in `config.json`; see `SECURITY.md` for exactly what the check sends,
  which is nothing beyond the request itself.
- **OpenKey has an icon.** It appears in Explorer, the taskbar, the Start menu and alt-tab, on both
  the console and the desktop app.
- **Choose what OpenKey calls you.** `/name Sam` in the console, or **⋯ → Your name…** in the app.
  It defaults to your Windows account name, so it never interrupts you to ask. The name is a label
  on your screen only — it is never sent to a model, and exports still say "You", so a transcript
  you share does not carry a name you did not choose to put in it.
- **The chat list is resizable.** Drag its edge; it remembers nothing between launches yet.
- **Your messages sit in a bubble on the right** that fits the text, instead of stretching across
  the conversation. A wide code block in a reply no longer inflates every message you sent.
- **The message box spans the conversation** instead of stopping short of the right edge, which was
  most obvious on a maximised window with an empty chat.
- Your messages now reach the right edge of the conversation, level with the message box. They were
  aligned to the right of the reading column rather than the pane, so they stopped short of it.
- **A waiting indicator while the model thinks** — three dots that pulse in turn, shown from the
  moment you send until the first word arrives. The blinking caret now only appears once text is
  actually coming in, so an empty caret can no longer look like a stall.
- **`OPENKEY_HOME`** points OpenKey at a different folder for its files. Mainly for trying things
  out without touching your real conversations; your key is still encrypted for your Windows
  account wherever the folder lives.
- **Replies now show bold, italic and inline code.** Links appear as links instead of the address
  being dumped in brackets after the text.
- **The toolbar is quieter.** New chat moved to the top of the chat list, where the chat it creates
  appears — it still shows in the toolbar when the list is hidden, so Ctrl+N always has a button.
  Copy moved onto each reply. The model picker moved down beside the message box, next to the Send
  it applies to. Export stayed put.
- **"Try again" appears on your message when a reply fails or you stop it**, so a send that went
  nowhere can be repeated without retyping.

### Changed

- **The light theme reads properly now.** Buttons, captions, hints and timestamps were all sitting
  at the bare minimum contrast against a heavy beige, so the whole interface looked faint even
  though the message text itself was fine. Those labels are ~50% stronger and the backgrounds lost
  most of their yellow.
- **Code blocks stand out from the page**, in every theme. In `mono` especially the snippet had
  almost no background of its own, so it floated on the page; it now has a visible panel and edge,
  and its comments, strings and keywords are properly separated.
- **The chat list runs the full height of the window.** The message box used to stretch underneath
  it, leaving an empty corner at the bottom left.
- **The colours were redone, all four themes.** The light theme in particular was flat — its
  surfaces were so close together that the window read as one sheet held together by hairlines,
  which is what "too light" was actually describing. Every theme now has clearly separated
  foreground and background layers.
- **Buttons respond to being pressed.** Hover, press and focus states throughout, including in the
  two dialogs, which previously used none of the app's styling at all.
- **Your messages sit on a card, replies sit on the page**, so you can find your own question in a
  long conversation at a glance.
- Replies are held to a comfortable reading width instead of stretching the full window, and text
  has more room to breathe.

### Fixed

- The primary button lost its colour when you hovered over it.
- "Erase everything…" stopped looking dangerous at the exact moment you pointed at it.
- The confirmation dialog ignored your theme and always showed dark colours, with a dark red
  "Close" button on the About box.
- The status bar showed dark-theme colours in the light theme, and put colour into `mono`, whose
  entire purpose is not to have any.
- The button for dismissing an error message was the smallest control in the app.
- "Try again" added a second copy of your message instead of replacing the one that failed.
- The Copy button inside a code block was a different size from the one on a reply.
- Hiding the chat list left an empty strip where it had been, instead of giving the space back to
  the conversation.
- A command typed or pasted with a leading space — or an invisible character left behind by a paste
  from a file or a web page — was not recognised, and got sent to the model as a message instead.
- Switching theme left the status bar's colour from the previous theme until the next message.
- The message box and the status bar sat 236px to the left of the conversation, tucked under the
  chat list, instead of lining up with the replies above them.
- Copy copied the *most recent* reply rather than the one you were looking at, so copying an older
  answer silently gave you a different message.

## [0.3.0] — 2026-08-03

### Added

- **A windowed app.** `OpenKeyApp.exe` ships alongside the console from the same release: the same
  chat, the same models, the same saved conversation, in a normal window. Code blocks are syntax
  highlighted and have their own copy button, text is selectable with the mouse, and there are
  buttons for new chat, retry, copy, export, model choice, theme and about.
- Four colour themes in both surfaces — default, dark, light and mono — remembered between runs and
  shared between the console and the app.
- Keyboard shortcuts in the app: Enter sends, Shift+Enter adds a line, Esc stops a reply, Ctrl+L
  clears the chat.
- **Clear can be undone.** Clearing a chat offers an Undo for as long as you haven't sent anything
  new, so a misclick doesn't cost you the conversation.

### Changed

- The app's "New chat" button is now "Clear". It never started a new conversation alongside the old
  one — it ended the only one there is — and the old label implied otherwise.
- Theme, About and Erase everything moved into a settings menu, leaving the toolbar for things that
  act on the conversation. Erase now sits alone at the bottom of that menu, away from Export.
- The model picker has an **Automatic** option again, so you can hand the choice back to OpenKey
  after picking a specific model.

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

- **A mistyped or revoked key was accepted and saved.** OpenKey checked keys against an endpoint
  that does not require one, so any text passed — and then every message failed, with advice that
  led back to the same place. Keys are now genuinely verified before being saved.
- **Piped or scripted input crashed the app.** Anything that needed a menu — choosing a model,
  confirming an erase, first-run setup — closed OpenKey with an error when input didn't come from
  a keyboard. Those now fall back to typing a number or a word.
- Choosing a model said the choice lasted "until you close OpenKey"; it is remembered.
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

[Unreleased]: https://github.com/corecompiled/OpenKey/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/corecompiled/OpenKey/releases/tag/v0.3.0
[0.2.1]: https://github.com/corecompiled/OpenKey/releases/tag/v0.2.1
[0.2.0]: https://github.com/corecompiled/OpenKey/releases/tag/v0.2.0
[0.1.0]: https://github.com/corecompiled/OpenKey/releases/tag/v0.1.0
