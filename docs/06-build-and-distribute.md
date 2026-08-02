# 06 — Build and Distribute

Producing the click-and-play `OpenKey.exe`.

## Publish command

From `C:\Users\Patron\OpenKey\`:

```cmd
dotnet publish src\OpenKey\OpenKey.csproj -c Release -r win-x64 -o publish\
```

**Every publish flag lives in `OpenKey.csproj`.** Don't pass them here and don't document a
different command anywhere else. They used to exist only as prose in the README, which meant any
publish that didn't paste that exact line — including CI — silently produced a *different artifact*
than the one that had been smoke-tested. Changing how the binary is built is a project-file edit,
reviewed like any other.

For Windows on ARM, swap the RID:

```cmd
dotnet publish src\OpenKey\OpenKey.csproj -c Release -r win-arm64 -o publish\
```

## Release naming convention

Match the house style used across CoreCompiled repos (e.g. [SnagLite](https://github.com/corecompiled/SnagLite/releases)):

- **Release title = the tag, version only** — `v0.1.0`. No app name, no "Phase X", no tagline in the title.
- **Body structure**: one-line pitch → `## What's in this release` (bullet the downloadable assets) → `## Quick start` → a `Full docs: [README](https://github.com/corecompiled/OpenKey#readme)` link.
- **Never** reference internal phase numbering in any public surface (release page, commit messages, README).

Output: `C:\Users\Patron\OpenKey\publish\OpenKey.exe`

That single file is the entire deliverable. Copy it to a USB stick, give it to a friend, double-click — done.

## Flag rationale

All set in `OpenKey.csproj`, not on the command line.

| Setting | Why |
|------|-----|
| `SelfContained` | Bundles the .NET runtime. The user doesn't need .NET installed — this is what makes it click-and-play. |
| `PublishSingleFile` | One file instead of a folder of DLLs. |
| `IncludeNativeLibrariesForSelfExtract` | Native libraries bundled and extracted to a temp dir at runtime. Required for a genuine single file. |
| `EnableCompressionInSingleFile` | Roughly 30% smaller, at the cost of a one-time decompression on first launch. |
| `PublishReadyToRun` | Pre-jits IL so startup feels instant. Larger file. |
| `IsAotCompatible` | Turns the trim/AOT analyzers on. Doesn't change the output; keeps the option open by failing the build on new reflection. |
| `RuntimeIdentifiers` | `win-x64;win-arm64`. |
| `InvariantGlobalization=false` | LLM replies are full of non-ASCII text. Costs ICU in the bundle; a deliberate trade. |

## Why NOT trimming

```
# DO NOT add:
-p:PublishTrimmed=true
```

Trimming is not enabled yet. The trim analyzers *are* on (`IsAotCompatible` is set on all three
projects) and the tree builds warning-clean, so the historical objection — that Spectre.Console's
internal reflection would silently break the UI — no longer applies unexamined. Enabling
`PublishTrimmed` now needs measurement rather than argument.

## Why NOT AOT (yet)

```
# Not enabled today:
-p:PublishAot=true
```

**The reason originally given here has expired.** This section used to say Spectre.Console's
reflection blocked AOT. Measured from the shipped assemblies: `IsTrimmable` metadata is **absent**
in Spectre.Console 0.49.1 and **present** in 0.55.2. The library did the work.

The other stated blocker, reflection-based `System.Text.Json`, is also gone — everything persisted
and every request body now goes through source-generated contexts.

Current status:

| Concern | State |
|---|---|
| Spectre.Console | Annotated trim/AOT-compatible since 0.55 |
| `System.Text.Json` | Source-generated contexts in place |
| Markdig | No analyzer warnings at our call sites |
| DPAPI via `ProtectedData` | AOT-safe |
| `Microsoft.Extensions.DependencyInjection` | Fine — composition is explicit, no assembly scanning |

So what remains is measurement, not a known obstacle. The prize is real for a USB-distributed app:
roughly 42 MB → 15–20 MB, no extract-to-temp on first run, and faster startup. Tracked in
[`../BACKLOG.md`](../BACKLOG.md); rationale in
[`architecture/08-decisions.md`](architecture/08-decisions.md).

## Expected output

| Metric | Value |
|--------|-------|
| File size | ~30–50 MB (compressed) |
| First launch cold-start | ~1–2 s (decompress + R2R) |
| Subsequent launches | <500 ms |
| Working set RAM | ~80–120 MB |

## Versioning

Edit `Directory.Build.props` at the repo root — version is shared by every project:

```xml
<Version>0.1.0</Version>
<InformationalVersion>0.1.0+$(GITCOMMIT)</InformationalVersion>
```

Print in banner:

```csharp
var ver = typeof(Program).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? "dev";
AnsiConsole.MarkupLine($"[bold cyan]OpenKey[/] [grey]v{ver}[/]");
```

SemVer:
- `1.0.0` — reserved for the first stable release; shipping versions so far are `0.x`
- `1.1.0` — Phase 1.1 QoL
- `1.2.0` — Phase 1.2 QoL
- `2.0.0` — GUI (Phase 2)

## Smoke test checklist (manual, run after every publish)

`dotnet test` covers the engine, provider, storage and streaming logic. This list is only for what
automation can't reach: a real terminal, a real browser, and a real double-click.

**Setup**

1. [ ] Launch shows the banner with a clean version (no `+<sha>` suffix)
2. [ ] First run offers: sign in with browser, or paste an existing key
3. [ ] During the browser wait, the hint to press `P` to paste or `Esc` to cancel is visible
4. [ ] Pressing `P` drops to the paste prompt within the same attempt
5. [ ] Pressing `Esc` returns to the menu
6. [ ] Browser path completes and the console reports the key was saved
7. [ ] Paste path: an invalid key shows a card explaining what to do, and re-prompts
8. [ ] If Windows Defender Firewall prompts, allowing "Private networks" only is sufficient
9. [ ] With port 3000 held by another app, OpenKey says so and offers paste in the same attempt

**Chatting**

10. [ ] Send "hello" → a `Thinking` spinner until the **first token**, then a
    `OpenKey AI · <model> · <elapsed>` header and text streaming in
11. [ ] Ask for a reply containing a code fence and a list → the fence renders as a bordered panel,
    the list renders with bullets, with one blank line between blocks
12. [ ] **A reply longer than the window scrolls normally and never erases earlier conversation**
13. [ ] Ctrl+C mid-reply stops that reply — **and the next message still works**
14. [ ] Resize the terminal mid-reply → output may lose styling on the block in flight, but earlier
    conversation is untouched
15. [ ] Paste a multi-line snippet containing a line starting with `/` → sent as one message, and
    the `/` line does **not** execute

**Commands and state**

16. [ ] `/help`, `/about`, `/model`, `/models`, `/cls` all render correctly
17. [ ] `/models` shows model names and context sizes, not raw ids
18. [ ] Close and relaunch → the last turns are shown in grey and the conversation continues
19. [ ] `/reset` states that it erases the conversation as well as the key, defaults to no, and
    re-runs setup in-process
20. [ ] Forced rotation: set a far-future `cooldownUntil` for the first model in
    `rotation.state.json` → the next message uses another model, shows one grey
    "Moved past N busy models." line, and the reply appears **once**
21. [ ] `/quit` exits cleanly

**Environment**

22. [ ] Run in **legacy `conhost.exe`** as well as Windows Terminal → no glyph renders as a box,
    including the spinner
23. [ ] `OpenKey.exe > out.txt` → plain text, no escape sequences, and the app does **not** exit
    immediately
24. [ ] **Double-click the exe** → on `/quit` and on any fatal error the window waits for a keypress
    instead of vanishing

## Antivirus / SmartScreen notes

Unsigned single-file .NET exes commonly trigger:
- Windows Defender SmartScreen "unrecognized app" warning on first launch (user clicks "More info → Run anyway")
- Occasional false-positive heuristic hits on first scan

Mitigations (Phase 1.2+):
- Code-sign with a certificate (Azure Trusted Signing or a purchased cert)
- Submit the exe to Microsoft Defender for whitelisting (free)
- Eventually publish via winget/Scoop

For Phase 1, ship unsigned + a one-line note in README that SmartScreen may warn.

## USB distribution checklist

1. Build via the publish command above
2. Run smoke test on the build machine
3. Copy `publish\OpenKey.exe` to USB drive root (or any folder)
4. Plug into a second Windows 10/11 machine
5. Run `OpenKey.exe` directly from USB
6. First-run prompt appears — state is per Windows user, so a second machine starts fresh
7. User pastes *their own* OpenRouter key → saved to *their* `%APPDATA%` on *that* machine
8. Verify chat works end-to-end

Note: the USB drive itself stores nothing user-specific. All state lives in `%APPDATA%` of whichever Windows user runs the exe. This is intentional: see [`05-persistence-and-reset.md`](05-persistence-and-reset.md).

## CI

Implemented — see `.github/workflows/ci.yml` (build, test, and a publish check on every push)
and `.github/workflows/release.yml` (both architectures attached to a `v*` tag). The sketch that
used to live here has been replaced by the real thing.

For reference, the shape is:

```yaml
name: build
on: [push]
jobs:
  publish:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: dotnet publish src/OpenKey/OpenKey.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=true -o publish
      - uses: actions/upload-artifact@v4
        with: { name: OpenKey-exe, path: publish/OpenKey.exe }
```

Tag-driven releases come in Phase 1.2 with the update checker.
