# ADR-0012: User-managed portable tool installations

- Status: Accepted

## Context

Tool Center must manually install and manage the available yym Windows x64 portable release.
Its data lives separately in `%LOCALAPPDATA%/yym`; copying only its EXE is insufficient.

## Decision

`ToolCenterSession` owns release, installation, busy/progress and outcome snapshots through
typed commands. Network and installation ports keep all transport, file and process access
in `Platform/Windows`. The only supported package identity is AliangHuang/yym's stable
`yym-<version>-win-x64.zip`, verified against SHA-256 metadata before extraction.

Use an owned per-tool root with a root-bound ownership marker, GUID version directories,
staging and an atomically replaced V1 installation receipt. Persist location selection under
Fowan's ToolCenter data root. An exclusive file lease coordinates production/development
processes. Never traverse filesystem links or accept arbitrary package URLs/executables.
Do not publish a new receipt until the complete portable tree is valid; the old version
remains intact until publication. No automatic update, execution, elevation or forced kill.

Default uninstall removes only managed program/cache/residual files and preserves upstream
user data. A separate explicit checkbox authorizes deletion of the fixed yym data directory;
external input files, exports and MuMu directories are outside that cleanup boundary.

## Consequences

Program files are independent of Fowan binaries and can be replaced without a Fowan release.
Installation location can be selected before installation. Changing it after installation
requires uninstall/reinstall while retaining data. Corruption or ambiguous ownership fails
explicitly instead of adopting or deleting foreign directories. The initial pinned release
metadata works offline; checking or installing newer versions remains an explicit user action.
