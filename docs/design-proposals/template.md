---
id: DP-0000
status: draft
title: Replace with a concise title
components:
  - component-id
adrs: []
---

# Problem and users

Describe the user problem, scenarios, and evidence.

## Goals and non-goals

State the intended outcome and explicit exclusions.

## Repository and component boundaries

Name the owning repository, component, state owner, layers, dependencies, and responsibilities
that must remain outside this component.

## Interfaces and data flow

Describe inputs, outputs, immutable snapshots, typed commands/events, ports, and end-to-end
data flow.

## Failure, cancellation, and atomicity

Define failure results, cancellation behavior, retries, cleanup, transaction/compensation, and
what state remains visible after failure.

## Compatibility, migration, and rollback

Cover local data, public APIs, protocol, deployment, migration, and rollback.

## Security, privacy, dependencies, and permissions

Document sensitive data, network exits, platform permissions, new dependencies, licenses,
alternatives, and removal strategy.

## Test and acceptance plan

List unit, contract, integration, failure-path, manual UI, accessibility, and release evidence.

## Reuse and duplication analysis

Identify reused components and explain how duplicate state or business logic is avoided.
