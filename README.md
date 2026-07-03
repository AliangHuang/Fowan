# Fowan

Fowan Orchestrates Workflows with AI, Natively.

## Repository Layout

```text
apps/
  windows/        # WinUI 3 Windows desktop client
assets/
  brand/          # Brand source assets and generated platform icons
  design/windows/ # Windows UI reference images
core/             # Future Rust cross-platform core workspace
docs/             # Architecture and product design documents
scripts/          # Local build and run helpers
out/              # Local build outputs, ignored by git
```

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
