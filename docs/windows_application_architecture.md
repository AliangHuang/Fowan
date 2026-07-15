# Windows application architecture

## Status

This document defines the required architecture for the Windows applications in
the public Fowan repository. It supplements `repository_boundaries.md`; product
requirements and local data formats remain unchanged.

## Dependency direction

```text
App / Window composition root
    -> feature Views
    -> Presenters
    -> application Workspace and use cases
    -> domain services and ports
    -> persistence, protocol, and Platform/Windows adapters
```

Dependencies may only point down this list. Platform adapters implement ports
defined by shared contracts or the owning application's `Application/Ports`
folder. Shared Todo and Diary code must not depend on WinUI, WPF, or native
Windows APIs.

## Ownership rules

- A Window owns dependency composition, native lifetime, the root layout, and
  event forwarding. It does not own business data or perform persistence.
- Each running tool has one application state owner: `ToolboxSession`,
  `TodoWorkspace`, `DiaryWorkspace`, `AiChatSession`, or `AiConfigSession`.
- Presenters own view-only state such as selection, navigation, filters, and
  dialog visibility. Views receive snapshots and callbacks; they do not mutate
  persisted models directly.
- A successful use case persists its change before publishing a typed change
  set. A failed use case either leaves the prior state intact or reports an
  explicit failure result.
- Todo main and Sticky share task and list use cases, but do not share UI types.
  WinUI and WPF geometry/drag adapters remain separate.
- Cross-component communication uses direct calls, narrow ports, and typed
  local events. There is no global event bus, service locator, or DI container.

## Feature boundaries

- Toolbox: catalog/navigation, launch, update, tray/window lifetime, profile,
  and settings.
- Todo: workspace persistence, task/list commands, main-window presentation,
  Sticky presentation, task drag geometry, window mode, and dialogs.
- Diary: workspace persistence, timeline, editor, metadata, notebooks/tags,
  attachments, environment acquisition, and dialogs.
- AI: chat session, configuration session, consent, typed Core API, connection,
  JSON-RPC peer, framing, and Windows process adapters.

## Semantic architecture checks

Architecture is evaluated by state ownership, dependency direction, immutable
presentation inputs, typed commands, and explicit failure behavior. Source line,
method-count, and file-size budgets are intentionally not used. Roslyn architecture
tests aggregate partial declarations and reject hidden state ownership or layer
bypasses regardless of source layout.

## Compatibility and verification

Refactors must preserve executable names, UI behavior, `%LOCALAPPDATA%` paths,
Todo and Diary JSON formats, AI protocol v0.1, accessibility behavior, and
light/dark theme output. `scripts/verify.ps1` enforces development policy and
semantic dependency boundaries in addition to Debug/Release builds and tests.
