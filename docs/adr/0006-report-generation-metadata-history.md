# ADR-0006: Persist report generation records and completed text results

## Status

Accepted.

## Context

The report tool originally prohibited client-side report history. Users need a local overview of
generation attempts, including safe failure and output-file information. They also need to reopen
a completed text result from the corresponding local record without retaining Todo snapshots or
request inputs as AI history.

## Decision

Keep the AI protocol at `0.1` contract revision `1`; the separate content-tree and multi-round
generation contract is defined by ADR-0005. The Windows report client persists a record after a
valid, range-scoped generation starts. A record may contain its
ID, timestamps, terminal status, report range, style, template mode, completed and unfinished
counts, file-output state/path and a normalized safe error code. A completed text-mode record may
also contain its constrained rich-block document for direct local viewing; file-mode records store
only the successfully committed output path. Records live under `%LOCALAPPDATA%\Fowan\Report\Records`,
use atomic replacement, support filtered display and explicit deletion, and do not delete referenced
output files.

This decision does not prohibit the separately approved, user-confirmed template preference
feature. Text templates and examples may be saved only when the user explicitly requests it,
as versioned JSON block documents under `%LOCALAPPDATA%\Fowan\Report\Preferences`; report output
and per-generation custom requirements are never written there. Legacy `text-template.rtf` and
`text-example.rtf` files are retained but never loaded, converted, overwritten, or deleted.

## Privacy and failure behavior

Records must never contain Todo titles, notes, snapshots, report request content, model raw
responses, templates, examples, custom requirements, credentials, endpoints or prompts. The
constrained rich-block document of a completed text result is the sole content exception and is
deleted with its record. A record left in a generating state is marked `interrupted` on the next
startup. File paths are written only after the copied Office output has been successfully committed
and re-opened.

## Consequences

This supersedes the client-history prohibition in ADR-0005 for listed local metadata and completed
text results. Core still creates no report history; neither candidate content trees nor client
structural diagnostics are durable records.
