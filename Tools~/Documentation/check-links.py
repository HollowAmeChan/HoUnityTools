"""Check repository-local Markdown links without fetching remote URLs.

Run from any directory. Covers tracked and non-ignored new Markdown files;
ignores code examples and fragment-only links. External workspace paths are
reported separately because they are not portable repository dependencies.
"""

from __future__ import annotations

import argparse
import re
import subprocess
from pathlib import Path
from urllib.parse import unquote


LINK = re.compile(r"\]\((?:<(?P<angle>[^>]+)>|(?P<plain>[^)\n]+))\)")
REFERENCE = re.compile(r"^\s*\[[^\]]+\]:\s*(?:<([^>]+)>|(\S+))", re.MULTILINE)
SCHEME = re.compile(r"^[a-zA-Z][a-zA-Z0-9+.-]*:")


def markdown_files(root: Path) -> list[Path]:
    result = subprocess.run(
        ["git", "-C", str(root), "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", "*.md"],
        check=True, stdout=subprocess.PIPE,
    )
    return sorted({root / name.decode("utf-8") for name in result.stdout.split(b"\0") if name})


def destinations(text: str):
    # Fenced code often contains example links or paths that are not documentation links.
    text = re.sub(r"(?ms)^\s*```[^\n]*\n.*?^\s*```[^\n]*$", "", text)
    text = re.sub(r"(?ms)^\s*~~~[^\n]*\n.*?^\s*~~~[^\n]*$", "", text)
    text = re.sub(r"`+[^`\n]+`+", "", text)
    for match in LINK.finditer(text):
        value = (match.group("angle") or match.group("plain")).strip()
        # Optional Markdown title following a destination.
        yield re.split(r'\s+["\']', value, maxsplit=1)[0]
    for match in REFERENCE.finditer(text):
        yield match.group(1) or match.group(2)


def resolve_link(document: Path, value: str, root: Path) -> Path | None:
    if not value or value.startswith("#") or SCHEME.match(value) or value.startswith("//"):
        return None
    path = unquote(value.split("#", 1)[0].split("?", 1)[0])
    if not path:
        return None
    return ((root / path.lstrip("/")) if path.startswith("/") else (document.parent / path)).resolve()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    args = parser.parse_args()
    root = args.root.resolve()
    checked = 0
    missing = []
    external = set()
    for document in markdown_files(root):
        if not document.is_file():  # A staged move can leave the old index entry until git add.
            continue
        text = document.read_text(encoding="utf-8-sig")
        for value in destinations(text):
            target = resolve_link(document, value, root)
            if target is None:
                continue
            try:
                target.relative_to(root)
            except ValueError:
                external.add((document.relative_to(root).as_posix(), value))
                continue
            checked += 1
            if not target.exists():
                missing.append((document.relative_to(root).as_posix(), value))
    for document, value in missing:
        print(f"MISSING {document}: {value}")
    for document, value in sorted(external):
        print(f"EXTERNAL_WORKSPACE {document}: {value}")
    print(f"Checked {checked} repository-local links; missing={len(missing)}; external-workspace={len(external)}.")
    print("Remote URLs and heading/fragment validity are not checked.")
    return 1 if missing else 0


if __name__ == "__main__":
    raise SystemExit(main())
