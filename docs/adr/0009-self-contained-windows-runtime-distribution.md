# ADR-0009: Return Windows applications to self-contained deployment

## Status

Accepted

## Context

ADR-0007 made the Windows applications framework-dependent. Fowan 0.2.1 and 0.2.2 carried
installers for .NET 8 Desktop Runtime x64, Windows App Runtime 2.2 x64, and Microsoft Visual C++
Redistributable x64, while later packaging required users or managed environments to provision
those runtimes separately.

Both framework-dependent approaches leave application startup dependent on the target machine's
.NET and Windows App Runtime discovery. Carrying the runtime installers does not eliminate failures
caused by an incomplete registration, architecture mismatch, custom runtime location, stale
environment configuration, or prerequisite detector disagreement on Windows 10.

## Decision

Every Windows application is published for `win-x64` with `.NET` self-contained, an explicitly
reviewed .NET 8 runtime patch, and Windows App SDK self-contained deployment. The release workflow
currently pins .NET `8.0.29` instead of accepting an older runtime pack selected by an unrelated
installed SDK. The installed application tree carries the managed runtime, host, and Windows App
SDK files required by each executable. Setup performs no .NET Desktop Runtime or Windows App
Runtime discovery and does not install either machine-level runtime.

Microsoft Visual C++ Redistributable x64 remains the only machine-level prerequisite. The release
packaging workflow downloads its installer from the official Microsoft URL into isolated staging,
verifies a valid Authenticode signature issued to Microsoft Corporation, and embeds it in Fowan
Setup. Before copying Fowan application files, Setup detects whether VC++ x64 is already installed
and otherwise runs the bundled installer with `/install /quiet /norestart`.

Setup accepts VC++ exit code `0` as success and `3010` as success requiring a Windows restart. It
does not launch Fowan when a restart is required. The VC++ installer is a temporary setup payload;
it is not copied into the Fowan application directory or retained as a separate public release
asset.

## Consequences

Clean supported Windows machines can install and launch Fowan without a machine-level .NET Desktop
Runtime or Windows App Runtime and without target-machine network access. This also removes the
installer checks that previously repeated a .NET requirement on affected Windows 10 machines.

The installer and installed application tree become larger because runtimes are repeated across
separate executable directories. Runtime security fixes require a new Fowan release instead of
being serviced only through a machine-level runtime update. Release packaging still requires
network access to the Microsoft VC++ download endpoint and successful Authenticode validation.
