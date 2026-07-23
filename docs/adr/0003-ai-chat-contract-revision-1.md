# ADR-0003: Establish Windows AI v0.1 at contract revision 1

## Status

Accepted.

## Context

Production chat requires model context limits, confirmed compaction, true branches, paged history,
durable stream state and safe provider diagnostics. These change the public wire contract and the
private protected-history schema. The product has not been released, so these capabilities form the
first `0.1` contract baseline rather than a second contract revision.

## Decision

`protocol/ai/v0.1` defines `contractRevision=1`, explicit context and branching capabilities, typed
methods/notifications/errors and model limit fields. Fowan and FowanCore artifacts are built together
from this single baseline; no earlier contract revision is accepted or adapted.

Fowan owns the implementation-independent schemas, DTOs, UI consent, cancellation and safe status
display. FowanCore owns estimation policy, summary prompts, protected summaries, branch traversal,
the V1 schema, provider limits and error classification. Provider bodies, prompts and protected-history
implementation remain private.

## Compatibility and security

Credential identifiers, DPAPI protection, named-pipe identity, local data paths and the 8 MiB frame
limit remain unchanged. Summary calls use only an already-consented endpoint and commit atomically.
Development databases created before this V1 baseline must be reset; the Core rejects an unexpected
schema instead of attempting an unverified migration or down-migration.

## Verification

Generated schemas/examples, revision/capability handshake tests, dual-repository contract locks,
V1 schema tests, safe-error tests and complete repository gates are required.
