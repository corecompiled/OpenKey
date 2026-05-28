# CLAUDE.md — OpenKey project rules

Meta-rules for any Claude session in this repo. Read top-to-bottom on every fresh session.

## North star

OpenKey is a click-and-play chat client for free OpenRouter LLMs. Desktop `.exe` is the **default minimum deliverable**. PWA and Android come later and reuse the same contract docs. Full overview: [`docs/00-overview.md`](docs/00-overview.md).

## Working rules for Claude

1. **95% confidence rule.** Do not proceed with any plan until you are at least 95% confident. Ask unlimited clarifying questions before that threshold is met. This applies to **every** planning session, not just the first. If unsure, ask — never guess.
2. **Plan-then-execute.** Always plan in plan mode, get user approval via `ExitPlanMode`, then implement. No surprise code.
3. **Read docs first.** Before any work, read `docs/00-overview.md` and the doc most relevant to the task. Don't re-derive decisions that already live in `docs/`.
4. **Never break the contract.** Contract surfaces:
   - `IChatProvider` interface
   - Error taxonomy enum (`ChatErrorKind`)
   - `%APPDATA%\OpenKey\` schema (filenames + JSON shapes)
   - OpenRouter wire format documented in `docs/03-openrouter-integration.md`
   - Rotation rules in `docs/04-model-rotation.md`

   Changes to any contract surface require explicit user approval **and** synchronized updates to all impls (currently C#; later TS PWA, Android).
5. **Doc hygiene on every MD update** (see "Doc hygiene checklist" below).

## Phase priority

- **Desktop exe (Phase 1) ships first.** Don't propose work on later phases until the Phase 1 acceptance checklist in `docs/02-phase1-build.md` is fully green.
- Within a phase, finish all listed items before moving on.
- **Quick Wins** (in `docs/07-roadmap.md`) can be picked off any time, but never replace blocked priority work.

Phase order:

| Phase | Surface |
|-------|---------|
| 1 / 1.1 / 1.2 | Desktop console exe |
| 2 | Desktop GUI (Avalonia) |
| 3 | Tool use / function calling |
| 4 | Local RAG |
| 5 | Claude Code + Anthropic providers |
| 6 | PWA |
| 7 | Android APK |

## Auto-sort behavior for new ideas

When the user mentions a new feature / QoL / roadmap item, slot it into existing docs:

1. **If it extends an existing phase**, insert into that phase's section in `docs/07-roadmap.md`.
2. **If it's structurally new**, add a new section in `docs/07-roadmap.md` at the correct tier position.
3. **If it doesn't fit any tier cleanly**, add it to the **Quick Wins** flat list at the bottom of `docs/07-roadmap.md`.

Sort by the **tier ladder**:

| Tier | Meaning |
|------|---------|
| 1 | Exe-blocking (must ship for Phase 1) |
| 2 | Exe polish (Phase 1.1 / 1.2) |
| 3 | Current-UI features within an existing host |
| 4 | New providers (no UI change) |
| 5 | New UI surfaces (GUI, PWA, Android) |

New items slot into the **lowest tier they legitimately belong to**, ordered within tier by user value. **One canonical home per item** — never silently duplicate across docs.

## Cross-UI guarantee

All UIs (desktop console, future Avalonia GUI, future PWA, future Android APK) implement the **same** behavior defined in the contract docs.

- **Contract docs** (normative): `01-architecture.md` (error taxonomy + `IChatProvider`), `03-openrouter-integration.md`, `04-model-rotation.md`, `05-persistence-and-reset.md`.
- **Reference impl**: C# desktop. Other impls are ports.
- **Storage** is platform-translated, but field names and semantics stay identical:

| Surface | Storage primitive |
|---------|-------------------|
| Desktop (Windows) | `%APPDATA%\OpenKey\` + DPAPI for key |
| PWA (browser) | `IndexedDB` + Web Crypto (AES-GCM, passphrase-derived) for key |
| Android | App-private storage + Android Keystore for key |

Rule: any change to a contract surface updates the contract doc **first**, then propagates to all impls.

## Doc hygiene checklist (run on EVERY md update)

Before finishing any task that touches a `.md` file under this repo, walk this list:

1. **Redundancy scan.** Does any new content duplicate something already stated elsewhere? If yes → keep one canonical home (usually the more specific doc), link to it from the other, do **not** copy-paste.
2. **Consistency scan.** Does the new content contradict any existing doc (CLAUDE.md, `docs/00-overview.md`, contract docs, roadmap)? If yes → either update the other docs in the same task or stop and surface the contradiction to the user.
3. **Tier check.** If you added a roadmap/QoL item, is it in the correct tier per the ladder above? Is it in exactly one place?
4. **Scope check.** Did you only edit docs relevant to the current task? No drive-by edits. If a related doc needs a follow-up, note it explicitly to the user rather than silently editing.
5. **Contract check.** Did this edit touch a contract surface? If yes → confirm user approval is on file (in the plan) and all contract docs are aligned.
6. **Doc map check.** New file created? → Add to the "Doc map" table below and to `docs/00-overview.md` references where relevant. File removed/renamed? → Update all references.
7. **Tone check.** Docs stay terse and scannable. No marketing fluff, no apologies, no "comprehensive" / "robust" filler.

If any item fails, fix before reporting the task done. Surface unresolvable conflicts to the user — don't paper over them.

## Doc map

| File | Purpose | Contract? |
|------|---------|-----------|
| `CLAUDE.md` (this file) | Meta-rules for Claude sessions | no |
| `docs/00-overview.md` | Pitch, principles, phase ladder, glossary | no |
| `docs/01-architecture.md` | Layers, `IChatProvider`, error taxonomy, cross-UI contract | **yes** |
| `docs/02-phase1-build.md` | Step-by-step Phase 1 build walkthrough | no (impl guide) |
| `docs/03-openrouter-integration.md` | OpenRouter wire format | **yes** |
| `docs/04-model-rotation.md` | Rotation policy, cooldowns, retries | **yes** |
| `docs/05-persistence-and-reset.md` | `%APPDATA%` layout, DPAPI, `/reset` | **yes** |
| `docs/06-build-and-distribute.md` | `dotnet publish`, smoke test, USB distribution | no |
| `docs/07-roadmap.md` | Future phases, tier ladder, Quick Wins | no |

## Things never to do

- No paid OpenRouter models in any Phase 1.x release.
- No breaking changes to `IChatProvider` without explicit user approval **and** synchronized updates to all impls.
- No Core code depending on a UI host or a specific provider.
- No telemetry. No phone-home.
- No auto-update installer in any phase (check-and-notify only).
- No contradictions between docs. If you find one, fix it or surface it.
- No silent doc duplication. One canonical home per fact.
- No drive-by edits outside the current task's scope.
