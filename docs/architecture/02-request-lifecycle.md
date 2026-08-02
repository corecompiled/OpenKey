# Request lifecycle

One message, keystroke to rendered reply.

```
ReadUserLine ──► CommandRouter ──► ChatEngine.SendAsync ──► OpenRouterProvider
                      │ handled          │  retry loop            │ SSE
                      ▼                  ▼                        ▼
                  (command)      TranscriptWriter  ◄──────── ChatChunk
```

## 1. Reading input

`ConsoleHost.ReadUserLine` uses `Console.ReadLine`, not a Spectre prompt. Three reasons, all
practical: the Windows console reader supplies arrow keys, Home/End, word jump and F7 history that
Spectre's reader does not implement; it does not throw when output is redirected, which Spectre
prompts now do; and it leaves the remainder of a multi-line paste in the driver buffer.

That last point matters. Console paste arrives as synthetic keystrokes and a newline reads as
Enter, so the first line submits and the rest lands in the *next* prompt. If one of those lines
begins with `/`, it executes as a command — a pasted transcript containing `/reset` could open the
wipe confirmation with the following line answering it. So after reading a line, OpenKey checks
whether input is already buffered: a human cannot type the next line within milliseconds, so
buffered input means paste, and the remainder is drained and joined into one message.

## 2. Command or message

`CommandRouter.HandleAsync` returns `NotACommand` immediately for anything not starting with `/`.
Commands are handled entirely in the host — Core has no notion of them.

## 3. The retry loop

`ChatEngine.SendAsync` is an async iterator. The user turn is appended once, before the loop, and
the whole loop is wrapped in `try`/`finally` — legal around `yield`, unlike `try`/`catch` — so that
a failed or cancelled turn removes its own user message instead of leaving it to be persisted on
the next success.

Up to `MaxAttempts` (5) times:

1. Fetch free models from `IModelCatalog`. An empty list breaks out with a `ChatException` rather
   than reaching `PickAsync`, which used to throw an `InvalidOperationException` that nothing
   caught.
2. Move the pinned model to the front, if one is pinned.
3. `IRotationPolicy.PickAsync` chooses the first model not on cooldown.
4. On attempts after the first, emit a chunk with `IsAttemptRestart` set — see below.
5. Trim history to fit the model's context.
6. Open the provider stream and relay chunks.

### The restart signal

When an attempt fails part-way, text from it has already reached the consumer. Without a signal,
the console would concatenate the abandoned attempt and the retry, showing the answer twice while
the saved history held it once — screen and history permanently disagreeing.

So the engine emits `ChatChunk` with `IsAttemptRestart = true` before any text from the new
attempt. `TranscriptWriter.Reset()` erases what was drawn. Providers never set this flag; it is
purely engine-to-consumer.

### Committing before the last yield

Success bookkeeping — `MarkSuccess`, appending the assistant turn, saving the session — happens
**before** the final chunk is yielded.

This ordering is not stylistic. A consumer that stops as soon as it sees `IsFinal` — the natural
way to read this stream, and what `ConsoleHost` does — disposes the iterator at that `yield`.
Anything after it never runs. With the commit placed afterwards, no conversation was ever written
to disk and no model success was ever recorded, in any build up to that point. Draining the
sequence to completion hides the bug entirely, which is why the regression test deliberately breaks
early.

## 4. Streaming out

`OpenRouterProvider.StreamChatAsync` reads SSE lines and yields `ChatChunk`s. Each read carries its
own deadline rather than the whole response sharing one — see
[`05-provider-layer.md`](05-provider-layer.md).

## 5. Rendering

`ConsoleHost.SendAndRenderAsync` runs in two phases.

**Phase one** shows a spinner until the first text arrives. The enumerator is created *outside* the
`Status().StartAsync` callback so it survives the handoff; the callback simply returns once it has
a chunk with text.

The reason for a hard handoff: writing beneath a running spinner works, but only for whole lines.
Spectre's live region repositions the cursor to column 0 before each repaint, so per-token writes
get overwritten. The spinner has to be torn down before streaming starts.

**Phase two** prints the reply header — model id and elapsed time, known before the first byte, so
the user never stares at nothing — then any rotation note, then hands every chunk to
`TranscriptWriter`.

## 6. Block-level rendering

`TranscriptWriter` streams raw text as it arrives and, each time a markdown block completes, erases
those rows and repaints them styled. The still-arriving tail stays plain: styled means settled, raw
means still coming.

Details, including the three cases where it refuses to erase, are in
[`06-console-host.md`](06-console-host.md).

## Cancellation

One `CancellationTokenSource` per turn, held in a field the Ctrl+C handler reads. Mid-turn, Ctrl+C
cancels that reply; at the prompt, it exits.

The previous design used a single process-lifetime source. Once cancelled it stayed cancelled, so
every subsequent message was born already cancelled and the session was unusable until restart.

The catch is filtered on `turnCts.IsCancellationRequested` so that an upstream deadline is not
mislabelled as "you cancelled this".
