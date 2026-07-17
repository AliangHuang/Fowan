---
name: create-shared-skill
description: Create or update reusable Codex Skills for the Fowan workspace repositories. Use when Fowan, FowanCore, or a workflow genuinely shared by both repositories needs a new or revised repository-local Skill; preserve the public/private boundary and do not create multi-tool Claude projections.
---

# Create Fowan Workspace Skill

Create Codex skills in `Fowan/.agents/skills`, `FowanCore/.agents/skills`, or
both. Each repository owns its projection. The workspace copy of this Skill is
linked into both repositories for discovery. Do not create `.claude`
projections.

1. Classify the target explicitly:
   - `Fowan` for public client, Windows UI, packaging, and public protocol work.
   - `FowanCore` for private orchestration, provider, storage, credential, and
     security workflows.
   - Both only when the complete instructions are valid in both repositories
     and reveal no private implementation details to Fowan.
2. Read each target repository's `AGENTS.md`, `CONTRIBUTING.md`, and affected
   scripts or documentation. For FowanCore, also follow `SECURITY.md` and its
   AI/security boundary documents. Preserve existing skills unless the user
   explicitly requests an update.
3. Prepare a temporary Markdown spec with YAML frontmatter containing `name`
   and `description`, followed by the tool-neutral workflow body. Names use
   lowercase kebab-case and should start with an action such as `build`,
   `create`, `publish`, `repair`, `review`, or `run`.
4. Generate or update the selected projection. Repeat `--repository-root` to
   generate the identical shared workflow in both repositories:

   ```powershell
   python .\scripts\create_shared_skill.py `
     --spec <spec-path> `
     --repository-root ..\Fowan `
     [--repository-root ..\FowanCore] `
     [--overwrite]
   ```

   The generator accepts only recognized Fowan or FowanCore repository roots,
   preflights every output before writing, and never writes `.claude` files.
5. Add or update `agents/openai.yaml` only when UI metadata is useful. Its
   `default_prompt` must name the skill as `$<skill-name>`. Keep references and
   scripts only when they make the workflow more reliable.
6. Validate every affected repository:

   ```powershell
   python .\scripts\validate_workspace_skills.py `
     --repository-root ..\Fowan `
     --repository-root ..\FowanCore
   ```

7. Remove temporary specs. Confirm `create-shared-skill` resolves to this
   workspace source from both repositories' `.agents/skills` directories.
