# ADR-0007: Share Windows runtimes across the Fowan tool suite

## Status

Accepted

## Amendment

The prerequisite delivery portion is amended by
[ADR-0009](0009-self-contained-windows-runtime-distribution.md).

## Context

Fowan 0.2.0 publishes Toolbox, Todo, Sticky Todo, Diary, Report, AI Chat and AI Configuration as
separate self-contained executables. Each application copies its own .NET runtime and Windows App
SDK native payload into the release tree. The resulting application staging directory is about
1.37 GB even though the suite is installed and updated as one product.

## Decision

Starting with 0.2.1, all Fowan Windows applications are published as framework-dependent
win-x64 applications. They require these machine-level shared prerequisites:

- .NET 8 Desktop Runtime x64;
- Windows App Runtime 2.2 x64; and
- Microsoft Visual C++ Redistributable x64.

The administrator-level Inno Setup installer checks these prerequisites before copying Fowan files.
When one is missing, it shows the exact missing component, offers to open its official Microsoft
download page, and stops. It never downloads, embeds, or silently installs a runtime. Existing
WindowsPackageType=None bootstrap configuration loads the installed Windows App Runtime for each
WinUI application.

Starting with 0.2.2, the installer is the only public Windows delivery. Fowan no longer creates or
uploads a portable archive. Runtime installation and servicing remain the responsibility of the
user or the organization's software-management system.

## Consequences

The release no longer duplicates runtime payloads for every tool or carries prerequisite installers,
reducing delivery size further. A fresh machine must install the required runtimes first; the Fowan
installer makes no application-file changes until those checks pass. Offline or managed deployment
must provision the same runtimes before running Fowan setup.

Fowan continues to target .NET 8 and Windows App Runtime 2.2. Runtime servicing can be delivered
by Microsoft independently of a Fowan release. The prior 0.2.0 self-contained release remains
compatible and is not modified or removed by this decision.
