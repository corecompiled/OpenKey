# OpenKey

Click-and-play Windows console chat client for free OpenRouter LLMs. Double-click the `.exe`, paste an OpenRouter API key once, and chat. Auto-rotates across free models on rate-limits. Single self-contained binary — runs from a USB stick.

## Run from source

```cmd
dotnet run --project src\OpenKey\OpenKey.csproj
```

## Build single-file exe

```cmd
dotnet publish src\OpenKey\OpenKey.csproj -c Release -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:PublishReadyToRun=true ^
  -o publish\
```

Output: `publish\OpenKey.exe`.

## Commands

| Command | Effect |
|---------|--------|
| `/model` | Show current active model |
| `/reset` | Wipe `%APPDATA%\OpenKey\` and re-run first-run setup |
| `/quit`  | Exit |

## Where data is stored

`%APPDATA%\OpenKey\` (typically `C:\Users\<you>\AppData\Roaming\OpenKey\`). The API key is DPAPI-encrypted per Windows user — only the user who entered it on this PC can decrypt.

## Get an OpenRouter API key

Sign up free at <https://openrouter.ai/keys>. OpenKey uses only free-tier models.

## Docs

Full project docs in [`docs/`](docs/). Start with [`docs/00-overview.md`](docs/00-overview.md). Meta-rules for Claude sessions in [`CLAUDE.md`](CLAUDE.md).

## Status

Desktop console exe available now. A browser (PWA) app and an Android app are planned.
