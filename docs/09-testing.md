# 09 — Testing

```cmd
dotnet test
```

65 tests across two projects. xunit, no other test dependencies.

| Project | Covers | Target |
|---|---|---|
| `tests/OpenKey.Core.Tests` | Engine, rotation, storage | `net10.0` |
| `tests/OpenKey.Tests` | Provider, console UI, PKCE | `net10.0-windows` |

The split follows the platform boundary. Core's tests run wherever Core does; anything touching
DPAPI, the console, or Windows-only behaviour belongs in the second project.

Both reach `internal` members through `InternalsVisibleTo`, declared in the corresponding
`.csproj`.

## The two helpers everything is built on

**`FakeChatProvider`** makes attempts scriptable:

```csharp
provider.ThenFails(ChatErrorKind.TransientRateLimit)
        .ThenSucceeds("recovered");
```

Its absence is why `ChatEngine` — the entire retry and rotation state machine — had no coverage at
all before. It also records which models were called, which is how rotation order is asserted.

**`TempAppPaths`** implements `IAppPaths` over a temp directory and deletes it on dispose. Because
`IAppPaths` derives every filename from `RootDir` via default interface members, a substitute needs
exactly one property — so storage tests touch a real filesystem instead of mocking one.

## What is covered

**Engine** — success and persistence; rotation on transient failure; the attempt-restart signal;
`NetworkDown` and `InvalidRequest` not rotating; user-turn removal on failure and on cancellation;
empty catalog; pinned-model preference; and streams ending without a final chunk.

**Provider** — SSE framing, `[DONE]` without a `finish_reason`, in-stream error objects,
captive-portal HTML with HTTP 200, HTTP status mapping across nine codes, and free-model detection
including the `0.000000` form the old string comparison got wrong.

**Storage** — session round-trip with non-ASCII content, corruption quarantine, best-effort writes
when the target can't be written, and the rule that an empty free-model list is never cached.

**UI** — block splitting across delta boundaries, fences spanning many chunks, unterminated fences,
`Reset()` discarding an abandoned attempt, and cell-width measurement for CJK, emoji, surrogate
pairs and combining marks.

## Two conventions worth keeping

**Test the consumer's actual behaviour, not a convenient one.**
`PersistsTheTurnEvenWhenTheConsumerStopsAtTheFinalChunk` deliberately breaks out of the loop on
`IsFinal`, because that is what the console does. An earlier test drained the sequence to
completion and passed against genuinely broken code — persistence had never worked in any build,
and draining hid it completely.

**Set up the way the app does.** `BuildAsync` calls `ResumeAsync`, because that is what seeds the
system prompt. Skipping it tests an engine in a state the app never reaches.

## Testing the console

`AnsiConsole.Create` with an `AnsiConsoleOutput` over a `StringWriter` gives a console you can
assert against:

```csharp
var output = new StringWriter();
var console = AnsiConsole.Create(new AnsiConsoleSettings
{
    Ansi = AnsiSupport.No,
    ColorSystem = ColorSystemSupport.NoColors,
    Out = new AnsiConsoleOutput(output),
});
```

With ANSI off, `TranscriptWriter` streams no raw text and emits no cursor movement, so what remains
is exactly the styled block output — which is what the assertions are about.

This is also why the writer decides whether it may rewind from **its own console's capabilities**
rather than global state. It was originally global, which made it both untestable and wrong: the
fallback path called `Cursor.Move` and threw without a real console handle.

## Not covered

`ConsoleHost`'s REPL loop, the OAuth browser flow, `DpapiKeyStore` (needs a real Windows user
profile), and `CommandRouter` interaction. These need either a live terminal or a network round
trip; they are covered by the manual checks in
[`06-build-and-distribute.md`](06-build-and-distribute.md) instead.

If you touch the console, run it and look at it — in Windows Terminal *and* in `conhost.exe`.
Several defects here were invisible in review and obvious on screen.
