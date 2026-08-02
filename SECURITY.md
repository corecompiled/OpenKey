# Security

## Reporting a vulnerability

Open a [private security advisory](https://github.com/corecompiled/OpenKey/security/advisories/new)
on the repository. Please don't file a public issue for anything exploitable.

Include what you did, what happened, and what you expected. A proof of concept helps.

## What OpenKey does with your data

- **Your API key** is encrypted with Windows DPAPI under `DataProtectionScope.CurrentUser` and
  written to `%APPDATA%\OpenKey\key.bin`. Only the same Windows account on the same machine can
  decrypt it. Copying the file to another PC or another user account yields nothing usable.
- **Your conversations** are stored in plain JSON at `%APPDATA%\OpenKey\session.json`. They are not
  encrypted. Anyone with access to your Windows account can read them. `/reset` deletes them.
- **Network traffic** goes to `openrouter.ai` and nowhere else. The only other connection OpenKey
  ever opens is a local `http://localhost:3000/callback` listener, briefly, during browser sign-in.
- **No telemetry.** No analytics, no crash reporting, no phone-home, in any phase. This is a
  standing project rule, not a current default.

## Threat model

OpenKey is a single-user desktop application. It assumes the Windows account it runs under is
trusted. It does not defend against:

- Another process running as the same user (DPAPI cannot help here — that process can decrypt the
  key exactly as OpenKey does).
- Physical access to an unlocked machine.
- A malicious OpenRouter endpoint, beyond ordinary TLS certificate validation.

## Handling of model output

Model replies are untrusted input and are treated as such. Every literal is escaped before it
reaches the console renderer, so a reply cannot inject console markup, forge UI chrome, or emit
control sequences. The fallback path taken when markdown parsing fails re-escapes as well.

## Supported versions

OpenKey is pre-1.0. Only the latest release receives fixes.
