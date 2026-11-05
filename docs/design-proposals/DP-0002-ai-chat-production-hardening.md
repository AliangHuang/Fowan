---
id: DP-0002
status: accepted
title: AI chat production hardening
components:
  - ai-shared
  - ai-chat-windows
  - ai-config-windows
  - ai-protocol-v0.1
adrs:
  - docs/adr/0003-ai-chat-contract-revision-1.md
---

# Problem and users

Long conversations currently send every stored message, have no model budget metadata or paging,
and can exceed the provider context or the 8 MiB local frame. Assistant variants are display-only,
stream state can be orphaned by navigation, and the handwritten Markdown renderer exposes incomplete
or misleading interactions. Windows AI chat users need predictable long-running conversations without
moving private orchestration into the public client.

## Goals and non-goals

Add explicit model limits, context estimates and confirmed compaction, real conversation branches,
paged history, one typed generation lifecycle, safe provider diagnostics, durable partial output, and
complete text Markdown presentation. Attachments, multimodal input, search, tools, skills, RAG, sync,
export, automatic routing, and silent compaction remain out of scope.

## Repository and component boundaries

`AiChatSession` remains the public client state owner. Windows composes views and forwards typed
commands; presentation consumes immutable snapshots. The public protocol and DTOs expose estimates,
status and encrypted-history metadata but contain no prompts, token policy, SQL, credentials, or
provider-specific rules. FowanCore continues to own context assembly, compaction, encryption,
persistence, provider access and safe error normalization.

## Interfaces and data flow

Protocol revision 1 defines model context/output limits, context estimate and compact operations, paged
branch messages, branch selection, compact lifecycle notifications and bounded safe request IDs. The
client estimates before sending, asks for explicit confirmation when compaction is required, waits for
compaction, and sends the unchanged draft only if the selected conversation, branch, model and draft
still match. History pages and branch selection flow through `AiChatSession`; Windows owns no duplicate
conversation state.

## Failure, cancellation, and atomicity

Compaction is independently cancellable and commits only a complete protected summary. Failure leaves
the previous summary and draft intact. Navigation during an invocation requires confirmation, cancels
and waits for the terminal state before switching. Provider, storage, protocol or rendering failure
keeps already displayed history and protected checkpoints. No raw provider body is shown or logged.

## Compatibility, migration, and rollback

The unreleased product baseline defines `protocol/ai/v0.1` with `contractRevision=1` and its required
capabilities. Public clients and Core are built together from this baseline; no previous protocol
revision is supported. Core creates the complete SQLite V1 schema directly while preserving Credential
Manager identifiers, DPAPI format and local data paths. Development databases created before this
baseline must be reset; no migration or down-migration is provided.

## Security, privacy, dependencies, and permissions

Compaction uses the already-consented endpoint and current credential/model, creates no new network
exit, and stores summaries only after Core protection. The public client never receives credentials or
provider prompts. Markdig 1.3.2 (BSD-2-Clause) is added to the Windows chat process for CommonMark AST
parsing with raw HTML, images and automatic external navigation disabled. It replaces an incomplete
handwritten parser and can be removed by substituting an equivalent maintained AST parser/renderer.

## Test and acceptance plan

Require protocol fixtures and cross-repository contract tests, session state and pagination tests,
Core migration/branch/budget/compaction/checkpoint/provider failure tests, Debug and Release gates,
visual fixture comparison, keyboard/accessibility checks and a rebuilt unified Debug runtime. Live
cloud messages are not sent automatically.

## Reuse and duplication analysis

Reuse `AiChatSession`, `AiCoreClient`, the existing protocol generator, Core `EngineContext`, focused
chat/conversation services, provider trait and connection invocation coordinator. Context state,
branch selection and persistence are not duplicated in Window, Presentation or a new global service.
