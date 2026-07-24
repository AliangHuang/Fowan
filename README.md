# Fowan

Fowan Orchestrates Workflows with AI, Natively.

## Repository layout

```text
apps/windows/
  platform/contracts/ # WinUI-free process, clipboard, dialog and dispatcher ports
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
build/                 # Ignored development build, test and staging output
publish/               # Ignored versioned delivery packages
```

Shared platform contracts stay intentionally small. Application-specific process
lifecycle operations live under each application's `Application/Ports`, while
all `Process`, picker, clipboard, tray, window-host, and native implementations
live under `Platform/Windows`. Toolbox tray commands are routed through pure
application logic before a restore or exit action is raised.

Todo and Diary keep their compatible JSON formats and continue to use
`%LOCALAPPDATA%\Fowan\Todo` and `%LOCALAPPDATA%\Fowan\Diary`. Assembly names,
executable names, and installed `Tools` layout are unchanged by the source-tree
reorganization.

## Architecture and security boundary

The public repository owns the Windows UI, local Todo/Diary behavior, and the
versioned AI client contract. The private sibling `FowanCore` repository owns
provider traffic, endpoint consent enforcement, credentials, protected chat
history, migrations, and orchestration. Read [repository boundaries](docs/repository_boundaries.md),
the [Windows application architecture](docs/windows_application_architecture.md),
and the [AI v0.1 protocol](protocol/ai/v0.1/README.md) before changing this boundary.
All contributions also follow [CONTRIBUTING.md](CONTRIBUTING.md). New tools and modules require
an accepted [design proposal](docs/design-proposals/README.md) and component-manifest entry
before implementation.

Every Core connection must complete `engine.handshake` before other methods.
Only normalized HTTPS endpoints, or loopback HTTP endpoints, are accepted.
Consent is keyed by that normalized endpoint and is enforced again at every
Core network exit. API secrets stay in Windows Credential Manager; conversation
content is stored in SQLite only after current-user DPAPI protection.

## Build and verification

Ordinary MSBuild output is written under ignored `build/msbuild/` and never
creates a release package:

```powershell
dotnet build .\Fowan.sln -c Debug
dotnet build .\Fowan.sln -c Release
.\scripts\verify.ps1
```

The verify script requires a stable .NET SDK 8.0.422 or newer (CI exercises the
8.0.422 minimum while local development may use a newer stable SDK) and runs restore, Debug and
Release builds and tests, JSON Schema validation for every declared protocol
fixture, documentation-link and retired-path checks, tracked-artifact checks,
deterministic protocol regeneration, staging/backup residue checks, platform
boundary scans, and a before/after worktree cleanliness check. It never installs
or changes a toolchain.

Runnable outputs are created only by explicit build scripts. The compatibility
entry points below all compose the same complete application tree at
`build/windows/win-x64/app/`; no tool gets a separate runnable output directory.
Release is still a normal build unless `-Publish` is supplied:

```powershell
.\scripts\build-windows.ps1 -Configuration Debug
.\scripts\build-windows-todo.ps1 -Configuration Debug
.\scripts\build-windows-diary.ps1 -Configuration Debug
.\scripts\build-windows-ai.ps1 -Configuration Debug
```

Development executables in this tree use a `.Dev.exe` suffix, including
`Fowan.Windows.Dev.exe` and `Core/fowan-core.Dev.exe`. Release packaging builds
the production executable names independently. Before replacing the development
tree, the build stops only matching `.Dev` processes whose executable paths are
inside that tree, then restarts the applications that were running. The first
build after this naming migration also recognizes legacy unsuffixed executables
inside the development tree; installed production processes are never selected.

Fowan consumes Core only from
`..\FowanCore\out\core\windows\win-x64\<configuration>\fowan-core.exe`, or from
an explicit `-CoreArtifactPath`. It does not search Cargo `target` directories.
Build scripts do not stop running applications; a locked output produces an
explicit error so the corresponding stop/run workflow remains a separate step.
All temporary output is created below `build/staging/<component>/<guid>/`;
the scripts pass an absolute, trailing-separator directory to MSBuild and remove
the isolated staging directory on every exit path. Output installation uses a
tested backup/replace/rollback exchange; if both replacement and restoration
fail, the error reports both phases and leaves the backup available for recovery.
Use `scripts/stop-windows.ps1 -Component <Toolbox|Todo|Diary|Ai|All>` only when
you intentionally want to stop a running component.

Startup update checks are owned by an application coordinator. Window close
cancels without blocking shutdown, while cancellation, dispatcher rejection,
dialog failure, and check failure all complete with an observed outcome.

## Packaging

Installer creation is an explicit release-only workflow and is not run by CI:

```powershell
.\scripts\package-windows.ps1 -Version 0.2.2 -CoreArtifactPath ..\FowanCore\out\core\windows\win-x64\release\fowan-core.exe -SkipInstaller
.\scripts\package-windows.ps1 -Version 0.2.2 -CoreArtifactPath ..\FowanCore\out\core\windows\win-x64\release\fowan-core.exe
```

The first command is a preflight only: it cleans its isolated staging directory
and never creates a `publish/` version. A successful release atomically writes
only these deliverables to `publish/windows/win-x64/<version>/`: the installer,
update manifest, and SHA-256 manifest. Before that atomic write,
the script checks version retention and keeps only the newest four published
versions; expired versions are restored automatically if publication fails.

See [Windows installer specification](docs/windows_installer_spec.md) for the
stable installation layout and update behavior.
