# Fowan

Fowan Orchestrates Workflows with AI, Natively.

## Repository Layout

```text
apps/
  windows/             # WinUI 3 toolbox shell and launcher
  windows-todo/        # Independent WinUI 3 todo window app
  windows-todo-core/   # Shared todo models, storage, and services
  windows-todo-sticky/ # Independent sticky todo shell
assets/
  brand/          # Brand source assets and generated platform icons
  design/windows/ # Windows UI reference images
core/             # Future Rust cross-platform core workspace
docs/             # Architecture and product design documents
scripts/          # Local build and run helpers
out/              # Local build outputs, ignored by git
```

## Development Conventions

`apps/windows` is the Fowan toolbox shell. It may show Todo as an available tool
and launch it, but it must not host Todo business UI or Todo task-management
logic.

Todo is an independent Windows tool with its own process, windows, data files,
and build entrypoint. Shared Todo behavior belongs in `apps/windows-todo-core`.
The sticky shell remains a separate executable so it can start directly when the
last saved Todo mode is sticky.

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
.\scripts\package-windows.ps1 -Version 0.1.2 -SkipInstaller
```

Build the final setup executable on a machine with Inno Setup 6 installed:

```powershell
.\scripts\package-windows.ps1 -Version 0.1.2
```

The setup executable and GitHub Release update manifest are written to:

```text
out/installer/windows/win-x64/FowanSetup-0.1.2-win-x64.exe
out/installer/windows/win-x64/fowan-update.json
```

For auto-update checks, upload both files to a public GitHub Release tagged
`v0.1.2`. The toolbox reads
`https://github.com/AliangHuang/Fowan/releases/latest/download/fowan-update.json`
on startup when automatic update checks are enabled.
