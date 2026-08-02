# Decisions

What was chosen, what was rejected, and the evidence. Written so that reasonable-looking ideas that
turn out to be wrong don't get re-proposed every six months.

---

## Keep `IChatProvider`; do not adopt `IChatClient` as the contract

**Status:** decided · **Revisit:** Phase 3

`Microsoft.Extensions.AI` (stable) offers `IChatClient`, which covers roughly what `IChatProvider`
covers and adds tool-calling middleware — useful for Phase 3 — plus a broad provider ecosystem,
useful for Phase 5.

It is nonetheless rejected **as the contract**, because `IChatProvider` is normative across a
planned browser PWA and Android app. `IChatClient` is .NET-only; a TypeScript port cannot implement
it. Adopting it as the boundary breaks the cross-UI guarantee that is the reason the contract docs
exist at all.

The move that keeps both benefits is to implement `IChatProvider` **over** an `IChatClient` inside
the provider project only. The portable contract survives, .NET gets the middleware, and no other
layer notices. That is the backlog entry.

---

## `LiveDisplay` is not usable for the transcript

**Status:** decided, with evidence · **Revisit:** if Spectre changes the overflow model

The obvious tool for streaming output, and wrong. From Spectre 0.57.2's `LiveRenderable`:

- Height is clamped to the viewport and overflow lines are **discarded**, not scrolled.
- On shrink it emits `EraseInDisplay(2)` then `ClearScrollback()` — it would erase the conversation.
- With output redirected, cursor control is dropped and every frame appends. Unlike
  `Status`/`Progress` it has no fallback renderer.

`TranscriptWriter` drives the cursor directly instead, and declines to erase in the three cases
where erasing would be wrong. See [`06-console-host.md`](06-console-host.md).

---

## No `IHttpClientFactory`, no Polly

**Status:** decided · **Revisit:** never, unless rotation is redesigned

Resilience here is **model-level**, not transport-level. `RotationPolicy` counts failures per model
and schedules cooldowns from them.

A transport retry policy fires underneath that: the provider silently retries, the engine sees one
outcome instead of three, failure counts and cooldowns no longer describe reality, and rotation
starts making decisions on corrupted data. The two mechanisms cannot both own retry.

`HttpClient` is a singleton with an infinite timeout — see
[`05-provider-layer.md`](05-provider-layer.md) for why the timeout must not be finite.

---

## No `Microsoft.Extensions.Hosting`

**Status:** decided

`docs/02-phase1-build.md` originally prescribed it. A REPL needs no generic host, no hosted-service
lifetime, and no configuration binding. `Program.cs` composes about a dozen services explicitly.

The code was right and the doc was wrong; the doc has been corrected.

---

## Hand-rolled SSE reader

**Status:** decided · **Revisit:** when `System.Net.ServerSentEvents` ships stable

`System.Net.ServerSentEvents` exists but is preview-only (`11.0.0-preview.6` at time of writing).
A preview dependency in a click-and-play binary is not worth ~40 lines of line-reading, especially
when those lines also carry the per-read deadline logic.

---

## `System.Text.Json` source generation

**Status:** decided

Reflection-based serialization is neither trim- nor AOT-safe, and it silently pulls in machinery
that never appears in a build log until AOT is attempted.

Enabling `IsAotCompatible` surfaced the reflection sites immediately as build errors. Anonymous
request bodies became named records; persisted shapes moved to generated contexts.

One trap is documented in [`04-storage-and-crypto.md`](04-storage-and-crypto.md): a type with two
constructors makes STJ throw `NotSupportedException`, which is not a `JsonException` and therefore
escapes corruption handling. It meant session restore had never worked.

---

## NativeAOT: adopted

**Status:** decided, measured

This document previously recorded AOT as "watching", because the stated blocker — Spectre.Console's
internal reflection — had expired without anyone checking. Measured from the shipped assemblies,
`IsTrimmable` is absent in Spectre 0.49.1 and present in 0.55.2.

Measured rather than argued, via `.github/workflows/aot-trial.yml`:

| | Single-file | NativeAOT |
|---|---|---|
| Size | 43 MB | **10.8 MB** |
| Startup | ~1–2 s cold | **~0.16 s** |
| Extract to temp on first run | yes | **no** |

Every one of those differences matters specifically because this is software copied onto a USB
stick and run on a machine that has nothing installed.

Verified on both architectures in CI, and the x64 binary run locally against a live model:
streaming, markdown, tokenizer, DPAPI and config persistence all work compiled. Markdig, the one
library whose AOT behaviour was unverified, is fine.

**The cost** is that `dotnet publish` now needs the MSVC linker from the Desktop C++ workload.
`build`, `test` and `run` do not, so day-to-day work is unchanged. That was judged an acceptable
price for a 4× smaller, instantly-starting binary — but it is a real cost, and it falls on anyone
who wants to produce a release build.

The trial workflow stays in the repo so this can be re-measured rather than re-litigated.

---

## Base-16 palette instead of a degradable theme object

**Status:** decided

A richer design — a theme record degraded once against live capabilities — was considered. It
solves problems this palette does not have: with only the 16 base ANSI colours there is nothing to
downsample, and Spectre strips colour itself under `NO_COLOR`.

Glyphs and borders are the real degradation axis, and they are handled by a tier check in
`Ui/Glyphs.cs`. The simpler design is the correct one here; the elaborate one would be machinery
without a job.

---

## `Console.ReadLine` instead of a Spectre prompt for chat input

**Status:** decided

Spectre's reader handles Enter, Tab, Backspace and printable characters, and silently drops
everything else — including arrow keys. `Console.ReadLine` gets the Windows console's own line
editor free: arrows, Home/End, word jump, F7 history.

It also cannot throw when output is redirected, which Spectre prompts now do, and it leaves the
remainder of a multi-line paste in the driver buffer where it can be drained rather than executed
as commands.

`TextPrompt` is kept for key entry, where `.Secret()` masking is the whole point.

---

## The multi-provider seam is designed but unproven

**Status:** acknowledged

`IChatProvider.Id` and `DisplayName` are read by nothing. One provider exists. An abstraction with
a single implementation has not been tested against reality, whatever its shape suggests.

The backlog therefore sequences the **Anthropic** provider before the Claude Code subprocess
provider: it exercises the same seam more honestly, since a subprocess provider could be made to
work around a bad abstraction in ways an HTTP one cannot.
