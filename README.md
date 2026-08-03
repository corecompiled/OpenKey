# OpenKey

Chat with capable AI models for free, from a single Windows `.exe`. Nothing to install, no
subscription. Double-click, sign in once, and type.

When a free model is busy, OpenKey quietly moves to another one and you still get your answer.

```
── OpenKey v0.1.0 ──────────────────────────────────────────────────
Developed by Paolo Patron

┌─ Getting started ─────────────────────────────────────────────────┐
│ Type a message and press Enter to chat.                           │
│                                                                   │
│ /models   Choose which AI model answers you                       │
│ /help     See everything OpenKey can do                           │
│ /quit     Close OpenKey                                           │
└───────────────────────────────────────────────────────────────────┘

Patron ❯ explain server-sent events in one line

OpenKey AI  ·  deepseek/deepseek-chat-v3:free  ·  1.2s

A one-way HTTP stream where the server pushes `data:` lines as they
happen, instead of the client polling for them.

Patron ❯
```

## Get it

Two ways to use it, same chat and same saved conversation underneath:

| | |
|---|---|
| **`OpenKeyApp.exe`** | A normal window. Start here if you're not sure. |
| **`OpenKey.exe`** | The terminal version, if that's where you live. |

Download either from [Releases](https://github.com/corecompiled/OpenKey/releases) and double-click.
Around 11–30 MB, nothing installed, runs from a USB stick.

Or via [Scoop](https://scoop.sh), which also avoids the SmartScreen prompt:

```
scoop install https://raw.githubusercontent.com/corecompiled/OpenKey/main/packaging/scoop/openkey.json
```

You'll need a free [OpenRouter](https://openrouter.ai) key. OpenKey can fetch one through your
browser on first run, or you can paste one you already have. Either way it's encrypted for your
Windows account and stays on your PC.

Full walkthrough: [`docs/08-user-guide.md`](docs/08-user-guide.md).

## Commands

| Command | Effect |
|---|---|
| `/new` | Start a fresh conversation, keeping your key |
| `/retry` | Send your last message again |
| `/history` | Show the conversation so far |
| `/copy` | Copy the last reply to the clipboard |
| `/export [path]` | Save the conversation as a markdown file |
| `/models` | Choose which AI model answers you |
| `/model` | Show which model is answering right now |
| `/theme` | Switch colours: default, dark, light, mono |
| `/name` | Change what OpenKey calls you |
| `/about` | Version, where your data lives, who made it |
| `/cls` | Clear the screen |
| `/help` | List all commands |
| `/reset` | Erase everything and start over |
| `/quit` | Close OpenKey (alias: `/exit`) |

Anything not starting with `/` is sent to the AI. Ctrl+C stops a reply in progress.

## Your data

Stored in `%APPDATA%\OpenKey\`. Your key is encrypted so only your Windows account on this PC can
read it; your conversation is plain JSON. OpenKey talks to OpenRouter and nowhere else, and
collects no telemetry of any kind. See [`SECURITY.md`](SECURITY.md).

## Build from source

Requires the .NET SDK pinned in `global.json`. Windows only.

```cmd
dotnet run --project src\OpenKey\OpenKey.csproj      # run
dotnet test                                          # 87 tests
dotnet publish src\OpenKey\OpenKey.csproj -c Release -r win-x64 -o publish\
```

Publish settings live in the project file, so that last line produces the shipping binary — no
flags to remember. Details in [`CONTRIBUTING.md`](CONTRIBUTING.md).

## Docs

| | |
|---|---|
| [User guide](docs/08-user-guide.md) | Every feature, and what to do when something breaks |
| [Architecture](docs/architecture/) | How it works, and [why it works that way](docs/architecture/08-decisions.md) |
| [Backlog](BACKLOG.md) | What's next, and what's deliberately not happening |
| [Changelog](CHANGELOG.md) | What changed |
| [Contributing](CONTRIBUTING.md) | Building, testing, house rules |

Project overview: [`docs/00-overview.md`](docs/00-overview.md).

## Status

The Windows desktop app works today. A browser app and an Android app reuse the same contracts and
are planned; a desktop GUI, tool use, and local document search come first. See
[`docs/07-roadmap.md`](docs/07-roadmap.md).

## Licence

MIT — see [`LICENSE`](LICENSE).
