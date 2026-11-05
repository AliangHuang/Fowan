# ADR-0001: Windows AI v0.1 public contract boundary

## Status

Accepted.

## Context

The public Windows clients require a versioned contract for the private FowanCore process
without importing private orchestration, credential, encryption, or storage implementation.

## Decision

The implementation-independent JSON-RPC contract remains under `protocol/ai/v0.1`. Fowan owns
the DTOs, framing expectations, client consent UI, cancellation, and safe status display.
FowanCore owns provider access, credential handling, protected history, and private policy.
Every contract change requires a compatible protocol test and an ADR in both repositories.

## Compatibility and security

This ADR records the existing contract and introduces no wire, storage, permission, or data
format change. The public repository must never contain credentials, private provider policy,
or protected-history implementation.

## Verification

Schema examples, protocol contract tests, manifest governance, and both repositories' full CI
gates verify the boundary.
