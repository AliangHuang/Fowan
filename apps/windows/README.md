# Fowan Windows Client

This is the first Windows native client shell for Fowan. It implements the
Toolbox Home experience as an interactive WinUI 3 desktop app.

## Current Scope

- Native WinUI 3 / Windows App SDK desktop app.
- Toolbox shell with category navigation, tool search, tool card grid, and detail panel.
- Available tools: Toolbox Home, Settings, Diagnostics.
- Disabled tools: Quick Capture.
- Planned tools: Todo, Notes, Knowledge, Files, Global Search, Workflows, AI, Plugins.
- Mock engine status and diagnostics data.
- Theme preference: system, light, dark.
- Language preference: system, zh-CN, en-US.
- Local settings file under `%LOCALAPPDATA%\Fowan\client-settings.json`.
- User data root under `%LOCALAPPDATA%\Fowan`.

The app does not connect to the Rust desktop engine yet. Quick Capture is kept
as a disabled tool card until the capture flow is re-enabled.

## Build

Install or make available .NET SDK 8.0.422, then run from the repository root:

```powershell
$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build .\Fowan.sln -c Debug
```

## Run

Use the short-path output helpers from the repository root:

```powershell
.\scripts\build-windows.ps1 -Configuration Debug
.\scripts\run-windows.ps1 -Configuration Debug
```

The executable is published to `out/windows/debug/Fowan.Windows.exe`.
