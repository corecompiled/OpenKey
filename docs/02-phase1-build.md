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
dotnet new sln -n OpenKey

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
dotnet add src\OpenKey\OpenKey.csproj package Microsoft.Extensions.DependencyInjection
dotnet add src\OpenKey\OpenKey.csproj package Microsoft.Extensions.Hosting
```

`OpenKey.Core` and `OpenKey.Providers.OpenRouter` use only BCL (`System.Text.Json`, `System.Net.Http`, `System.Security.Cryptography.ProtectedData`). DPAPI lives in the `System.Security.Cryptography.ProtectedData` NuGet (it was removed from the SDK BCL on non-Windows targets):

```cmd
dotnet add src\OpenKey.Core\OpenKey.Core.csproj package System.Security.Cryptography.ProtectedData
```

## Step 3 — Project file edits

`src\OpenKey\OpenKey.csproj` — set output, version, icon (optional):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>OpenKey</RootNamespace>
    <AssemblyName>OpenKey</AssemblyName>
    <Version>0.1.0</Version>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  </PropertyGroup>
</Project>
```

`OpenKey.Core.csproj` and `OpenKey.Providers.OpenRouter.csproj` — just enable nullable + implicit usings.

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

`/reset` flow:
1. Spectre `Confirm("Wipe all OpenKey data and re-run setup?")`
2. `_keyStore.Clear(); _sessions.Clear(); _catalog.ClearCache(); _rotation.Clear();`
3. Delete `%APPDATA%\OpenKey\` directory
4. Call `EnsureFirstRunAsync()` again
5. Continue REPL

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
   │                              │         2. start HttpListener on 127.0.0.1:<ephemeral>/callback
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
REPL  ("<Environment.UserName>:" prompt; "OpenKey AI is thinking…" spinner until the reply completes, then "OpenKey AI:" header + markdown-rendered reply)
```

OAuth wire details live in `03-openrouter-integration.md` § "OAuth / PKCE". Storage behavior unchanged (DPAPI-encrypted `key.bin`) — OAuth is just a UX option for *obtaining* the key.

## Acceptance checklist (Phase 1 done)

- [ ] `dotnet run --project src\OpenKey` launches Spectre banner
- [ ] First-run menu offers (1) Sign in with browser (OAuth/PKCE), (2) Paste an existing key
- [ ] During OAuth wait, hint `[p] paste, [c] cancel` is visible
- [ ] Pressing `p` during OAuth wait drops to paste prompt within the same first-run attempt
- [ ] Pressing `c` (or Esc) during OAuth wait returns to the menu
- [ ] OAuth path: browser opens to `openrouter.ai/auth` with `callback_url=http://localhost:3000/callback` (fixed per docs), callback returns a key, app validates + persists DPAPI-encrypted
- [ ] If port 3000 is in use, OpenKey reports it and offers paste fallback in the same first-run attempt
- [ ] Paste path: secret prompt accepts a key, validates against `/models`, persists DPAPI-encrypted
- [ ] After key validation, screen is cleared and banner reprinted before the chat REPL opens
- [ ] REPL prompt label uses the current Windows username (`Environment.UserName`) followed by `: `
- [ ] AI label reads `OpenKey AI:` (no `❯` glyph)
- [ ] Banner rule reads `OpenKey vX.Y.Z` (version inline); subline reads `Developed by Paolo Patron`. No data dir on the banner.
- [ ] `/help` prints a Spectre table listing `/about`, `/models`, `/model`, `/cls`, `/help`, `/reset`, `/quit`
- [ ] `/about` prints a Spectre panel with version, data dir, active model, pinned model, developer
- [ ] `/models` opens a Spectre `SelectionPrompt` over the free-model catalog with `↑/↓` navigation; selecting a model pins it (via `ChatEngine.PreferredModelId`) until restart; selecting "Auto" clears the pin
- [ ] Reply text is never truncated — the full buffered reply (including the final chunk) is rendered and the cursor returns to the prompt without a perceived hang
- [ ] When a message is sent, a `OpenKey AI is thinking…` spinner shows until the reply completes; then the markdown-rendered reply prints (bold, italic, inline/fenced code, headings, lists)
- [ ] Killing model 1 (e.g., set temporary `cooldownUntil` via test hook) auto-rotates to model 2 mid-conversation
- [ ] `/model` prints `current model: <id>`
- [ ] `/reset` confirms, wipes, re-runs the first-run menu in same process
- [ ] `/quit` exits cleanly
- [ ] Close + reopen → previous session restored, last 2 turns shown
- [ ] Ctrl+C while the reply spinner is active cancels cleanly, returns to prompt
- [ ] Build via the `dotnet publish` command in `06-build-and-distribute.md` produces a runnable single `.exe`
