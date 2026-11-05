---
id: DP-0006
status: accepted
title: Managed manual installation of the first Tool Center application
components:
  - tool-center-windows
adrs:
  - docs/adr/0012-managed-tool-installations.md
---

# Problem and users

The user requested AliangHuang/yym (痒痒猫) as the first available Tool Center tool,
with manual download/installation, managed installation location, cleanup, uninstall and
ordinary management actions. The published stable v0.10.0 provides a Windows x64 portable
ZIP and a SHA-256 manifest. Its complete `yym` directory, runtime and licenses must be kept.

## Goals and non-goals

Provide manual install/update/reinstall, cancel, launch, check updates, choose installation
location before installation, open program/user-data directories, open source/release pages,
clear download/residual files, and uninstall with optional user-data cleanup. Nothing downloads
or installs automatically on startup. Do not bundle yym, build or modify its source, add a
generic plugin installer, run external setup scripts, elevate, or change Fowan release versions.

## Repository and component boundaries

Extend `tool-center-windows`; `ToolCenterSession` remains the only business state owner.
Application defines immutable release/installation/progress snapshots and typed commands.
Narrow ports cover GitHub release/download and installation/platform actions. Windows adapters
implement HTTPS transport, validated ZIP extraction, receipts, process/path checks and folder
selection. Presentation subscribes to snapshots and invokes commands; it performs no I/O.
No dependency on FowanCore or another UI assembly and no new package/project is introduced.

## Interfaces and data flow

The first catalog entry is the verified yym stable release. Manual version checking reads
only the fixed repository's latest non-prerelease release and checksum manifest. Exact
`yym-<version>-win-x64.zip` selection excludes source archives. Downloads stream to owned
partial files with progress and cancellation, verify size/SHA-256, then extract into an
isolated stage. Validate the executable, Python/Qt runtime and license files before publishing
an atomic installation receipt. Session publishes installed state after receipt persistence.

## Failure, cancellation, and atomicity

Serialize session commands and acquire an interprocess file lease for filesystem mutations.
Network, checksum, archive, process-lock and permission failures preserve the previous receipt
and installation. Cancellation is allowed before installation publication; temporary stages
are cleaned, and cleanup failures are reported. A committed installation remains installed
even if later optional cleanup fails. Refuse update/uninstall while the managed executable
is running; never stop an external yym instance. Interrupted/partial removal is reported and
can be retried, with ownership metadata kept until all other managed files have been removed.

## Compatibility, migration, and rollback

Default program root is `%LOCALAPPDATA%/Fowan/ToolCenter/Tools/yym`. A user-selected parent
uses its dedicated `FowanTools/yym` child. Location changes are allowed before installation;
installed tools must be uninstalled first, with user data preserved by default. Receipts,
location preferences, operation lock and download cache are additive V1 local files.
Existing Toolbox tools, six excluded planning placeholders, Todo/Diary data and executable
names stay unchanged. No production release or installer packaging is part of this work.

## Security, privacy, dependencies, and permissions

Only the fixed HTTPS GitHub repository and approved GitHub download hosts are used; no tokens
are sent and no package URL is accepted from the UI. Enforce transport/JSON/archive limits,
download SHA-256, exact package identity, Windows path validation, no traversal/ADS/device
names, no archive symlinks, and no filesystem reparse-point traversal. Recursive removal
requires an owned managed root; user-data cleanup is restricted to the fixed yym data root
and explicitly selected in the uninstall dialog. Never read or remove MuMu data/source files.
This guards accidental path escape and malformed packages, not a malicious same-user process
racing local filesystem access. Preserve upstream MIT and bundled third-party license files.
Use existing .NET HTTP/JSON/ZIP and Windows App SDK dependencies. An external setup executable
was rejected because the portable package provides a bounded, reversible managed install.

## Test and acceptance plan

Test release/URL selection, checksum mismatch, truncation, cancellation, malicious ZIP paths,
symlinks, missing runtime files, receipt corruption, foreign/reparse directories, running
processes, install/update publication, repeated install, normal/full uninstall and cache
cleanup. Exercise the actual v0.10.0 ZIP in an isolated test root without launching yym or
reading real user/game data. Run Debug/Release builds, full tests, governance and architecture
checks, rebuild the unified development runtime, and inspect the catalog and manual actions.

## Reuse and duplication analysis

Reuse the Tool Center process, preference localization and Fluent layout. Keep local path,
network and process mechanics behind owning application ports; do not copy Toolbox UI or
create duplicate mutable application state. Tools remain independently downloaded rather
than being added to the Fowan installer payload.
