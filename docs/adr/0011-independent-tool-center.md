# ADR-0011: Independent Tool Center process

- Status: Accepted

## Context

The user confirmed a single independent 工具中心 application, then narrowed its catalog
to available tools only. Toolbox remains the launcher for its existing available tools.

## Decision

Add `Fowan.ToolCenter.Windows` under `apps/windows/tool-center/app`, distributed at
`Tools/ToolCenter`, and launch it through the existing Toolbox coordinator. Register a
single-instance key distinct between development and production. The center owns an immutable
catalog through `ToolCenterSession`; navigation and selection are presentation state.

The six IDs `notes`, `knowledge`, `files`, `global-search`, `workflows`, and `plugins`
are absent from both catalogs. Tool Center rejects unavailable entries. Its first available
tool and managed installation are introduced by [ADR-0012](0012-managed-tool-installations.md).
No planned item gains an executable, installation flow, Core capability,
or data store. The planning history remains in the original UI design document.

## Consequences

The center has its own window/lifetime and inherits theme/language preferences read-only at
startup. Toolbox retains existing launch error and minimize behavior. Build/distribution and
stop scripts must include the additional executable; existing data and executable locations
remain compatible. This boundary can evolve independently when a planned application receives
its own accepted design, without making Toolbox the owner of that application's state.
