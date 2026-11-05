# Contributing to Fowan

Policy version: 2026-07-14

This document is the normative development policy for the public Fowan repository. The
[Chinese execution guide](docs/development_guide.zh-CN.md) explains the same workflow for
the current team. If the two documents disagree, this file is authoritative.

## Before implementation

1. Read `AGENTS.md`, `docs/repository_boundaries.md`, and the architecture document for
   the component being changed.
2. Reuse an existing component when the capability has the same state owner and lifecycle.
3. Before creating an executable, project, top-level feature module, shared service, or
   platform adapter, add an accepted design proposal under `docs/design-proposals/` and
   register it in `docs/component-manifest.json`.
4. Add or update an ADR when a change affects a public protocol, cross-repository contract,
   data format or migration, security or privacy boundary, process boundary, or important
   dependency that is difficult to reverse.

Implementation must not begin while the required proposal is still `draft`.

## Architecture rules

- Each running tool has exactly one application state owner: a Workspace or Session.
- Windows compose dependencies, own native lifetime and root layout, and forward events.
  They do not own business data, perform persistence, or call raw RPC.
- Presentation consumes immutable snapshots and presentation-only state. Mutations are
  requested through typed command ports.
- Application code does not depend on Presentation or `Platform/Windows`.
- Platform adapters implement ports declared by shared contracts or the owning
  application's `Application/Ports` folder.
- Shared business behavior belongs in `contracts` or `shared`; UI types are never shared
  between tools.
- Service locators, mutable global state, generic event buses, and cross-layer static access
  are prohibited.
- A failed use case leaves prior state intact or returns an explicit failure result. A
  successful use case persists before publishing a typed change event.

Architecture is evaluated by ownership and dependency direction, not source line counts.

## Compatibility and quality

- Preserve executable names, UI behavior, accessibility, themes, `%LOCALAPPDATA%` paths,
  Todo and Diary JSON formats, and AI protocol v0.1 unless an accepted migration or ADR says
  otherwise.
- New packages require a proposal that records purpose, alternatives, licensing, security
  impact, and removal strategy.
- Never log credentials, prompts, user content, recovery material, or production endpoints.
- Implement the Application/Domain behavior and failure tests before wiring Presentation and
  Platform code.

## Pull requests and definition of done

Every pull request must identify its proposal or state `baseline change`, name the state
owner, describe dependency changes and failure behavior, and include verification evidence.
`baseline` is not a free-form exemption: it is valid only for exact entries in
`docs/architecture-baseline.json`. Changing that frozen ledger requires an accepted proposal
and an ADR.
Work is complete only when:

- success, failure, cancellation, and resource cleanup are tested where applicable;
- no duplicate business state or direct persistence/RPC/platform bypass was introduced;
- component manifest, proposal/ADR, documentation, and code agree;
- Debug and Release builds, all tests, semantic architecture checks, and `git diff --check`
  pass with zero warnings and errors; and
- affected Windows workflows receive proportionate manual UI and accessibility verification.

Run the repository gate before requesting review:

```powershell
./scripts/verify.ps1
```

Repository administrators must configure the default branch to require the GitHub Actions
check `CI / Architecture and governance`; reviews may not bypass a failing or missing check.
