---
name: publish-fowan-release
description: Prepare, package, validate, and publish a user-confirmed Fowan Windows GitHub Release. Use only when the user explicitly asks to prepare or publish a precise release version, create installer or portable ZIP delivery artifacts, push release commits or tags, create GitHub Releases, upload release assets, or diagnose release upload failures. Do not use for routine builds, tests, or repairs.
---

# Publish Fowan Release

Use this workflow only after the user explicitly confirms the exact version. Never infer, increment, or pre-fill a release version. Routine build, test, and repair work must not run `scripts/package-windows.ps1`, including `-SkipInstaller`.

## Release gate

1. Inspect the complete bundled-tool diff since the previous release tag and update every affected current-version changelog section with concrete user-visible behavior.
2. Confirm the configured repository is `AliangHuang/Fowan`, release tag is `v<version>`, and public release access is appropriate for tokenless updates.
3. Run the release-note gate:

```powershell
.\.agents\skills\publish-fowan-release\scripts\Test-ReleaseNotes.ps1 -Version <version>
```

4. Run `git diff --check`, build the solution, then package with the confirmed version and a release Core artifact:

```powershell
.\scripts\package-windows.ps1 -Version <version> -CoreArtifactPath ..\FowanCore\out\core\windows\win-x64\release\fowan-core.exe
```

`-SkipInstaller` is only a temporary preflight and must not create a publish version.

## Delivery validation

The release directory is `publish\windows\win-x64\<version>\`. It must contain only:

- `FowanSetup-<version>-win-x64.exe`
- `Fowan-<version>-portable.zip`
- `fowan-update.json`
- `SHA256SUMS.txt`

The package script retains at most four published versions and restores temporarily removed older versions if the new atomic write fails. Run the package gate before committing, tagging, or uploading:

```powershell
.\.agents\skills\publish-fowan-release\scripts\Test-PackagedRelease.ps1 -Version <version>
```

The portable ZIP must contain one `Fowan-<version>-portable` root with `app/`, `app/Core/fowan-core.exe`, all Tools, release notes, `prerequisites/vc_redist.x64.exe`, and `README.txt`.

## Publish

Commit only source, docs, scripts, and metadata; `build/` and `publish/` are ignored. Create and push `v<version>` only when the user asks to publish. Upload all four delivery files to the GitHub Release, then read back the public `fowan-update.json` and verify its installer hash and URL. Never expose credentials or make a private repository public without explicit user confirmation.
