# 08 — User guide

Everything OpenKey does, from a user's point of view.

## Getting started

Download `OpenKey.exe` and double-click it. Nothing to install, no runtime to add. It runs happily
from a USB stick.

Windows may show a SmartScreen warning the first time, because the file isn't code-signed yet.
Choose **More info → Run anyway** if you trust where you got it from.

### Signing in, once

OpenKey needs a free OpenRouter key. Two ways:

**Sign in with your browser** (recommended). OpenKey opens OpenRouter, you approve, and it collects
the key automatically. Press <kbd>P</kbd> at any point to switch to pasting instead, or
<kbd>Esc</kbd> to cancel.

**Paste a key you already have.** Create one at <https://openrouter.ai/keys>. It won't appear on
screen as you type.

Either way OpenKey checks the key works before saving it, and encrypts it for your Windows account.

## Chatting

Type and press Enter. Anything that doesn't start with `/` goes to the AI.

While a reply arrives you'll see the model's name and how long it's taken. Text appears as it's
generated; formatting settles a paragraph at a time, so finished parts look tidy while the rest is
still coming.

Press <kbd>Ctrl</kbd>+<kbd>C</kbd> to stop a reply you don't want. That cancels only that reply —
you can carry straight on. At the prompt with nothing running, <kbd>Ctrl</kbd>+<kbd>C</kbd> closes
OpenKey.

Arrow keys, <kbd>Home</kbd>, <kbd>End</kbd> and <kbd>F7</kbd> (recent lines) all work while typing.
Pasting several lines at once sends them as a single message.

Your conversation is remembered. Next time you open OpenKey it shows the last couple of turns and
picks up where you left off.

## Commands

| Command | What it does |
|---|---|
| `/models` | Choose which AI model answers you |
| `/model` | Show which model is answering right now |
| `/about` | Version, where your data lives, who made it |
| `/cls` | Clear the screen |
| `/help` | List these commands |
| `/reset` | Erase everything and start over |
| `/quit` | Close OpenKey (also `/exit`) |

### `/models`

Lists every free model, with its context size — roughly how much conversation it can hold at once.
Arrow keys to move, Enter to choose.

Picking one pins it until you close OpenKey. Choose **Auto** to let OpenKey pick the best available
model for each message, which is the default and usually what you want.

### `/reset`

Deletes your saved key **and your entire conversation history**, then starts setup again. It tells
you this and asks to confirm first, and never assumes yes.

Use it if your key stopped working or you want to sign in with a different account.

## Why the model sometimes changes

Free models are shared and frequently busy. When one refuses, OpenKey quietly moves to another and
carries on — you just get your answer. If that happened you'll see a small note like *"Moved past 2
busy models."*

This is normal and is the main thing OpenKey does for you. If **every** free model is busy at once,
it says so and suggests waiting a moment.

## Where your data lives

`%APPDATA%\OpenKey\` — usually `C:\Users\<you>\AppData\Roaming\OpenKey`. `/about` shows the exact
path.

| File | What it is |
|---|---|
| `key.bin` | Your key, encrypted for your Windows account |
| `session.json` | Your conversation |
| `models.cache.json` | The model list, refreshed daily |
| `rotation.state.json` | Which models are busy |

Your key is encrypted so that only your Windows account on this PC can read it — copying the file
to another machine gets someone nothing. **Your conversation is not encrypted**, so anyone who can
use your Windows account can read it.

OpenKey talks to OpenRouter and nowhere else. No analytics, no tracking, ever.

## When something goes wrong

Every error says what happened and what to do next. The common ones:

**"Your key was refused."** The key was revoked or replaced. `/reset` and sign in again.

**"This key is out of credit."** Your free allowance is used up. Wait for it to renew, or add
credit at OpenRouter.

**"Can't reach OpenRouter."** Usually your connection. If you're on hotel, airport or café Wi-Fi,
you probably still need to sign in to the network itself in a browser — OpenKey will tell you when
it detects that.

**"Every free model is busy right now."** Wait a minute and resend, or use `/models` to pick one
directly.

**"Another app is using the sign-in port."** Something else holds port 3000, which OpenKey needs
briefly during browser sign-in. Close it, or paste a key instead.

**Boxes or question marks instead of symbols.** You're in the older console. OpenKey normally
detects this and uses plain characters; if it slips through, run it from Windows Terminal.

**The window closes instantly.** It shouldn't — OpenKey waits for a keypress before closing when
you've double-clicked it. If it still happens, run it from a terminal to see the message.

## Questions

**Does it cost anything?** No. OpenKey only ever uses free models.

**Do I need an internet connection?** Yes, to reach OpenRouter.

**Can I use it on another PC?** Yes, but you'll sign in again — the saved key is deliberately tied
to one Windows account on one machine.

**Can I run it from a USB stick?** Yes. That's the intended way. Your data still goes to
`%APPDATA%` on whichever PC you use.

**Is my conversation sent anywhere?** Only to OpenRouter, to generate replies. Nowhere else.

**How do I start a fresh conversation without losing my key?** Not possible yet — `/reset` is
currently all-or-nothing. A `/new` command is planned; see [`../BACKLOG.md`](../BACKLOG.md).
