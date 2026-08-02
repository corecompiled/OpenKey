# 02 — Phase 1 Build Walkthrough

End-to-end build steps. Follow top to bottom on a clean machine.

## Prereqs

- Windows 10 or 11 (x64)
- .NET 10 SDK installed (`dotnet --version` returns `10.x.x`)
- Any editor (VS Code with C# Dev Kit, Rider, or Visual Studio)
- Git (optional)

## Step 1 — Create solution + projects

From `C:\Users\Patron\OpenKey\`:

```cmd
dotnet new sln -n OpenKey    # the repo now uses the newer OpenKey.slnx format

dotnet new console     -n OpenKey                          -o src\OpenKey                          --framework net10.0
dotnet new classlib    -n OpenKey.Core                     -o src\OpenKey.Core                     --framework net10.0
dotnet new classlib    -n OpenKey.Providers.OpenRouter     -o src\OpenKey.Providers.OpenRouter     --framework net10.0

dotnet sln add src\OpenKey\OpenKey.csproj
dotnet sln add src\OpenKey.Core\OpenKey.Core.csproj
dotnet sln add src\OpenKey.Providers.OpenRouter\OpenKey.Providers.OpenRouter.csproj

dotnet add src\OpenKey\OpenKey.csproj                            reference src\OpenKey.Core\OpenKey.Core.csproj
dotnet add src\OpenKey\OpenKey.csproj                            reference src\OpenKey.Providers.OpenRouter\OpenKey.Providers.OpenRouter.csproj
dotnet add src\OpenKey.Providers.OpenRouter\OpenKey.Providers.OpenRouter.csproj reference src\OpenKey.Core\OpenKey.Core.csproj
```

## Step 2 — NuGet packages

```cmd
dotnet add src\OpenKey\OpenKey.csproj package Spectre.Console
dotnet add src\OpenKey\OpenKey.csproj package Markdig
dotnet add src\OpenKey\OpenKey.csproj package Microsoft.Extensions.DependencyInjection
dotnet add src\OpenKey\OpenKey.csproj package System.Security.Cryptography.ProtectedData
```

Two corrections to what this step originally said:

- **Markdig is required.** `MarkdownConsoleRenderer` is built on it, so omitting it here produced a
  project that does not compile.
- **`Microsoft.Extensions.Hosting` is not used.** A REPL needs no generic host, hosted-service
  lifetime, or configuration binding; `Program.cs` composes its dozen services explicitly. See
  [`architecture/08-decisions.md`](architecture/08-decisions.md).

`OpenKey.Core` and `OpenKey.Providers.OpenRouter` use only BCL (`System.Text.Json`, `System.Net.Http`, `System.Security.Cryptography.ProtectedData`). DPAPI lives in the `System.Security.Cryptography.ProtectedData` NuGet (it was removed from the SDK BCL on non-Windows targets):

```cmd
dotnet add src\OpenKey.Core\OpenKey.Core.csproj package System.Security.Cryptography.ProtectedData
```

## Step 3 — Project file edits

Shared settings — `TargetFramework`, `Nullable`, `ImplicitUsings`, `Version`,
`TreatWarningsAsErrors` — live in **`Directory.Build.props`** at the repo root, not in each project.
Don't repeat them per project; they apply automatically.

`src\OpenKey\OpenKey.csproj` carries only what is specific to the host:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <!-- -windows only on the host: DPAPI is Windows-only, and Core must stay portable
         so a future PWA or Android port doesn't inherit a Windows target framework. -->
    <TargetFramework>net10.0-windows</TargetFramework>
    <RootNamespace>OpenKey</RootNamespace>
    <AssemblyName>OpenKey</AssemblyName>
    <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
    <InvariantGlobalization>false</InvariantGlobalization>

    <!-- The publish profile lives here, not in a README. See docs/06. -->
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    <PublishReadyToRun>true</PublishReadyToRun>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
</Project>
```

`OpenKey.Core.csproj` and `OpenKey.Providers.OpenRouter.csproj` need almost nothing beyond
`IsAotCompatible` and their `InternalsVisibleTo` entries.

## Step 4 — File-by-file scaffold

Below: the stub signatures. Implementation details for the OpenRouter parts live in `03-openrouter-integration.md`; rotation in `04-model-rotation.md`; persistence in `05-persistence-and-reset.md`.

### `src\OpenKey.Core\Providers\IChatProvider.cs`
See full definition in `01-architecture.md` § "IChatProvider contract". Drop it in this file.

### `src\OpenKey.Core\Providers\ChatException.cs`
See `01-architecture.md` § "Error taxonomy". Drop it in this file.

### `src\OpenKey.Core\Engine\ChatEngine.cs`

```csharp
public sealed class ChatEngine
{
    private readonly IChatProvider _provider;
    private readonly IRotationPolicy _rotation;
    private readonly IModelCatalog _catalog;
    private readonly ISessionStore _sessions;
    private readonly List<ChatMessage> _turns = new();

    public ChatEngine(IChatProvider provider, IRotationPolicy rotation,
                     IModelCatalog catalog, ISessionStore sessions) { ... }

    public ModelInfo? ActiveModel { get; private set; }

    public Task ResumeAsync(CancellationToken ct);          // load last session if any
    public Task NewSessionAsync(CancellationToken ct);       // clear _turns
    public IAsyncEnumerable<ChatChunk> SendAsync(string userText, CancellationToken ct);
    public IReadOnlyList<ChatMessage> Turns => _turns;
}
```

Inside `SendAsync`: append user turn → trim rolling window → loop over `_rotation.PickAsync` retrying on `ChatException` (kind in transient set) → yield chunks → on final, append assistant turn → `_sessions.SaveAsync`.

### `src\OpenKey.Core\Engine\IRotationPolicy.cs`

```csharp
public interface IRotationPolicy
{
    Task<ModelInfo> PickAsync(IReadOnlyList<ModelInfo> candidates, CancellationToken ct);
    void MarkFailure(string modelId, ChatErrorKind kind, string? retryAfterHint);
    void MarkSuccess(string modelId);
}
```

Default impl `RotationPolicy.cs` — see `04-model-rotation.md`.

### `src\OpenKey.Core\Storage\IKeyStore.cs`

```csharp
public interface IKeyStore
{
    bool HasKey();
    string? Load();                 // returns plaintext, null if missing/corrupt
    void Save(string apiKey);
    void Clear();
}
```

Default impl `DpapiKeyStore.cs` — see `05-persistence-and-reset.md`.

### `src\OpenKey.Core\Storage\ISessionStore.cs`

```csharp
public interface ISessionStore
{
    Task<SessionSnapshot?> LoadAsync(CancellationToken ct);
    Task SaveAsync(SessionSnapshot snap, CancellationToken ct);
    void Clear();
}

public sealed record SessionSnapshot(
    string ModelId,
    DateTimeOffset StartedAt,
    IReadOnlyList<ChatMessage> Turns);
```

### `src\OpenKey.Core\Storage\IModelCatalog.cs`

```csharp
public interface IModelCatalog
{
    Task<IReadOnlyList<ModelInfo>> GetFreeModelsAsync(CancellationToken ct);
    Task RefreshAsync(CancellationToken ct);
}
```

Default impl calls `IChatProvider.ListModelsAsync`, filters `IsFree`, caches to `%APPDATA%\OpenKey\models.cache.json` with 24h TTL.

### `src\OpenKey.Providers.OpenRouter\OpenRouterProvider.cs`
Implements `IChatProvider`. See `03-openrouter-integration.md` for the wire details.

### `src\OpenKey\Program.cs`

```csharp
var services = new ServiceCollection();
services.AddSingleton<IKeyStore, DpapiKeyStore>();
services.AddSingleton<ISessionStore, JsonSessionStore>();
services.AddSingleton<IRotationPolicy, RotationPolicy>();
services.AddSingleton<IChatProvider, OpenRouterProvider>();
services.AddSingleton<IModelCatalog, ModelCatalog>();
services.AddSingleton<ChatEngine>();
services.AddSingleton<CommandRouter>();
services.AddSingleton<ConsoleHost>();
services.AddSingleton<HttpClient>(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(60) });

var sp = services.BuildServiceProvider();
await sp.GetRequiredService<ConsoleHost>().RunAsync(default);
```

### `src\OpenKey\ConsoleHost.cs`

```csharp
public sealed class ConsoleHost
{
    public async Task RunAsync(CancellationToken outerCt)
    {
        PrintBanner();
        await EnsureFirstRunAsync();           // see 05-persistence-and-reset.md
        await _engine.ResumeAsync(outerCt);    // restore last session if any
        ShowResumeRecap();                     // print last 2 turns if resumed

        using var cts = LinkCtrlC(outerCt);
        while (!cts.IsCancellationRequested)
        {
            var line = AnsiConsole.Ask<string>("[bold cyan]you[/] ❯ ");
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (_commands.TryHandle(line, out var stop)) { if (stop) break; else continue; }

            // Buffer the full reply (engine still streams chunks), then render once.
            var sb = new StringBuilder();
            await foreach (var chunk in _engine.SendAsync(line, cts.Token))
                sb.Append(chunk.DeltaText);
            AnsiConsole.MarkupLine("[bold magenta]OpenKey AI[/]:");   // no model id; see /model
            MarkdownConsoleRenderer.Render(AnsiConsole.Console, sb.ToString()); // markdown → Spectre
            AnsiConsole.WriteLine();
        }
    }
}
```

### `src\OpenKey\CommandRouter.cs`

```csharp
public sealed class CommandRouter
{
    public bool TryHandle(string input, out bool stop)
    {
        stop = false;
        if (!input.StartsWith("/")) return false;
        var parts = input.Trim().Split(' ', 2);
        switch (parts[0].ToLowerInvariant())
        {
            case "/quit":  case "/exit": stop = true; return true;
            case "/reset": ResetAll(); return true;
            case "/cls":   ClearAndShowChatHeader(); return true; // clear + reprint banner/getting-started (not /reset)
            case "/model": ShowActiveModel(); return true;
            case "/models": ShowModelPicker(); return true; // Spectre SelectionPrompt over free models; pins via ChatEngine.PreferredModelId
            case "/about": ShowAbout(); return true;        // Spectre Panel: version, data dir, active model, pinned, dev
            case "/help":  ShowHelp(); return true;         // Spectre Table of all commands
            default:
                AnsiConsole.MarkupLine($"[red]unknown command:[/] {parts[0]}");
                return true;
        }
    }
}
```

`/reset` semantics are normative in
[`05-persistence-and-reset.md`](05-persistence-and-reset.md#reset-semantics) — follow that, not a
copy here. Both files previously spelled out divergent sequences.

## First-run flow (sequence)

```
OpenKey.exe launched
   │
PrintBanner()
   │
keyStore.HasKey()?  ── no ─→  Spectre SelectionPrompt:
   │                              ├── "Sign in with browser (OAuth/PKCE)"
   │                              │       │
   │                              │       Show hint: "[p] paste, [c] cancel"
   │                              │       Spawn background key-watcher (Console.ReadKey).
   │                              │       │
   │                              │       OpenRouterOAuth.AcquireKeyAsync(ct):
   │                              │         1. generate PKCE pair
   │                              │         2. start HttpListener on localhost:3000/callback (fixed)
   │                              │         3. Process.Start the openrouter.ai/auth URL
   │                              │         4. await callback ?code=... (timeout 5min)
   │                              │         5. POST /api/v1/auth/keys exchange → user_key
   │                              │       │
   │                              │       interrupts:
   │                              │         'p' → cancel listener, fall into paste prompt (same attempt)
   │                              │         'c' / Esc → cancel listener, return to menu (next attempt)
   │                              │       ↓
   │                              │       validate: provider.ListModelsAsync(ct)
   │                              │       ↓
   │                              │       keyStore.Save(user_key)
   │                              │
   │                              └── "I already have a key — paste it"
   │                                      │
   │                                      AnsiConsole.Prompt(secret) → validate → keyStore.Save
   │
   │                              on either path failure: show error, re-show menu,
   │                              up to 3 attempts then exit
   │ yes
   ↓
AnsiConsole.Clear() + reprint banner + commands hint
   ↓
catalog.GetFreeModelsAsync()  (cache or refresh)
   ↓
engine.ResumeAsync()          (loads session.json if present)
   ↓
REPL  ("<Environment.UserName> ❯ " prompt; "Thinking" spinner until the FIRST token, then a
       "OpenKey AI · <model> · <elapsed>" header and a block-by-block streamed reply)
```

OAuth wire details live in `03-openrouter-integration.md` § "OAuth / PKCE". Storage behavior unchanged (DPAPI-encrypted `key.bin`) — OAuth is just a UX option for *obtaining* the key.

## Acceptance checklist (Phase 1 done)

All met as of the production-readiness pass. Several items were reworded when the console was
rebuilt — the originals described a buffered spinner and an `OpenKey AI:` label that no longer
exist. Automated coverage is in `tests/`; see [`09-testing.md`](09-testing.md).

- [x] `dotnet run --project src\OpenKey` launches the banner
- [x] First-run menu offers (1) Sign in with browser, (2) Paste an existing key
- [x] During OAuth wait, the hint to press `P` to paste or `Esc` to cancel is visible
- [x] Pressing `P` during OAuth wait drops to the paste prompt within the same attempt
- [x] Pressing `Esc` during OAuth wait returns to the menu
- [x] OAuth path: browser opens to `openrouter.ai/auth` with `callback_url=http://localhost:3000/callback` (fixed), callback returns a key, app validates and persists it DPAPI-encrypted
- [x] If port 3000 is in use, OpenKey says so and offers paste in the same attempt
- [x] Paste path: secret prompt accepts a key, validates against `/models`, persists it
- [x] After validation the screen clears and the banner reprints before the REPL opens
- [x] REPL prompt is `<Environment.UserName> ❯ `, degrading to `>` where the glyph is unsafe
- [x] Reply header is `OpenKey AI · <model id> · <elapsed>`, printed before the first token arrives
- [x] Banner reads `OpenKey vX.Y.Z` with subline `Developed by Paolo Patron`; no data dir on the banner
- [x] `/help` lists `/models`, `/model`, `/about`, `/cls`, `/help`, `/reset`, `/quit`
- [x] `/about` shows version, active model, model choice, data dir, key handling, developer
- [x] `/models` opens a picker showing display name and context size; selecting one pins it until restart; "Auto" clears the pin
- [x] Reply text is never truncated; the cursor returns to the prompt without a perceived hang
- [x] A `Thinking` spinner shows until the **first token**, then the reply streams block by block with bold, italic, inline and fenced code, headings and lists rendered
- [x] A forced failure on model 1 rotates to model 2 mid-conversation, and the reply appears **once** (covered by `ChatEngineTests`)
- [x] `/model` names the current model
- [x] `/reset` states what it erases, confirms, wipes, and re-runs setup in the same process
- [x] `/quit` exits cleanly, holding the window when double-clicked
- [x] Close and reopen restores the previous session and shows the last 2 turns
- [x] Ctrl+C during a reply cancels that reply only; **the next message still works**
- [x] `dotnet publish -c Release -r win-x64` produces a runnable single `.exe` with no extra flags
