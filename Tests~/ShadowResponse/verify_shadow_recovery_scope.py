"""Verify the complete material-file scope against a pre-migration hash baseline."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import re


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("baseline", type=Path)
    cli.add_argument("legacy_journal", type=Path)
    cli.add_argument("effect_journal", type=Path)
    cli.add_argument("report", type=Path)
    args = cli.parse_args()
    if args.report.exists():
        raise RuntimeError("Preserve existing evidence")
    read = lambda p: json.loads(p.read_text(encoding="utf-8"))
    base, legacy, effect = map(read, (args.baseline, args.legacy_journal, args.effect_journal))
    root = Path(base["projectRoot"])
    if not all(Path(j["projectRoot"]).resolve() == root.resolve() and j["state"] == "Verified" for j in (legacy, effect)):
        raise RuntimeError("Both transactions must be verified in this project")
    allowed = {r["row"]["targetPath"] for r in legacy["rows"]} | {r["source"]["path"] for r in effect["rows"]}
    before = {f["path"]: f for f in base["files"]}
    changed, missing, wrong_guid = [], [], []
    for path, stamp in before.items():
        file = root / path
        if not file.is_file():
            missing.append(path)
            continue
        if sha(file) != stamp["sha256"]:
            changed.append(path)
        if stamp["kind"] != "material":
            match = re.search(r"(?m)^guid:\s*([0-9a-f]{32})\s*$", file.read_text(encoding="utf-8-sig"))
            if not match or match[1] != stamp["guid"]:
                wrong_guid.append(path)
    after_materials = {p.relative_to(root).as_posix() for p in (root / "Assets").rglob("*.mat")}
    prior_materials = {f["path"] for f in base["files"] if f["kind"] == "material"}
    forbidden = sorted(set(changed) - allowed)
    untouched_targets = sorted(allowed - set(changed))
    extras = sorted(after_materials - prior_materials)
    result = {"passed": not any((missing, wrong_guid, forbidden, untouched_targets, extras)),
              "materialCount": len(after_materials), "baselineFiles": len(before), "allowedMaterials": len(allowed),
              "changedMaterials": len(changed), "unchangedOtherMaterials": len(after_materials) - len(allowed),
              "changedMetaFiles": sum(p.endswith(".meta") for p in changed), "missing": missing, "wrongGuids": wrong_guid,
              "unexpectedChanges": forbidden, "untouchedTargets": untouched_targets, "extraMaterials": extras,
              "changedPaths": sorted(changed)}
    args.report.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in result.items() if k != "changedPaths"}, ensure_ascii=False))
    if not result["passed"]:
        raise RuntimeError("Unexpected production material scope; inspect the report")


if __name__ == "__main__":
    main()
