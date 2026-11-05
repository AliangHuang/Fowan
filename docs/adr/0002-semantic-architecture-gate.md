# ADR-0002: Compiler-backed architecture governance

## Status

Accepted

## Context

Text and regular-expression checks cannot resolve aliases, partial declarations, project references,
or the actual receiver of a state mutation. They allowed architecture declarations to drift away
from compiled behavior.

## Decision

Fowan uses Roslyn `MSBuildWorkspace`, `SemanticModel`, and `IOperation` in test-only architecture
checks. The Roslyn and MSBuild locator packages are development dependencies only. Component,
proposal, dependency, and ADR metadata is validated against a frozen baseline ledger.

## Consequences

CI requires an installed .NET SDK/MSBuild toolset and takes longer than text scanning. In return,
aliases, fully-qualified names, partial declarations, and symbol-level mutation paths are checked
against the real solution. The gate is removable by replacing it with an equivalent compiler-backed
implementation under a new accepted proposal and ADR.
