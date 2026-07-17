#!/usr/bin/env python3
"""Validate Codex skills owned by Fowan workspace repositories."""

from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
from pathlib import Path


SKILL_NAME_PATTERN = re.compile(r"[a-z0-9]+(?:-[a-z0-9]+)*")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Validate Fowan workspace .agents/skills.")
    parser.add_argument(
        "--repository-root",
        required=True,
        action="append",
        help="Fowan or FowanCore repository root; repeat to validate both",
    )
    parser.add_argument(
        "--skip-quick-validate",
        action="store_true",
        help="Skip the system quick_validate.py check only for local triage",
    )
    return parser.parse_args()


def identify_repository(root: Path, errors: list[str]) -> str | None:
    if not (root / "AGENTS.md").is_file():
        errors.append(f"repository root is missing AGENTS.md: {root}")
        return None
    if (root / "Fowan.sln").is_file():
        return "Fowan"
    if (root / "Cargo.toml").is_file() and (root / "crates" / "fowan-engine").is_dir():
        return "FowanCore"
    errors.append(f"repository root is not recognized as Fowan or FowanCore: {root}")
    return None


def parse_frontmatter(path: Path, errors: list[str]) -> dict[str, str]:
    try:
        text = path.read_text(encoding="utf-8")
    except OSError as exc:
        errors.append(f"cannot read {path}: {exc}")
        return {}
    match = re.match(r"\A---\r?\n(.*?)\r?\n---", text, re.DOTALL)
    if not match:
        errors.append(f"missing YAML frontmatter: {path}")
        return {}
    fields: dict[str, str] = {}
    for line in match.group(1).splitlines():
        if ":" in line:
            key, value = line.split(":", 1)
            fields[key.strip()] = value.strip().strip("'\"")
    return fields


def find_quick_validate() -> Path | None:
    codex_home = os.environ.get("CODEX_HOME")
    candidates = []
    if codex_home:
        candidates.append(Path(codex_home) / "skills" / ".system" / "skill-creator" / "scripts" / "quick_validate.py")
    candidates.append(Path.home() / ".codex" / "skills" / ".system" / "skill-creator" / "scripts" / "quick_validate.py")
    return next((path for path in candidates if path.is_file()), None)


def validate_skill(skill_dir: Path, validator: Path | None, args: argparse.Namespace, errors: list[str]) -> None:
    if not SKILL_NAME_PATTERN.fullmatch(skill_dir.name):
        errors.append(f"invalid skill directory name: {skill_dir.name}")
    for forbidden in ("README.md", "__pycache__"):
        if (skill_dir / forbidden).exists():
            errors.append(f"forbidden skill artifact: {skill_dir / forbidden}")
    skill_file = skill_dir / "SKILL.md"
    if not skill_file.is_file():
        errors.append(f"missing SKILL.md: {skill_dir}")
        return
    fields = parse_frontmatter(skill_file, errors)
    if fields.get("name") != skill_dir.name:
        errors.append(f"frontmatter name must match directory: {skill_dir.name}")
    if not fields.get("description"):
        errors.append(f"missing frontmatter description: {skill_dir.name}")
    if validator is None:
        if not args.skip_quick_validate:
            errors.append("system quick_validate.py is unavailable")
        return
    environment = os.environ.copy()
    environment["PYTHONUTF8"] = "1"
    result = subprocess.run(
        [sys.executable, str(validator), str(skill_dir)],
        capture_output=True,
        text=True,
        check=False,
        env=environment,
    )
    if result.returncode:
        detail = "\n".join(part.strip() for part in (result.stdout, result.stderr) if part.strip())
        errors.append(f"quick_validate.py failed for {skill_dir.name}: {detail}")


def main() -> int:
    args = parse_args()
    errors: list[str] = []
    repository_roots = list(dict.fromkeys(Path(value).resolve() for value in args.repository_root))
    validator = find_quick_validate()
    validated: list[str] = []
    for repository_root in repository_roots:
        repository_name = identify_repository(repository_root, errors)
        if repository_name is None:
            continue
        skills_root = repository_root / ".agents" / "skills"
        if not skills_root.is_dir():
            errors.append(f"missing {repository_name} skills root: {skills_root}")
            continue
        for skill_dir in sorted(path for path in skills_root.iterdir() if path.is_dir()):
            validate_skill(skill_dir, validator, args, errors)
        validated.append(repository_name)

    if errors:
        print("ERROR: Fowan workspace skill validation failed")
        for error in errors:
            print(f"- {error}")
        return 1
    print(f"OK: Codex skill validation passed for {', '.join(validated)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
