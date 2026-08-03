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

### Prerequisite: the C++ workload

OpenKey publishes as a NativeAOT binary, so `dotnet publish` needs the MSVC linker. Install once:

```cmd
winget install Microsoft.VisualStudio.2022.BuildTools --override "--quiet --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
```

For `win-arm64`, also add `Microsoft.VisualStudio.Component.VC.Tools.ARM64`.

Without it you get *"Platform linker not found"*. **`dotnet build`, `dotnet test` and `dotnet run`
are unaffected** — only publishing needs this.

A present `link.exe` does not mean the workload is installed: a Visual Studio install can leave a
compiler stub with no import libraries and no Windows SDK, which fails exactly the same way. Verify
what the compiler actually probes for rather than looking for the linker:

```powershell
& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -prerelease -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
```

Empty output means the workload is missing, whatever else is on disk. That command is the same
query `findvcvarsall.bat` in the `microsoft.dotnet.ilcompiler` package runs — note it already
passes `-prerelease`, so a preview Visual Studio is not the problem.

### Prerequisite: `vswhere.exe` on `PATH`

```
C:\Program Files (x86)\Microsoft Visual Studio\Installer
```

Add that directory to `PATH`. Without it, publishing fails with a linker command that begins
`'vswhere.exe' is not recognized...` **even though the workload is installed correctly** — which
reads like a missing linker and is not one.

The cause is worth knowing, because nothing about the message points at it. `findvcvarsall.bat`
calls `vcvarsall.bat`, which looks up `vswhere` on `PATH`; when that fails it prints to stderr and
carries on, so the script still exits 0 with the right answer on stdout. But the compiler captures
it with MSBuild's `ConsoleToMSBuild`, which **merges stderr into stdout**, and then takes
`Split('#')[0]` as the linker directory. That slice is the error text rather than the path, so the
compiler invokes a command built out of an error message.

Diagnose by running the script directly — it should print exactly two lines, a path ending in `#`
and a `LIB` list. Any line before those is the fault:

```powershell
cmd /c "`"$env:USERPROFILE\.nuget\packages\microsoft.dotnet.ilcompiler\10.0.5\build\findvcvarsall.bat`" x64"
```

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
| `PublishAot` | Compiles to a native binary. No JIT, no runtime to bundle, nothing extracted at startup. |
| `SelfContained` | Implied by AOT; stated for clarity. The user needs nothing installed. |
| `RuntimeIdentifiers` | `win-x64;win-arm64`. |
| `InvariantGlobalization=false` | LLM replies are full of non-ASCII text. Costs ICU in the bundle; a deliberate trade. |
| `ApplicationIcon` | `assets/openkey.ico`, the same file for both executables — one product, two front doors. The SDK writes it into the PE's Win32 resource table ahead of the AOT link, so it survives `PublishAot`. |

`IsAotCompatible` is gone — it existed to surface trim/AOT warnings without committing to AOT, and
`PublishAot` implies the same analyzers.

### The icon is a committed artefact, not a build step

`assets/openkey.ico` is checked in. Nothing in the build generates it, so the publish pipeline
needs no image toolchain. Regenerate it by hand after a change to the mark or the brand colour:

```powershell
.\tools\make-icon.ps1
```

The script uses only `System.Drawing` from the .NET Framework GAC — present on every Windows box,
nothing to install. It writes seven sizes (16, 20, 24, 32, 48, 64, 256): BMP entries below 256 and
PNG at 256, which is the layout real icon tooling emits. PNG at every size is legal on Windows 10
and later but is not universally decodable — `System.Drawing.Icon` refuses such a file outright,
which is fair warning about other consumers.

## AOT, and why the old objection expired

This document used to say AOT was blocked by Spectre.Console's internal reflection. Measured from
the shipped assemblies, `IsTrimmable` metadata is **absent** in Spectre.Console 0.49.1 and
**present** in 0.55.2 — the library did the work. The other stated blocker, reflection-based
`System.Text.Json`, went away when everything persisted moved to source-generated contexts.

So the question became a measurement, and `.github/workflows/aot-trial.yml` answered it:

| | Single-file (previous) | NativeAOT (now) |
|---|---|---|
| Size | 43 MB | **10.8 MB** |
| Startup | ~1–2 s cold (decompress + extract) | **~0.16 s** |
| Extracts to temp on first run | yes | **no** |
| Loose DLLs beside the exe | n/a | 0 |

All three differences land on the same thing: this is software people copy onto a USB stick and
run on someone else's machine.

Verified on both architectures in CI, and the x64 binary was run locally against a live model —
streaming, markdown rendering, the tokenizer, DPAPI key load and config persistence all work
compiled. The trial workflow stays in the repo so the comparison can be re-run rather than
re-argued.

The cost is the C++ workload prerequisite above. `build`, `test` and `run` are unaffected.

Trimming is not separately enabled: AOT already implies it.

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

No sketch is reproduced here: a workflow copied into prose is a second source of truth that drifts
from the real one, which is the mistake the publish flags already made once. Read the files.

A third workflow, `aot-trial.yml`, exists to re-measure AOT against the current single-file settings
on demand. It is an experiment rather than a gate, and it is where the numbers in this document
came from.
