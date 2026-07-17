#!/usr/bin/env python3
"""Generate repository-local Codex skills for Fowan and FowanCore."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path


SKILL_NAME_PATTERN = re.compile(r"[a-z0-9]+(?:-[a-z0-9]+)*")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Generate Fowan workspace .agents/skills projections from a prepared spec."
    )
    parser.add_argument("--spec", required=True, help="Path to the source Markdown spec file")
    parser.add_argument(
        "--repository-root",
        required=True,
        action="append",
        help="Fowan or FowanCore repository root; repeat to target both",
    )
    parser.add_argument(
        "--overwrite",
        action="store_true",
        help="Overwrite an existing SKILL.md",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print the planned output path without writing files",
    )
    return parser.parse_args()


def read_text(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except FileNotFoundError as exc:
        raise SystemExit(f"Spec file not found: {path}") from exc


def parse_frontmatter(raw: str) -> tuple[str, str]:
    match = re.match(r"\A---\r?\n(.*?)\r?\n---\r?\n?", raw, re.DOTALL)
    if not match:
        raise SystemExit("Spec must start with a YAML frontmatter block delimited by ---")
    return match.group(1), raw[match.end() :]


def parse_name_and_description(frontmatter: str) -> tuple[str, str]:
    fields: dict[str, str] = {}
    for line in frontmatter.splitlines():
        if ":" not in line:
            continue
        key, value = line.split(":", 1)
        fields[key.strip()] = value.strip().strip("'\"")

    name = fields.get("name", "")
    description = fields.get("description", "")
    if not name:
        raise SystemExit("Frontmatter must include a non-empty 'name'")
    if not description:
        raise SystemExit("Frontmatter must include a non-empty 'description'")
    if not SKILL_NAME_PATTERN.fullmatch(name):
        raise SystemExit("Skill name must use ASCII lowercase kebab-case")
    if "<" in description or ">" in description:
        raise SystemExit("Frontmatter description cannot contain angle brackets")
    return name, re.sub(r"\s+", " ", description).strip()


def quote_yaml_scalar(value: str) -> str:
    return "'" + value.replace("'", "''") + "'"


def build_skill_text(name: str, description: str, body: str) -> str:
    if not body.strip():
        raise SystemExit("Skill spec requires a Markdown body after the frontmatter")
    return (
        f"---\nname: {name}\ndescription: {quote_yaml_scalar(description)}\n---\n\n"
        f"{body.lstrip().rstrip()}\n"
    )


def identify_repository(root: Path) -> str:
    if not (root / "AGENTS.md").is_file():
        raise SystemExit(f"Repository root is missing AGENTS.md: {root}")
    if (root / "Fowan.sln").is_file():
        return "Fowan"
    if (root / "Cargo.toml").is_file() and (root / "crates" / "fowan-engine").is_dir():
        return "FowanCore"
    raise SystemExit(f"Repository root is not recognized as Fowan or FowanCore: {root}")


def main() -> int:
    args = parse_args()
    spec_path = Path(args.spec).resolve()
    repository_roots = list(dict.fromkeys(Path(value).resolve() for value in args.repository_root))
    repositories = [(root, identify_repository(root)) for root in repository_roots]

    frontmatter, body = parse_frontmatter(read_text(spec_path))
    name, description = parse_name_and_description(frontmatter)
    outputs = [
        (root, repository_name, root / ".agents" / "skills" / name / "SKILL.md")
        for root, repository_name in repositories
    ]
    existing = [path for _, _, path in outputs if path.exists()]
    if existing and not args.overwrite:
        formatted = "\n".join(f"- {path}" for path in existing)
        raise SystemExit(f"Output exists, rerun with --overwrite:\n{formatted}")

    content = build_skill_text(name, description, body)
    print(f"spec: {spec_path}")
    print(f"name: {name}")
    for root, repository_name, output_path in outputs:
        existed = output_path.exists()
        if not args.dry_run:
            output_path.parent.mkdir(parents=True, exist_ok=True)
            with output_path.open("w", encoding="utf-8", newline="\n") as handle:
                handle.write(content)
        status = "overwritten" if existed else "created"
        print(f"skill: {output_path} ({status}; repository={repository_name})")
        print(f"repository_root: {root}")
    if args.dry_run:
        print("mode: dry-run")
    return 0


if __name__ == "__main__":
    sys.exit(main())
