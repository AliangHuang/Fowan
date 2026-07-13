---
name: publish-fowan-release
description: Package and publish a Fowan Windows GitHub Release. Use when preparing or releasing a Fowan version, generating the Inno Setup installer and fowan-update.json, pushing release commits/tags, creating GitHub Releases, uploading release assets, or diagnosing GitHub Release asset upload failures.
---

# Publish Fowan Release

## Overview

Use this workflow for Fowan stable Windows releases. A release is complete only when the code commit, `v<version>` tag, GitHub Release, installer asset, and `fowan-update.json` asset all exist and the latest manifest is publicly readable.

Project defaults:

- Repository: `AliangHuang/Fowan`
- Release tag: `v<version>`
- Installer asset: `FowanSetup-<version>-win-x64.exe`
- Update manifest asset: `fowan-update.json`
- Latest manifest URL: `https://github.com/AliangHuang/Fowan/releases/latest/download/fowan-update.json`

## Preflight

1. Confirm the repo root is the inner checkout, usually `D:\Dev\FowanWorkSpace\Fowan`.
2. Confirm `origin` points to `https://github.com/AliangHuang/Fowan.git`.
3. Confirm the repository is public if the released build is expected to update installed clients without a GitHub token. A private repository can have uploaded release assets, but public `github.com/.../releases/latest/download/...` URLs return 404 to the updater.
4. Confirm the requested version is reflected in:
   - `Directory.Build.props`
   - `apps/windows/Models/ToolCatalog.cs`
   - `changelogs/toolbox/CHANGELOG.md`
   - `changelogs/tools/todo/CHANGELOG.md` when the bundled Todo tool ships with the toolbox
   - `changelogs/tools/diary/CHANGELOG.md` when the bundled Diary tool ships with the toolbox
5. Search for stale release repository URLs:

```powershell
rg -n "aliang1016|github\.com/.*/Fowan|AliangHuang/Fowan" .
```

## Release Notes Accuracy Gate

Do not package or publish until the current-version changelog text has been checked against the actual component diff. A version heading alone is not sufficient evidence that release notes are ready.

Run the checked-in gate first:

```powershell
.\.codex\skills\publish-fowan-release\scripts\Test-ReleaseNotes.ps1 -Version <version>
```

The gate compares the working tree with the most recent earlier version tag, maps changed paths to Toolbox, Todo, and Diary, requires a current-version section with release-note bullets, and blocks no-change placeholder wording for a changed component. If a reviewed component truly contains only non-observable refactoring, tests, packaging metadata, or documentation maintenance, record that reasoning and rerun with an explicit override such as `-AllowNoUserVisibleChanges Diary`.

1. Resolve the previous release tag and inspect changes by component, including staged and unstaged work:

```powershell
$previousTag = git describe --tags --abbrev=0
git diff --stat $previousTag -- apps/windows apps/windows-todo apps/windows-todo-sticky apps/windows-todo-shared
git diff --stat $previousTag -- apps/windows-diary apps/windows-diary-shared
git diff --stat $previousTag -- installer scripts
```

2. Read each current-version changelog section as UTF-8 and compare every user-visible behavior in the diff with the notes:

```powershell
Get-Content changelogs/toolbox/CHANGELOG.md -Encoding UTF8 | Select-Object -First 20
Get-Content changelogs/tools/todo/CHANGELOG.md -Encoding UTF8 | Select-Object -First 20
Get-Content changelogs/tools/diary/CHANGELOG.md -Encoding UTF8 | Select-Object -First 20
```

3. Map component changes to their changelog:
   - `apps/windows-todo*`, `apps/windows-todo-shared`, Todo tests, and Todo requirements/design docs require review of the Todo section.
   - `apps/windows-diary*`, `apps/windows-diary-shared`, Diary tests, and Diary requirements/design docs require review of the Diary section.
   - `apps/windows`, installer behavior, update behavior, and bundled-tool changes require review of the toolbox section.
4. Treat phrases such as “no user-visible changes”, “随发布包更新”, “maintenance release”, or equivalent placeholder wording as a release blocker whenever that component has UI, interaction, workflow, settings, storage-format, lifecycle, installer, or other observable behavior changes.
5. “No user-visible changes” is allowed only after the component diff has been inspected and is limited to packaging/version metadata, internal refactoring with equivalent behavior, tests, or documentation-only maintenance. Record that conclusion in the release handoff.
6. Before proceeding, summarize the concrete user-visible changes from each affected component. The GitHub Release notes must be generated from these validated current-version changelog sections, not from memory or a previous release.

## Build And Package

Run the local dotnet executable; it is the reliable build entrypoint for this project:

```powershell
git diff --check
& "$env:USERPROFILE\.dotnet\dotnet.exe" build Fowan.sln --nologo
.\scripts\build-windows.ps1 -Configuration Debug
.\scripts\package-windows.ps1 -Version <version>
```

After packaging, verify both files exist:

```powershell
Test-Path "out\installer\windows\win-x64\FowanSetup-<version>-win-x64.exe"
Test-Path "out\installer\windows\win-x64\fowan-update.json"
Get-Content "out\installer\windows\win-x64\fowan-update.json" -Raw
Get-FileHash "out\installer\windows\win-x64\FowanSetup-<version>-win-x64.exe" -Algorithm SHA256
```

The manifest must use `channel: stable`, point at the `AliangHuang/Fowan` release asset URL, and contain a SHA-256 that matches the installer.

## Commit And Tag

Commit only source, docs, scripts, and project metadata. Do not add ignored generated output under `out/`.

```powershell
git status --short --branch
git add -A
git diff --cached --stat
git commit -m "feat: add GitHub release update checks"
git tag -a v<version> -m "Fowan <version>"
git push origin main v<version>
```

Adjust the commit message to match the actual release changes.

## Create The GitHub Release

Prefer `gh` when available:

```powershell
gh release create v<version> `
  "out\installer\windows\win-x64\FowanSetup-<version>-win-x64.exe" `
  "out\installer\windows\win-x64\fowan-update.json" `
  --repo AliangHuang/Fowan `
  --title "Fowan <version>" `
  --notes-file <release-notes-file>
```

If `gh` is unavailable, use the GitHub REST API with an authenticated token from Git Credential Manager or another secure source. Never print the token. Create the release through `api.github.com`, then upload assets through the `upload_url` returned by the release response.

Release notes must mention the validated user-visible changes for every affected component and the update mechanism when relevant. After creating or editing a release, read the release body back from GitHub and compare it with the current changelog sections before declaring success.

## Correct An Already Published Release

When binaries are correct but release notes are incomplete or inaccurate:

1. Correct the relevant current-version changelog sections in the repository.
2. Commit and push the documentation/workflow correction to `main`.
3. Update the existing GitHub Release body in place from the corrected changelog sections.
4. Do not move or recreate the existing tag, and do not rebuild or re-upload unchanged installer/manifest assets.
5. Read the release body back through the GitHub API or release page and verify the corrected text is publicly visible.
6. Re-run the public manifest and asset checks so the correction does not accidentally disturb the release.

## Asset Upload Checklist

Upload exactly these assets:

- `FowanSetup-<version>-win-x64.exe`
- `fowan-update.json`

If retrying a partially failed upload, list existing assets first and delete same-name assets before re-uploading. A GitHub asset in `starter` state is incomplete and must be deleted.

Treat the release as incomplete until both assets are visible on the release page.

When the normal upload path fails but GitHub API and push are otherwise authenticated, use this Windows fallback:

```powershell
# Use DNS-over-HTTPS or another trusted resolver to find real GitHub IPs.
# These values worked from this environment on 2026-07-07; refresh them when reusing.
$apiIp = "20.205.243.168"
$uploadIp = "20.205.243.161"
$owner = "AliangHuang"
$repo = "Fowan"
$tag = "v<version>"
$installer = "out/installer/windows/win-x64/FowanSetup-<version>-win-x64.exe"
$manifest = "out/installer/windows/win-x64/fowan-update.json"

$env:GCM_INTERACTIVE = "Never"
$credText = "protocol=https`nhost=github.com`n`n" | git credential fill
$token = ($credText | Where-Object { $_ -like "password=*" } | Select-Object -First 1).Substring(9)

$apiArgs = @(
  "--silent", "--show-error", "--fail-with-body", "--ssl-no-revoke", "--http1.1",
  "--resolve", "api.github.com:443:$apiIp",
  "-H", "Authorization: Bearer $token",
  "-H", "Accept: application/vnd.github+json",
  "-H", "X-GitHub-Api-Version: 2022-11-28",
  "-H", "User-Agent: FowanReleasePublisher"
)

$release = ((& curl.exe @apiArgs "https://api.github.com/repos/$owner/$repo/releases/tags/$tag") | Out-String | ConvertFrom-Json)

# Delete same-name or starter assets before re-uploading.
$assets = ((& curl.exe @apiArgs "https://api.github.com/repos/$owner/$repo/releases/$($release.id)/assets") | Out-String | ConvertFrom-Json)
foreach ($asset in @($assets)) {
  if ($asset.name -in @([IO.Path]::GetFileName($installer), [IO.Path]::GetFileName($manifest))) {
    & curl.exe @apiArgs -X DELETE "https://api.github.com/repos/$owner/$repo/releases/assets/$($asset.id)" | Out-Null
  }
}

# Keep SNI/Host as uploads.github.com, but bypass bad local DNS.
function Upload-Asset($path, $contentType) {
  $name = [IO.Path]::GetFileName($path)
  $uri = "https://uploads.github.com/repos/$owner/$repo/releases/$($release.id)/assets?name=$([Uri]::EscapeDataString($name))"
  & curl.exe `
    --silent --show-error --fail-with-body --ssl-no-revoke --http1.1 `
    --connect-timeout 60 --max-time 1800 --retry 5 --retry-all-errors --retry-delay 10 `
    --resolve "uploads.github.com:443:$uploadIp" `
    -X POST `
    -H "Authorization: Bearer $token" `
    -H "Accept: application/vnd.github+json" `
    -H "X-GitHub-Api-Version: 2022-11-28" `
    -H "User-Agent: FowanReleasePublisher" `
    -H "Content-Type: $contentType" `
    --data-binary "@$path" `
    $uri
}

Upload-Asset $installer "application/octet-stream"
Upload-Asset $manifest "application/json"
```

Use `--ssl-no-revoke` only as a Windows network workaround when Schannel reports revocation-check failures against otherwise valid GitHub TLS endpoints.

## Network Failure Lessons

Git push, GitHub API release creation, and GitHub asset uploads use different endpoints. Do not assume one working path proves all paths work:

- Git push uses `github.com`.
- Release metadata uses `api.github.com`.
- Asset uploads use `uploads.github.com`.

Known failure signatures:

- `schannel: failed to receive handshake`
- `TLS connect error: unexpected eof while reading`
- `Client network socket disconnected before secure TLS connection was established`
- `read ECONNRESET`
- Asset appears as `starter` after a failed upload

When upload fails:

1. Check DNS for `uploads.github.com`.
2. If it resolves to a private, loopback, or benchmarking range such as `198.18.0.0/15`, fix VPN/proxy/DNS/hosts first; repeated upload retries are unlikely to succeed.
3. If DNS cannot be fixed immediately, get a real IP via DNS-over-HTTPS and retry with `curl --resolve uploads.github.com:443:<real-ip>`.
4. Delete any `starter` or same-name asset before retrying.
5. Retry the upload from a network where `uploads.github.com` has a working TLS path.

## Private Repository Check

If release assets show `state: uploaded` in the GitHub API but `https://github.com/<owner>/<repo>/releases/download/...` or `/releases/latest/download/...` returns 404, check repository visibility. For Fowan auto-update, private releases are not usable because the client intentionally has no embedded GitHub token.

Check visibility through the API:

```powershell
$repoJson = & curl.exe @apiArgs "https://api.github.com/repos/AliangHuang/Fowan"
$repoInfo = ($repoJson | Out-String) | ConvertFrom-Json
$repoInfo.private
$repoInfo.visibility
```

Interpretation:

- `private: false` and asset download 404: wait briefly for GitHub/CDN propagation, then re-check exact asset names.
- `private: true`: release assets can be uploaded and listed by API, but public latest/download URLs are not usable for tokenless auto-update.
- `state: starter`: upload is incomplete; delete and re-upload.

Do not make a private repository public without explicit user confirmation. Offer two safe options:

- make `AliangHuang/Fowan` public
- keep the source private and publish installer plus `fowan-update.json` from a separate public release repository

## Final Verification

Verify the public release and latest update manifest:

```powershell
git status --short --branch
git ls-remote --tags origin v<version>
Invoke-WebRequest -UseBasicParsing "https://github.com/AliangHuang/Fowan/releases/latest/download/fowan-update.json"
```

Confirm the downloaded manifest:

- has `version` equal to the release version
- has `channel` equal to `stable`
- has `installerUrl` pointing to the release asset
- has `installerSha256` equal to the uploaded installer hash
- can be consumed by the toolbox update checker

Also confirm the GitHub Release body contains the validated current-version toolbox, Todo, and Diary notes without stale placeholder claims.
