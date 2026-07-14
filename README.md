# Fowan

Fowan Orchestrates Workflows with AI, Natively.

## Repository layout

```text
apps/windows/
  toolbox/             # WinUI 3 toolbox shell and launcher
  todo/
    contracts/         # Stable Todo JSON contract shared with Diary
    shared/            # Todo storage and business services
    app/               # Todo window executable
    sticky/            # Sticky window executable
  diary/
    shared/            # Diary storage and business services
    app/               # Diary window executable
  ai/
    shared/            # Public v0.1 client DTOs and IPC client
    chat/              # AI chat executable
    config/            # AI configuration executable
    ui/                # Shared AI visual resources
protocol/ai/v0.1/      # Public JSON-RPC contract, schema, and fixtures
artifacts/             # Ignored ordinary build/intermediate output
out/                   # Explicit runnable/publish output only
```

Todo and Diary keep their compatible JSON formats and continue to use
`%LOCALAPPDATA%\Fowan\Todo` and `%LOCALAPPDATA%\Fowan\Diary`. Assembly names,
executable names, and installed `Tools` layout are unchanged by the source-tree
reorganization.

## Architecture and security boundary

The public repository owns the Windows UI, local Todo/Diary behavior, and the
versioned AI client contract. The private sibling `FowanCore` repository owns
provider traffic, endpoint consent enforcement, credentials, protected chat
history, migrations, and orchestration. Read [repository boundaries](docs/repository_boundaries.md)
and the [AI v0.1 protocol](protocol/ai/v0.1/README.md) before changing this boundary.

Every Core connection must complete `engine.handshake` before other methods.
Only normalized HTTPS endpoints, or loopback HTTP endpoints, are accepted.
Consent is keyed by that normalized endpoint and is enforced again at every
Core network exit. API secrets stay in Windows Credential Manager; conversation
content is stored in SQLite only after current-user DPAPI protection.

## Build and verification

Ordinary builds write to ignored `artifacts/build/<project>/<configuration>` and
never create a release package:

```powershell
dotnet build .\Fowan.sln -c Debug
dotnet build .\Fowan.sln -c Release
.\scripts\verify.ps1
```

The verify script requires a stable .NET SDK 8.0.422 or newer (CI exercises the
8.0.422 minimum while local development may use a newer stable SDK) and runs restore, Debug and
Release builds and tests, JSON Schema validation for every declared protocol
fixture, documentation-link and retired-path checks, tracked-artifact checks,
and a before/after worktree cleanliness check. It never installs or changes a
toolchain.

Runnable outputs are created only by explicit build scripts. Release is still a
normal build unless `-Publish` is supplied:

```powershell
.\scripts\build-windows.ps1 -Configuration Debug
.\scripts\build-windows-todo.ps1 -Configuration Debug
.\scripts\build-windows-diary.ps1 -Configuration Debug
.\scripts\build-windows-ai.ps1 -Configuration Debug
```

Fowan consumes Core only from
`..\FowanCore\out\core\windows\win-x64\<configuration>\fowan-core.exe`, or from
an explicit `-CoreArtifactPath`. It does not search Cargo `target` directories.
Build scripts do not stop running applications; a locked output produces an
explicit error so the corresponding stop/run workflow remains a separate step.
Use `scripts/stop-windows.ps1 -Component <Toolbox|Todo|Diary|Ai|All>` only when
you intentionally want to stop a running component.

## Packaging

Installer creation is an explicit release-only workflow and is not run by CI:

```powershell
.\scripts\package-windows.ps1 -Version 0.1.4 -CoreArtifactPath ..\FowanCore\out\core\windows\win-x64\release\fowan-core.exe -SkipInstaller
.\scripts\package-windows.ps1 -Version 0.1.4 -CoreArtifactPath ..\FowanCore\out\core\windows\win-x64\release\fowan-core.exe
```

See [Windows installer specification](docs/windows_installer_spec.md) for the
stable installation layout and update behavior.
