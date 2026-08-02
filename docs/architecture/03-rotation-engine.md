# Rotation engine

Free models rate-limit constantly. Rotation is what makes that invisible: when one model refuses,
OpenKey moves to another and the user sees a reply rather than an error.

Normative rules live in [`../04-model-rotation.md`](../04-model-rotation.md). This explains the
design and the parts that are easy to get wrong.

## State

`RotationPolicy` keeps a `ModelState` per model id:

| Field | Meaning |
|---|---|
| `ModelId` | Key |
| `FailureCount` | Consecutive failures; drives backoff |
| `CooldownUntil` | Not usable before this instant |
| `LastErrorKind` | Why it was last cooled down |
| `LastUsedAt` | Last selection |

Persisted to `rotation.state.json` after each success or failure, so cooldowns survive a restart —
otherwise relaunching would immediately retry a model that just rate-limited you.

## Selection

`PickAsync` returns the first candidate whose cooldown has expired. Candidate order comes from the
catalog, with a pinned model moved to the front.

If everything is cooling down, it finds the soonest and either waits (when the wait is short) or
raises a rate-limit error telling the user roughly how long. An empty candidate list raises a
`ChatException` — deliberately not `InvalidOperationException`, because callers catch the former
and an empty list used to escape as an unhandled crash.

## Two separate questions

The subtle part of this design is that "should we retry?" and "is this model to blame?" are *not*
the same question, and treating them as one produces bad behaviour.

```
IsTransient(kind)   → should we try a different model?
IsModelFault(kind)  → should this model be penalised?
```

**`NetworkDown` answers no to both.** With no route to the provider every model fails identically.
Retrying burns all five attempts; cooling each one down punishes eight models for an outage none of
them caused — and leaves OpenKey still broken after the network returns. So a network failure stops
immediately, blames nobody, and says "check your connection".

**`InvalidRequest` answers no to the first.** A request the model cannot accept — context overflow,
unknown model id — cannot succeed by being sent somewhere else unchanged. Retrying it would cool
down every model in turn for the user's fault.

**`AuthFailure` and `QuotaExhausted`** are fatal for the key, not the model. No rotation helps.

Only `TransientRateLimit`, `TransientServer` and `MalformedResponse` rotate.

## Cooldowns

Base duration by kind, doubled per consecutive failure, capped at five minutes. `AuthFailure` is
effectively permanent — it will not succeed on retry, so there is no point scheduling one.

A `Retry-After` header, when present, raises the rate-limit cooldown to at least what the server
asked for. Ignoring it means being rate-limited again immediately.

## Context trimming

Before each attempt, `BuildMessagesForModel` drops the oldest non-system messages until the
estimated token count fits the chosen model's context, reserving room for the reply. The system
prompt is never dropped.

The estimate is `(role.Length + content.Length) / 4 + 4` per message. It is crude and deliberately
conservative — a real tokenizer is on the backlog. Trimming happens per attempt rather than once,
because rotation can land on a model with a very different context size.

## What the user sees

One grey line: *"Moved past 2 busy models."*

Not a yellow warning, not one line per attempt, and never a model id or an error-kind name.
Rotation working correctly is not a warning — it is the feature doing its job, and shouting about
it makes a working app look broken. The model that actually answered is already named in the reply
header.
