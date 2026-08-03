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
- **Network traffic** goes to `openrouter.ai`, plus one request to `api.github.com` at launch to
  ask whether a newer release exists. The only other connection OpenKey opens is a local
  `http://localhost:3000/callback` listener, briefly, during browser sign-in.
- **No telemetry.** No analytics, no crash reporting, no usage data, in any phase. This is a
  standing project rule, not a current default.

### About the update check

It is worth being precise, because "checks for updates" and "phones home" can look alike.

The check is an unauthenticated `GET` of a public page — the same URL a browser would open — and
sends no identifier, no key, no version history and no usage data. It cannot be correlated with an
account because no account is involved.

It does, however, reveal to GitHub that *someone at your IP launched OpenKey*. That is a real
disclosure, small but not nothing, so it is declared here rather than buried, and you can switch it
off:

```json
{ "checkForUpdates": false }
```

in `%APPDATA%\OpenKey\config.json`.

**Nothing is ever downloaded or installed automatically.** OpenKey tells you a version exists and
gives you the link. A tool that replaces its own binary is a tool you are right to distrust, and no
phase of this project will add one.

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
