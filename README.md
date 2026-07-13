# Fowan

Fowan Orchestrates Workflows with AI, Natively.

## Repository Layout

```text
apps/
  windows/             # WinUI 3 toolbox shell and launcher
  windows-todo/        # Independent WinUI 3 todo window app
  windows-todo-shared/ # Open-source shared Todo models, storage, and services
  windows-todo-sticky/ # Independent sticky todo shell
  windows-diary/       # Independent WinUI 3 diary window app
  windows-diary-shared/ # Open-source shared Diary models, storage, and services
assets/
  brand/          # Brand source assets and generated platform icons
  design/windows/ # Windows UI reference images
docs/             # Architecture and product design documents
scripts/          # Local build and run helpers
out/              # Local build outputs, ignored by git
```

## Development Conventions

`apps/windows` is the Fowan toolbox shell. It may show Todo as an available tool
and launch it, but it must not host Todo business UI or Todo task-management
logic.

Todo is an independent Windows tool with its own process, windows, data files,
and build entrypoint. Shared Todo behavior belongs in `apps/windows-todo-shared`.
The sticky shell remains a separate executable so it can start directly when the
last saved Todo mode is sticky.

The `Shared` projects are part of this GPL-3.0 open-source client. They contain
ordinary tool models, local JSON storage, queries, attachments, location, and
weather integrations; they are not the proprietary Fowan core.

Future AI orchestration, model strategy, user-information encryption, key
management, sensitive indexing, and commercial policy belong in the private
`FowanCore` repository. See `docs/repository_boundaries.md` before adding a new
cross-cutting or security-sensitive capability. No public protocol is defined
until a concrete private-core use case exists.

Build validation must finish with 0 warnings and 0 errors. Any compiler, restore,
XAML, package audit, analyzer, or script warning must be fixed even when it is
outside the current code-change scope.

## Windows Client

Build from the repository root:

```powershell
$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build .\Fowan.sln -c Debug
```

Create the short-path runnable output:

```powershell
.\scripts\build-windows.ps1 -Configuration Debug
```

Run the client:

```powershell
.\scripts\run-windows.ps1 -Configuration Debug
```

The executable is published to:

```text
out/windows/debug/Fowan.Windows.exe
```

## Windows Todo Tool

Default local validation flow for Todo development:

1. Stop any running `Fowan.Todo.Windows`, `Fowan.Todo.Sticky.Windows`, or related Fowan processes so build outputs are not locked.
2. Build the Todo output with `.\scripts\build-windows-todo.ps1 -Configuration Debug`.
3. Launch the freshly built artifact with `.\scripts\run-windows-todo.ps1 -Configuration Debug`.

Build the independent Todo tool and sticky shell:

```powershell
.\scripts\build-windows-todo.ps1 -Configuration Debug
```

Run the Todo tool:

```powershell
.\scripts\run-windows-todo.ps1 -Configuration Debug
```

Todo build outputs use a single runnable artifact directory. The toolbox also
launches Todo from this directory; do not use `apps/windows-todo*/bin` as a
runnable Todo artifact path.

The executables are written to:

```text
out/windows-todo/debug/Fowan.Todo.Windows.exe
out/windows-todo/debug/Fowan.Todo.Sticky.Windows.exe
```

## Windows Installer

The Windows installer design is tracked in
`docs/windows_installer_spec.md`. It defines the install directory layout,
update flow, privacy agreement, shortcuts, and uninstall data handling.

Build the offline x64 installer staging output from the repository root:

```powershell
.\scripts\package-windows.ps1 -Version 0.1.4 -SkipInstaller
```

Build the final setup executable on a machine with Inno Setup 6 installed:

```powershell
.\scripts\package-windows.ps1 -Version 0.1.4
```

The setup executable and GitHub Release update manifest are written to:

```text
out/installer/windows/win-x64/FowanSetup-0.1.4-win-x64.exe
out/installer/windows/win-x64/fowan-update.json
```

For auto-update checks, upload both files to a public GitHub Release tagged
`v0.1.4`. The toolbox reads
`https://github.com/AliangHuang/Fowan/releases/latest/download/fowan-update.json`
on startup when automatic update checks are enabled.
