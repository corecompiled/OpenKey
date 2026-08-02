# Console host

The console is a product surface, not a debug view. A non-technical person double-clicks an `.exe`
off a USB stick; everything below follows from that.

## Where things live

| File | Responsibility |
|---|---|
| `Ui/Theme.cs` | Every colour, as Spectre style names |
| `Ui/Glyphs.cs` | Every non-ASCII character, tiered |
| `Ui/ConsoleLayout.cs` | Startup: encoding, width, capabilities |
| `Ui/Components.cs` | Every visible element |
| `Ui/TranscriptWriter.cs` | Streaming reply rendering |
| `Ui/TextWidth.cs` | Terminal cell measurement |
| `MarkdownConsoleRenderer.cs` | Markdown to Spectre |
| `ConsoleHost.cs` | REPL, first run, cancellation |
| `CommandRouter.cs` | Slash commands |

**A colour or glyph literal outside `Theme` or `Glyphs` is a defect.** Styling used to happen at
call sites and drifted into three different letter cases and four border styles, sometimes inside a
single method.

## Colour

> One accent, one neutral, three signals.

Colour carries meaning, never decoration. Body text is never coloured — it inherits the terminal's
own foreground, because hardcoding white breaks every light-background console.

| Role | Style | For |
|---|---|---|
| Brand | `aqua` | Wordmark, AI label, caret, commands, model ids |
| Strong | `bold` | Names: username, headers, markdown strong |
| Muted | `grey` | Hints, elapsed, borders, secondary detail |
| Ok / Warn / Danger | `green` / `yellow` / `red` | The glyph and card chrome only |

Only the 16 base ANSI colours are used. Legacy conhost downsamples anything richer, and the nicer
greys (`grey19`, `grey23`) land on black or silver depending on the user's scheme — so they either
vanish or invert.

That restriction is also why there is no capability-degradation machinery here: with a base-16
palette there is nothing to downsample, and Spectre strips colour itself under `NO_COLOR`. A more
elaborate theme object would be solving a problem this palette does not have.

No background colours. Inline code used to be `white on grey23`, which downsampled to white-on-black
— invisible on light schemes, identical to body text on dark ones. The one style meant to make code
stand out did nothing.

## Glyphs

Windows Terminal has font fallback and renders anything. The GDI-rendered legacy conhost does not:
a glyph missing from Consolas draws a box. So the tier is chosen conservatively — full Unicode only
when the codepage is UTF-8 *and* `WT_SESSION` is set.

| Role | Unicode | ASCII | Note |
|---|---|---|---|
| Caret | `❯` | `>` | In neither CP437 nor Consolas |
| Ok / Fail | `✓` `✗` | `+` `x` | Not in CP437 |
| Spinner | Braille dots | `SimpleDots` | **Ran on every single turn** |
| Borders | Square | Square | Rounded is outside CP437 |
| Bullet | `-` | `-` | A hyphen is calmer *and* safer than `•` |

The spinner was the most likely visible breakage in the whole app: Braille U+28xx, absent from
Consolas, displayed on every message.

## Streaming

`TranscriptWriter` writes raw text as it arrives; when a markdown block completes it erases those
rows and repaints them styled. The unstyled tail is intentional — styled means settled, raw means
still arriving.

### Why not `LiveDisplay`

It is the obvious tool and it is wrong here. Reading Spectre 0.57.2's `LiveRenderable`:

- The region is clamped to the viewport, and overflow lines are **discarded**, not scrolled.
- On shrink it issues `EraseInDisplay(2)` followed by `ClearScrollback()` — which would erase the
  conversation.
- Cursor control is dropped entirely when output is redirected, and unlike `Status`/`Progress` it
  has no fallback renderer, so every frame appends.

For a scrolling transcript, destroying scrollback ends the discussion.

### The rewind

`\r`, `CursorUp(n)`, `EraseInDisplay(0)` — emitted through `ControlCode` so it stays inside
Spectre's capability gate. Never `EraseInDisplay(2)`, never `ClearScrollback`, and always relative
`CursorUp` rather than an absolute position, which lands wrong if the region straddled a scroll.

It **refuses to rewind** in three cases, leaving the raw text in place instead:

1. **Taller than the viewport** — those rows are already in scrollback, where no escape sequence
   reaches. Erasing what remains would tear the output in half.
2. **The terminal was resized** — the row count is stale, and a wrong rewind eats unrelated
   transcript above.
3. **No ANSI, or output redirected** — nothing can be erased, so nothing is streamed raw either;
   the styled block render is the only output.

Losing a restyle is cheap. Eating the conversation is not.

Code fences are buffered rather than streamed raw, since they are the most likely block to outgrow
the viewport.

### Counting rows

`TextWidth` measures terminal cells, not characters. CJK and emoji occupy two, combining marks and
joiners occupy none, and a surrogate pair is two chars but one glyph. Getting this wrong makes the
rewind erase the wrong number of rows — which damages the transcript above, so it is worth the
care.

Wrapping is computed at `width - 1` because terminals disagree about a glyph landing exactly on the
last column: conhost wraps immediately, Windows Terminal defers. Staying a column short agrees with
both.

Spectre has an equivalent calculator, but it is `internal` in 0.57.2.

## Spinner handoff

Writing beneath a running spinner works — but only for whole lines, because the live region
repositions the cursor to column 0 before each repaint. Per-token writes get overwritten.

So phase one runs the spinner until the first text arrives, with the enumerator created *outside*
the callback so it survives; phase two streams freely after teardown.

## Capabilities

`ConsoleLayout.Rich` is `caps.Ansi && caps.Interactive`. Both halves matter: Spectre 0.55 disables
ANSI when stdout is redirected, and makes `Interactive` false if *any* standard stream is
redirected — at which point its prompts throw. That throw used to land in a bare `catch` and exit
the REPL silently.

Width is clamped to `[60, 100]`. Without the cap, a maximized 200-column terminal stretches a
three-line snippet across the whole screen and no two machines render a reply the same way.

## Voice

Sentence case. No `DPAPI`, `OAuth`, `PKCE`, `429`, or `ChatErrorKind` anywhere a user can see —
those were the most developer-tool-looking thing in the app and told the user nothing actionable.

Every error is a card with two parts: what happened, and what to do next. **A card with no next
step is a bug**, because it leaves someone at a dead end whose only escape is closing the window.

`/reset` states that it erases the conversation as well as the key *before* asking to confirm, and
never defaults to yes.

## Exit hold

If `GetConsoleProcessList` reports OpenKey is the console's only client, it was double-clicked and
the window dies with the process. In that case OpenKey waits for a keypress before exiting.

Without this, the goodbye line and every fatal error were unreadable by construction — nobody in
the target audience had ever seen either.
