# Contributing

## Getting a build

```cmd
git clone https://github.com/corecompiled/OpenKey.git
cd OpenKey
dotnet build
dotnet test
```

You need the .NET SDK version pinned in `global.json` (or a later patch of the same feature band).
Windows only — OpenKey targets `net10.0-windows` and uses DPAPI for key storage.

Run it from source:

```cmd
dotnet run --project src\OpenKey\OpenKey.csproj
```

Build the shipping binary:

```cmd
dotnet publish src\OpenKey\OpenKey.csproj -c Release -r win-x64 -o publish\
```

Every publish flag lives in `OpenKey.csproj`. Don't pass them on the command line, and don't
document a different command anywhere — the point is that CI and a developer machine produce the
same artifact.

OpenKey publishes as a NativeAOT binary, so that one command needs the MSVC linker:

```cmd
winget install Microsoft.VisualStudio.2022.BuildTools --override "--quiet --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
```

Without it you get *"Platform linker not found"*. `build`, `test` and `run` are unaffected, so you
only need this to cut a release build. See
[`docs/06-build-and-distribute.md`](docs/06-build-and-distribute.md).

## Before you open a PR

- `dotnet build` is clean. `TreatWarningsAsErrors` is on, and `IsAotCompatible` enables the
  trim/AOT analyzers, so warnings fail the build. That is deliberate.
- `dotnet test` is green. Add tests for behaviour you changed.
- If you touched the console, run it and look at it — in Windows Terminal *and* in `conhost.exe`.
  Several defects here were invisible in review and obvious on screen.
- If you touched anything under `docs/`, walk the doc-hygiene checklist in
  [`CLAUDE.md`](CLAUDE.md#doc-hygiene-checklist-run-on-every-md-update).

## Things that will get a PR sent back

**Contract changes without discussion.** These four surfaces are normative because a browser and
Android port are planned and must behave identically:

- `IChatProvider` and the records around it
- The `ChatErrorKind` taxonomy
- The `%APPDATA%\OpenKey\` file layout and JSON shapes
- Rotation rules

Changing any of them means updating the contract doc first, then every implementation. Open an
issue before writing code.

**A colour or glyph literal outside `Ui/Theme.cs` or `Ui/Glyphs.cs`.** The console was previously
styled at call sites and drifted into three different cases and four border styles. Everything
visible is a component in `Ui/Components.cs`.

**An error message with no next step.** Every failure the user can see must say what happened and
what to do about it. A dead end is a bug, not a rough edge.

**Internal vocabulary on screen.** No `DPAPI`, `OAuth`, `PKCE`, `429`, or `ChatErrorKind` on any
surface a user reads. HTTP status codes may appear only in a card's grey detail line.

**Core depending on a host or a provider.** `OpenKey.Core` has no package references and no
knowledge of the console. Keep it that way.

## Layout

| Project | What it is |
|---|---|
| `src/OpenKey.Core` | Engine, rotation, storage, contracts. No dependencies. |
| `src/OpenKey.Providers.OpenRouter` | OpenRouter wire format and SSE. |
| `src/OpenKey` | Console host, UI, DPAPI key store, OAuth. |
| `tests/OpenKey.Core.Tests` | Engine, rotation, storage. |
| `tests/OpenKey.Tests` | Provider, console UI, PKCE. |

Start with [`docs/architecture/`](docs/architecture/) — particularly
[`08-decisions.md`](docs/architecture/08-decisions.md), which records things that were tried or
considered and rejected, so they don't get re-proposed.

Testing conventions are in [`docs/09-testing.md`](docs/09-testing.md).

## Commits

Conventional Commits (`feat:`, `fix:`, `docs:`, `test:`, `chore:`). Explain *why* in the body when
it isn't obvious from the diff — most of the valuable commit messages in this repo describe a
failure mode, not a code change.

## Releases

Tag `vX.Y.Z` and the release workflow builds and attaches both architectures. Release titles carry
the version only — no phase numbers on any public surface.
