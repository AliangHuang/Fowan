---
id: DP-0003
status: accepted
title: Windows report tool
components:
  - todo-contracts
  - todo-shared
  - report-shared
  - report-windows
  - report-tests
  - ai-shared
  - ai-config-windows
  - ai-protocol-v0.1
adrs:
  - docs/adr/0005-ai-report-contract-revision-1.md
  - docs/adr/0006-report-generation-metadata-history.md
---

# Problem and users

Fowan users need a foreground-only Windows tool that turns a filtered, read-only Todo
snapshot into a weekly, monthly, or custom-period report. The existing chat binding must
not select a report model; Core never retains report history, while the Windows client keeps a
separate local generation record and may retain a completed text result for direct viewing.

## Goals and non-goals

Add an independently launched report tool, the `ai.report` binding, text editing/copying,
safe `.docx`/`.xlsx` template copies, and local generation records with viewable completed text
results. Scheduling, sending, collaboration, shared or cloud report history, macros, formulas,
legacy Office formats, and in-tool model selection remain out of scope.

## Repository and component boundaries

The Application-layer `ReportWorkspaceOwner` creates and exposes the sole `ReportWorkspace`.
That workspace receives immutable Todo snapshots through a narrow port and never mutates Todo
storage. The report Window composes WinUI controls and
forwards commands only. `Fowan.Ai.Shared` exposes typed public DTOs and Core transport calls;
it contains neither prompt policy nor provider logic. Open XML manipulation is a report Windows
platform adapter; Core owns report prompt construction, model selection, consent enforcement,
provider access, response validation, and cancellation.

## Interfaces and data flow

The report range becomes a `TodoDateRangeFilter` and is passed to `TodoQuery`; shared date
presets supply this week, last complete week, and current month. At Generate, the workspace
reloads, filters and freezes completed and incomplete task records. The client sends the
normalized snapshot, style, custom requirement, and complete normalized text or Office content
tree through `ai.report.generate`; it sends no credential, model id, local path, OpenXML,
binary payload or position ID. Core replies only through a typed terminal report notification.
The client exposes text results in an editable Notion-style block editor, or writes a file
candidate to a fresh temporary template copy and atomically commits it only after local strong
validation. For file mode, Word body paragraphs/tables and Excel sheets/cells are writable
content while Office styling, formulas and layout remain a client-owned read-only skeleton.
The client permits at most an initial candidate plus two repair candidates, each based on an
opaque safe structural diagnostic; Core does not locate or write Office content.

## Failure, cancellation, and atomicity

No Todo mutation occurs. Empty results, absent `ai.report` binding, denied consent, protocol
failure, cancellation and invalid report instructions leave previous state and source files
unchanged. Every file candidate starts from a fresh source-template copy. A document output
exists only after local shape/type/length/source-hash/OpenXML checks pass and the copied package
reopens successfully; all temporary files are removed otherwise.

## Compatibility, migration, and rollback

The public protocol remains `0.1` and is published at contract revision 1 as one atomic
public/Core baseline. The existing generic binding table needs no schema migration; it starts
accepting the registered `ai.report` feature. Todo JSON, Todo paths and the chat binding retain
their format and meaning. The first release supports only this revision 1 baseline.

## Security, privacy, dependencies, and permissions

The consent dialog names task details, templates, examples and custom requirements before the
first send to each endpoint. Report snapshots, custom requirements and file contents remain in
memory and are not logged or persisted. The client may persist record ID, timestamps, terminal
status, range, style, template mode, task counts, output-file state/path, a safe error code, and
the constrained rich-block result of a completed text report for direct local viewing; it never
persists report input. A user may explicitly save only a controlled local template/example
preference copy as versioned JSON block documents. Legacy RTF
preference files are retained but never read, converted, overwritten, or deleted. `DocumentFormat.OpenXml` 3.5.1 (MIT) is used
only in the Windows report adapter for strongly typed Open XML package handling; it replaces no
existing dependency and can be removed with the report file-mode capability.

## Test and acceptance plan

Require Todo filter parity tests, ReportWorkspace snapshot/cancel/failure tests, contract/schema
fixtures, Core report lifecycle tests, first/second/third candidate flows, source-template hash
checks, reopened Word/Excel output, protected-formula/object checks, no-partial-output tests,
visual fixture review, accessibility checks and both repository gates.

## Reuse and duplication analysis

Reuse `TodoQuery`, `TodoStore` read behavior, the AI Core transport, consent storage, connection
invocation coordinator, Toolbox launch mechanism and existing Fluent UI resources. Only pure Todo
filter criteria/date preset helpers are extracted for semantic reuse; Todo UI controls, report UI,
Office rendering and report lifecycle are not copied from another tool.
