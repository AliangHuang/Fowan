# Fowan Workspace Skill Spec Format

Use this format for a temporary source spec passed to the workspace generator.

```md
---
name: review-fowan-runtime
description: Review and validate the Fowan Windows runtime output.
---

# Review Fowan Runtime

...
```

Required frontmatter fields are `name` and `description`. The name must be
lowercase kebab-case and match its output directory. Keep the description short
and explicit about the Fowan workflow that triggers the skill.

The generator writes one projection per explicitly supplied repository root:

```text
Fowan/.agents/skills/<name>/SKILL.md
FowanCore/.agents/skills/<name>/SKILL.md
```

Do not select both repositories unless the full workflow is valid for both and
contains no FowanCore-private implementation detail that would be exposed in
the public Fowan repository. The generator does not create `.claude` files.
Add `agents/openai.yaml`, scripts, or references only when the resulting
workflow needs them.
