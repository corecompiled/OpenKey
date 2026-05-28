# 06 — Build and Distribute

Producing the click-and-play `OpenKey.exe`.

## Publish command

From `C:\Users\Patron\OpenKey\`:

```cmd
dotnet publish src\OpenKey\OpenKey.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:PublishReadyToRun=true ^
  -o publish\
```

Output: `C:\Users\Patron\OpenKey\publish\OpenKey.exe`

That single file is the entire deliverable. Copy it to a USB stick, give it to a friend, double-click — done.

## Flag rationale

| Flag | Why |
|------|-----|
| `--self-contained true` | Bundles the .NET runtime into the exe. User doesn't need .NET installed. |
| `-p:PublishSingleFile=true` | One file output (vs a folder of DLLs). |
| `-p:IncludeNativeLibrariesForSelfExtract=true` | Native libs (e.g., DPAPI shim) bundled and extracted to a temp dir at runtime. Required for a true single-file experience. |
| `-p:EnableCompressionInSingleFile=true` | Cuts ~30% off file size. Trade: slightly slower first launch (one-time decompression). |
| `-p:PublishReadyToRun=true` | Pre-jits IL for faster startup. Larger exe but the cmd app feels instant. |
| `-r win-x64` | Single RID. Phase 1 is Windows only. |

## Why NOT trimming

```
# DO NOT add:
-p:PublishTrimmed=true
```

Spectre.Console uses reflection internally (for prompt validators, table cells, etc.). Trimming will silently break the UI or crash with `MissingMethodException` at runtime. If we later want a smaller exe, we'd need to add `[DynamicallyAccessedMembers]` attributes throughout — not worth it for Phase 1.

## Why NOT AOT

```
# DO NOT add in Phase 1:
-p:PublishAot=true
```

AOT (NativeAOT) produces a ~10MB exe with no JIT overhead. But:
- Spectre.Console v0.49+ has partial AOT support but quirks remain.
- DPAPI via `System.Security.Cryptography.ProtectedData` works under AOT, fine.
- `System.Text.Json` source generators are required (no reflection-based serialization).
- DI containers like `Microsoft.Extensions.DependencyInjection` work but constructor injection via reflection requires care.

Net: doable, but adds yank to the Phase 1 timeline. Defer to Phase 1.2 or Phase 2.

## Expected output

| Metric | Value |
|--------|-------|
| File size | ~30–50 MB (compressed) |
| First launch cold-start | ~1–2 s (decompress + R2R) |
| Subsequent launches | <500 ms |
| Working set RAM | ~80–120 MB |

## Versioning

Edit `src\OpenKey\OpenKey.csproj`:

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
- `1.0.0` — Phase 1 ship
- `1.1.0` — Phase 1.1 QoL
- `1.2.0` — Phase 1.2 QoL
- `2.0.0` — GUI (Phase 2)

## Smoke test checklist (manual, run after every publish)

Run the published `OpenKey.exe`:

1. [ ] Launch shows banner with version
2. [ ] First-run shows a menu: (1) Sign in with browser, (2) Paste an existing key
3. [ ] During OAuth wait, hint `[p] paste, [c] cancel` is visible
4. [ ] Pressing `p` during OAuth wait drops to paste prompt within the same attempt
5. [ ] Pressing `c` (or Esc) during OAuth wait returns to the menu
6. [ ] OAuth path: browser opens to openrouter.ai/auth, user signs in, success page renders, console reports "✓ key validated and saved."
7. [ ] Paste path: invalid key shows red error and re-prompts (test once); valid key → "✓ key validated and saved."
8. [ ] After key validation, screen is cleared and banner reprinted before the chat REPL opens
9. [ ] REPL prompt label uses current Windows username + `: ` (e.g., `Patron: `); AI label reads `OpenKey AI:`
9a.[ ] Banner grey line shows `Developed by Paolo Patron` (non-italic)
9b.[ ] `/help` prints a Spectre table listing all supported commands
10. [ ] Callback listener binds to `http://localhost:3000/callback`. If Windows Defender Firewall prompts on first run, allow only "Private networks"
10a.[ ] If port 3000 is in use by another app, OpenKey reports it and offers paste fallback in the same attempt
11. [ ] Send "hello" → `OpenKey AI is thinking…` spinner shows until the reply completes, then the reply prints as rendered markdown (bold, code, headings, lists)
12. [ ] `/model` prints the active model id
13. [ ] Ctrl+C while the reply spinner is active cancels cleanly, returns to prompt
14. [ ] Close + relaunch → "Resumed session" + last 2 turns shown
15. [ ] `/reset` confirms, wipes, re-runs first-run menu in-process; on success the screen clears again before the chat REPL
16. [ ] After reset, send a message → works with newly-saved key
17. [ ] Forced rotation: temporarily edit `rotation.state.json` to set `cooldownUntil` far future for model #1 → next send picks model #2
18. [ ] `/quit` exits cleanly with code 0

Document any deviation in `docs\smoke-results.md` (create if needed).

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
6. First-run prompt appears (because per-user provisioning — see 05-persistence-and-reset)
7. User pastes *their own* OpenRouter key → saved to *their* `%APPDATA%` on *that* machine
8. Verify chat works end-to-end

Note: the USB drive itself stores nothing user-specific. All state lives in `%APPDATA%` of whichever Windows user runs the exe. This is intentional (and aligns with the "Per-user provisioning" decision).

## CI build (optional, Phase 1.1)

GitHub Actions workflow sketch:

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
