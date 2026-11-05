---
name: repair-and-verify
description: Diagnose, minimally fix, and verify Fowan Windows defects. Use for AI Chat conversation selection or streamed-output failures, incorrect AI configuration messages, failed tests or builds, stale runnable output, locked application files, or requests to prove a fix is present in the unified build runtime.
---

# Fowan 修复与验证

Use the smallest safe fix and preserve unrelated working-tree changes.

1. Read repository instructions and inspect `git status --short`. Treat pre-existing edits and untracked files as user-owned.
2. Trace the real event path before editing: UI handler, application/session state, Core protocol callback, and affected localized text. For streaming failures, do not await Core RPC calls from the Core reader's notification callback.
3. Add a focused regression test for application or protocol-state behavior when it is testable without a live WinUI window. Run the narrowest relevant test project, then build the changed Windows project with `--no-restore` when dependencies are restored.
4. Run `git diff --check` for changed files. Do not run `scripts/package-windows.ps1` for routine repair or validation.

## Unified runtime output

`dotnet build` writes intermediate output under `build/msbuild/`; it does not update the runnable application tree. Invoke `$build-fowan-windows` to rebuild and verify the complete Windows application tree. Follow that skill's Core-artifact, configuration, locked-output, and stop-authorization rules; do not call `build-windows.ps1` directly from this repair workflow.

The only development runtime is:

```text
build\windows\win-x64\app\
```

Check the concrete executable or resource there before claiming the runnable output is updated. The build may automatically stop and restart only `.Dev` processes whose exact executable paths are inside this development tree. Do not stop installed production processes or unrelated executables; report any remaining lock that is not owned by the development runtime.

## Handoff

State the root cause, exact behavior change, validation commands and results, the unified runtime path, and any remaining locked process or unrelated dirty change.
