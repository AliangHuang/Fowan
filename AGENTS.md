# Fowan repository instructions

- Keep the repository buildable with zero warnings and zero errors.
- Never revert or delete code without the user's explicit confirmation for that specific action. This includes restoring older revisions, discarding changes, removing files or implementations, and using Git rollback commands; a general request to fix, simplify, clean up, or make the project buildable is not authorization to revert or delete code.
- Do not solve problems by degrading visual quality, product performance, functionality, user experience, correctness, maintainability, test coverage, or overall code quality. Fix the root cause while preserving existing quality bars; if a genuine tradeoff cannot be avoided, stop and ask the user to choose before implementing it.
- Treat installer packaging as a release-only operation. Routine development, build, test, and validation work must not run `scripts/package-windows.ps1`, build the setup executable, or generate the complete release package.
- Enter the complete packaging workflow only when the user explicitly asks to prepare or publish a release version. At that point, inspect every bundled tool for changes, update the affected tool and toolbox changelogs, update all confirmed version references, build the release package, and run the full release checks.
- Never choose, infer, increment, or write a release version automatically. Before changing changelogs or version fields and before packaging, ask the user to confirm the exact version, then use only that confirmed version throughout the release.
- Treat `Fowan.Todo.Shared` and `Fowan.Diary.Shared` as open-source tool code, not as the private Fowan core.
- Keep UI, platform integration, ordinary tool behavior, local JSON formats, attachments, location, and weather code in this repository.
- Do not implement AI orchestration, model strategy, user-information encryption, key management, sensitive indexing policy, licensing decisions, or commercial policy here. Those belong in the private `FowanCore` repository.
- Do not add a protocol, SDK, sidecar, or placeholder RPC until a concrete private-core capability requires it.
- Preserve `%LOCALAPPDATA%\Fowan\Todo` and `%LOCALAPPDATA%\Fowan\Diary` data compatibility unless a migration is explicitly designed and tested.
- Read `docs/repository_boundaries.md` before adding cross-cutting, AI-related, or security-sensitive functionality.
