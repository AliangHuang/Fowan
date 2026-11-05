# Design proposals

Design proposals are required before implementing a new executable, project, top-level
feature module, shared service, or independent platform adapter.

## Lifecycle

Use `DP-NNNN-short-title.md` and the template in this folder. Valid states are `draft`,
`accepted`, `implemented`, and `superseded`.

- `draft`: discussion only; implementation is not allowed.
- `accepted`: implementation and manifest registration are allowed.
- `implemented`: the accepted design is present and verified.
- `superseded`: a newer proposal or ADR replaces the decision.

Existing components are registered as `baseline` in the component manifest and are not
required to receive retrospective proposals.

An ADR is additionally required for protocol, cross-repository, schema/migration, security,
privacy, process-boundary, public compatibility, or difficult-to-reverse dependency changes.
