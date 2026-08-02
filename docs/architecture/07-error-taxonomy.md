# Error taxonomy

`ChatErrorKind` is a contract surface — see
[`../01-architecture.md`](../01-architecture.md#error-taxonomy). Adding a member propagates to every
implementation, including future ports.

Everything a provider can fail with maps onto one of these, and rotation is driven entirely by
them.

## The full table

| Kind | Cause | Retry elsewhere? | Model's fault? | Cooldown |
|---|---|---|---|---|
| `TransientRateLimit` | HTTP 429 | yes | yes | 60s base, honours `Retry-After` |
| `TransientServer` | 5xx, 408, stalled stream | yes | yes | 30s base |
| `MalformedResponse` | Unparseable payload | yes | yes | 10s base |
| `NetworkDown` | No route, DNS, captive portal | **no** | **no** | none |
| `AuthFailure` | 401, 403 | no | n/a | permanent |
| `QuotaExhausted` | 402 | no | n/a | n/a |
| `InvalidRequest` | 400, 404, 422 | **no** | n/a | n/a |

Two columns, not one, because "should we try another model?" and "is this model to blame?" are
different questions. Conflating them is what produced the two worst behaviours this taxonomy now
prevents.

## Why `NetworkDown` answers no to both

When there is no route to OpenRouter, every model fails identically. Rotating burns all five
attempts and puts eight models on cooldown for an outage none of them caused — so OpenKey stays
broken *after* the network comes back, which is the part users actually notice.

It fails fast instead, blames nobody, and says to check the connection.

## Why `InvalidRequest` exists

Before it, a request no model could accept — context overflow, a model id that no longer exists —
mapped to `MalformedResponse`, which is retryable. So OpenKey resent an identical, impossible
request to five models in turn and cooled down every one of them.

The request is at fault, not the model. Retrying unchanged cannot succeed.

## What the user sees

Copy lives in `ConsoleHost.ShowChatError`. Enum names never reach the screen; every card names a
next step.

| Kind | Card | Next step |
|---|---|---|
| `AuthFailure` | Danger — "Your key was refused" | `/reset` to sign in again, *and* that it erases history |
| `QuotaExhausted` | Danger — "This key is out of credit" | Add credit, or wait for the allowance to renew |
| `InvalidRequest` | Danger — "The model refused this message" | Shorter message, or `/models` |
| `NetworkDown` | Warn — "Can't reach OpenRouter" | Check the connection and resend |
| `TransientRateLimit` | Warn — "Every free model is busy right now" | Wait and resend, or `/models` |
| other | Warn — "That didn't go through" | Resend, or `/models` |

Danger means it will not fix itself. Warn means it might. That distinction is the entire reason
there are two card styles — a wall of red reads as panic and stops carrying information.

## Adding a kind

1. Update `ChatErrorKind` and [`../01-architecture.md`](../01-architecture.md).
2. Decide `IsTransient` and `IsModelFault` **separately**.
3. Map it in `OpenRouterProvider.MapHttpError`.
4. Add copy in `ShowChatError`, with a next step.
5. Add a cooldown in `RotationPolicy.MarkFailure` if it is a model fault.
6. Update every other implementation — this is a contract change and needs approval first.
