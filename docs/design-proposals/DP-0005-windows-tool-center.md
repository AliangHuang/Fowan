---
id: DP-0005
status: accepted
title: Independent Windows Tool Center for available tools
components:
  - tool-center-windows
  - windows-toolbox
adrs:
  - docs/adr/0011-independent-tool-center.md
---

# Problem and users

The user requested an independent 工具中心 (Tool Center). The subsequent confirmed scope
removes notes, knowledge, files, global-search, workflows, and plugins from its catalog.
Tool Center admits only available tools; planning placeholders belong outside the product catalog.

## Goals and non-goals

Provide an available Toolbox launch card and a separate native Windows application with
search, category navigation, and tool details. The first available tool and its manual
installation lifecycle are defined by [DP-0006](DP-0006-managed-yym-tool.md). Existing available Toolbox tools and
disabled Quick Capture retain their behavior. Implementing planned applications or moving
existing available Toolbox tools into the center is out of scope.

## Repository and component boundaries

`apps/windows/tool-center/app` owns the new executable. `ToolCenterSession` owns the
immutable application catalog and startup preferences. Presentation owns search, category,
and selection state. A Windows composition adapter reads Toolbox theme/language preferences
without writing them. Toolbox owns only the launch card and executable resolution.

## Interfaces and data flow

The composition root reads local preferences and localized resources, then creates one
session. The session exposes immutable application snapshots and a pure search query.
The session filters out unavailable entries before exposing snapshots or search results.
When no tools are registered, the view renders a dedicated empty-catalog state, hides the
detail pane, and disables search. Registered available tools can expose management commands
under their own accepted design.
Repeated process activation returns to the same application window. No UI project references
another UI project; no shared mutable catalog or IPC contract is introduced.

## Failure, cancellation, and atomicity

Missing or unreadable preferences fall back to system settings without modifying user data.
Missing localized strings fall back to English. Empty searches have an explicit empty state
and a clear-search action. Missing/failed executable launches use existing Toolbox error
reporting and do not minimize the Toolbox. Window close exits the independent application.
There is no background work, persistence, partial installation, or cancellation transaction.

## Compatibility, migration, and rollback

The six planning placeholders are absent from both Toolbox and Tool Center. Available tool IDs,
executables, settings and Todo/Diary data remain compatible. The new production executable is
`Tools/ToolCenter/Fowan.ToolCenter.Windows.exe`; development uses `.Dev.exe`. Add the
application to the solution, unified build, release staging checks and development stop flow.
Do not change release versions or create installer artifacts during this work.
Register the center's release-note path; its versioned changelog is created only during a
later user-confirmed release preparation, as required by the repository's release policy.

## Security, privacy, dependencies, and permissions

Reuse the existing Microsoft.WindowsAppSDK dependency and GPL-3.0 client code. The initial
catalog-only shell requires no network, Core capability, elevation, credentials or new package.
The managed-download extension is governed by DP-0006. The shell reads only
theme/language fields from existing client settings. Alternatives were an embedded Toolbox
page or repurposing an existing application; neither provides the requested independent
application boundary. Removal can later remove this catalog-only process without a data
migration, through a separately authorized change.

## Test and acceptance plan

Verify the empty-catalog state, exclusion of unavailable entries, localized search/category
behavior, empty results and startup preference fallback. Verify Toolbox has one available center card
and no planned application cards. Run Debug/Release builds, all repository tests and semantic
architecture checks. Build the unified development runtime and manually exercise launch,
repeat activation, search, categories, detail selection, keyboard access and theme rendering.

## Reuse and duplication analysis

Reuse Toolbox process launch/error/debounce handling, existing Fluent resources and local
preference conventions. Remove the six placeholders and their center-owned descriptions; do not create
duplicate persistence, AI services, business workflows or application implementations.
