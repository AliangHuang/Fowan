# ADR-0007: Share Windows runtimes across the Fowan tool suite

## Status

Accepted

## Context

Fowan 0.2.0 publishes Toolbox, Todo, Sticky Todo, Diary, Report, AI Chat and AI Configuration as
separate self-contained executables. Each application copies its own .NET runtime and Windows App
SDK native payload into the release tree. The resulting application staging directory is about
1.37 GB even though the suite is installed and updated as one product.

## Decision

Starting with 0.2.1, all Fowan Windows applications are published as framework-dependent
win-x64 applications. The installer and portable archive carry exactly one signed copy of each
shared prerequisite:

- .NET 8 Desktop Runtime x64;
- Windows App Runtime 2.2 x64; and
- Microsoft Visual C++ Redistributable x64.

The administrator-level Inno Setup installer installs a missing .NET 8 Desktop Runtime, always
chains the Windows App Runtime installer as recommended for unpackaged Windows App SDK apps, and
then installs a missing VC++ runtime. Existing WindowsPackageType=None bootstrap configuration
loads the installed Windows App Runtime for each WinUI application.

The portable archive includes the same signed prerequisite installers plus an administrator
PowerShell helper. Its README requires running the helper before the first application launch.
All downloaded prerequisite binaries are Authenticode-checked for a Microsoft Corporation signer
before packaging, and the portable helper checks them again before execution.

## Consequences

The release no longer duplicates runtime payloads for every tool, reducing download and installed
size while retaining an offline installer. A fresh machine now receives machine-level runtime
components during Fowan installation; installation must therefore remain elevated and failure to
install either required runtime aborts setup rather than leaving a non-startable application.

Fowan continues to target .NET 8 and Windows App Runtime 2.2. Runtime servicing can be delivered
by Microsoft independently of a Fowan release. The prior 0.2.0 self-contained release remains
compatible and is not modified or removed by this decision.
