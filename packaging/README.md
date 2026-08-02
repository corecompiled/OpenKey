# Packaging and distribution

Everything about getting OpenKey onto someone else's machine.

## SmartScreen — the biggest barrier

An unsigned binary triggers *"Windows protected your PC — unrecognized app"*, and the user has to
click **More info → Run anyway**. For software whose whole distribution story is "copy it to a USB
stick and hand it to a friend", that warning costs more adoption than any missing feature.

Two ways to reduce it, in increasing order of cost.

### 1. Reputation submission (free, one form, do this first)

Submit each released binary to Microsoft for review:

<https://www.microsoft.com/en-us/wdsi/filesubmission>

- Choose **Software developer** as the submission type.
- Upload `OpenKey-win-x64.exe` (repeat for `win-arm64`).
- State that it is a false positive: an unsigned open-source .NET console app, source at
  <https://github.com/corecompiled/OpenKey>.

This does not remove the warning immediately. It clears active detections and helps reputation
accumulate as downloads do. It costs nothing but time, so there is no reason not to.

### 2. Code signing (removes it properly)

**Azure Trusted Signing** is the cheap route — roughly $10/month for the individual/small-business
tier, against a few hundred a year for a traditional EV certificate. It requires identity
verification, which takes a few days.

The release workflow is already wired for it. Enable by adding these repository secrets:

| Secret | What |
|---|---|
| `AZURE_TENANT_ID` | Directory tenant |
| `AZURE_CLIENT_ID` | Service principal |
| `AZURE_CLIENT_SECRET` | Service principal secret |
| `AZURE_SIGNING_ENDPOINT` | e.g. `https://eus.codesigning.azure.net` |
| `AZURE_SIGNING_ACCOUNT` | Trusted Signing account name |
| `AZURE_CERT_PROFILE` | Certificate profile name |

Then set the repository variable `SIGNING_ENABLED` to `true`. The signing step runs before
checksums are computed, since signing changes the file.

Until those exist the step is skipped and releases ship unsigned, exactly as now.

## Scoop

`packaging/scoop/openkey.json` installs the released binary:

```
scoop install https://raw.githubusercontent.com/corecompiled/OpenKey/main/packaging/scoop/openkey.json
```

Installing through Scoop sidesteps SmartScreen entirely, which is a large part of why it is worth
having.

To offer the shorter `scoop install openkey`, the manifest needs to live in a bucket — either
submitted to [`ScoopInstaller/Extras`](https://github.com/ScoopInstaller/Extras) or published as
`corecompiled/scoop-bucket`. Extras generally wants a package with some existing usage, so a
personal bucket is the sensible first step.

`checkver` and `autoupdate` are configured, so a new tag is picked up automatically. Autoupdate
reads `OpenKey-<version>-checksums.txt`, which the release workflow publishes as a release asset.

### After each release

Update `version` and both hashes. From a release directory:

```pwsh
Get-FileHash OpenKey-win-x64.exe -Algorithm SHA256
```

Or take them straight from the `checksums.txt` asset.

## winget

Worth doing once there is download history — `microsoft/winget-pkgs` involves a manifest PR and a
review cycle, which is more friction than Scoop for the same benefit. Revisit after a release or
two.

## What is deliberately not done

**No auto-update installer, in any phase.** Check-and-notify only. A tool that silently replaces
its own binary is a tool people are right to distrust, and it fights the "one file you can copy
anywhere" model. This is a standing project rule.
