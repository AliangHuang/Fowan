# ADR-0005: Publish report generation in the initial AI contract

## Status

Accepted.

## Context

The report tool needs a model binding distinct from chat and a cancellable, structured report
generation lifecycle. The initial v0.1 contract must publish every first-release AI capability
and method together, including report generation.

## Decision

Keep `protocolVersion` at `0.1` and set `contractRevision` to `1`. Register `ai.report`,
`ai.report.v1`; add
`ai.report.generate`, `ai.report.cancel`, and started/completed/cancelled/
failed notifications. A report request contains a normalized task snapshot and constrained
text or Office content tree, never provider credentials, model selections, local paths,
OpenXML, binary payloads, executable content, macros, formulas or document operations. A result
is a complete same-format content tree, not Markdown, target IDs, scalar values, row values or
editing commands. Repair attempts may carry the prior candidate and a safe structural diagnostic;
the first release allows at most three total candidates. Core resolves the `ai.report` binding
and enforces generic JSON/size boundaries; the public client owns content-tree matching, Office
temporary writes, protected-object checks and atomic commit.

## Compatibility and security

Only the initial revision 1 contract is supported. The generic binding table stores the added feature
without a SQLite schema change. Core creates no protected report history; the client-local text
result exception is defined by ADR-0006. Endpoint consent covers the task details,
template/example context and custom requirements named by the UI.

## Verification

Update public schemas, deterministic fixtures, C# and Rust contract mirrors, cross-repository
locks, capability handshake tests, binding tests, cancellation tests, safe error tests, file
candidate strong-validation tests and the two repository gates.
