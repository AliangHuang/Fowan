---
id: DP-0001
status: accepted
title: Semantic architecture governance and state ownership
components:
  - architecture-tests
  - windows-toolbox
  - todo-shared
  - todo-windows
  - todo-sticky-windows
  - diary-shared
  - diary-windows
  - ai-shared
  - ai-chat-windows
  - ai-config-windows
adrs: []
---

# Problem and users

Previous God-class refactoring reduced file size but left business state and orchestration in
Windows and callback networks. Maintainers need enforceable ownership and dependency rules for
existing applications and future tools.

## Goals and non-goals

Establish one state owner per tool, immutable presentation snapshots, typed commands, design
proposal governance, and semantic CI checks. This proposal does not change product behavior,
data formats, protocol v0.1, packaging, or impose source-size limits.

## Repository and component boundaries

Fowan retains UI, platform integration, ordinary local tool behavior, Todo/Diary storage, and
the public AI contract. Each Workspace/Session owns business state. Window and Presentation
remain outside persistence and raw RPC. FowanCore retains sensitive AI execution and storage.

## Interfaces and data flow

Workspaces/Sessions publish immutable snapshots and typed change events. Presentation sends
typed commands to application coordinators. Platform adapters implement ports declared in
contracts or Application/Ports. A component manifest binds source roots and modules to this
accepted proposal.

## Failure, cancellation, and atomicity

Successful commands persist before publishing changes. Failures keep the previous state or
return explicit failure results. Existing cancellation and cleanup behavior remains mandatory.

## Compatibility, migration, and rollback

Executable names, UI behavior, local paths, Todo/Diary JSON, and AI protocol remain unchanged.
The refactor is source-internal and can be reviewed component by component.

## Security, privacy, dependencies, and permissions

No new runtime permission or network exit is introduced. Roslyn, MSBuildWorkspace, and the
MSBuild locator are added only to the test project for symbol- and operation-based architecture
verification; they are not shipped with applications. The dependency manifest records their
licenses, alternatives, security impact, and removal strategy.

## Test and acceptance plan

Add governance validation, real-solution Roslyn positive and negative fixtures, state-owner tests, full
Debug/Release builds, existing unit suites, and manual UI regression of affected workflows.

## Reuse and duplication analysis

Todo and Sticky share TodoWorkspace; AI clients share typed sessions; platform access continues
through existing contracts. No new global event bus, service locator, or duplicated store is
introduced.
