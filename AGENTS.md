# Fowan repository instructions

- Keep the repository buildable with zero warnings and zero errors.
- Treat `Fowan.Todo.Shared` and `Fowan.Diary.Shared` as open-source tool code, not as the private Fowan core.
- Keep UI, platform integration, ordinary tool behavior, local JSON formats, attachments, location, and weather code in this repository.
- Do not implement AI orchestration, model strategy, user-information encryption, key management, sensitive indexing policy, licensing decisions, or commercial policy here. Those belong in the private `FowanCore` repository.
- Do not add a protocol, SDK, sidecar, or placeholder RPC until a concrete private-core capability requires it.
- Preserve `%LOCALAPPDATA%\Fowan\Todo` and `%LOCALAPPDATA%\Fowan\Diary` data compatibility unless a migration is explicitly designed and tested.
- Read `docs/repository_boundaries.md` before adding cross-cutting, AI-related, or security-sensitive functionality.
