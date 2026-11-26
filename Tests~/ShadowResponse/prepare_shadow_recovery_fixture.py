"""Copy a small, exact recovery corpus into an existing private Unity host.

No source project writes. Package presets are redirected into this host's Assets.
Run after normalize_shadow_recovery_plan.py; this is not a production converter.
"""
from __future__ import annotations
import argparse
import copy
import hashlib
import json
from pathlib import Path
import shutil


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("plan", type=Path)
    parser.add_argument("host", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--suffix", default="")
    args = parser.parse_args()
    host = args.host.resolve()
    if not host.is_relative_to(Path("D:/Temp").resolve()) or not (host / "Assets").is_dir():
        raise RuntimeError("Use an existing disposable D:/Temp Unity host")
    if args.output.exists():
        raise RuntimeError("Preserve existing evidence")
    original = json.loads(args.plan.read_text(encoding="utf-8"))
    source = Path(original["projectRoot"])

    def prop(row: dict, name: str) -> dict:
        return next(p for p in row["sourceProperties"] if p["name"] == name)

    def spatial_score(row: dict) -> int:
        return sum(bool(p["textureGuid"]) for p in row["sourceProperties"] if p["name"].startswith("_Shadow")) * 10 + int(prop(row, "_ShadowMaskType")["scalar"] == 2) * 100

    selected = []
    for family in ("Cloth", "Hair", "Skin", "Eye", "Environment"):
        selected.append(max((r for r in original["rows"] if r["targetFamily"] == family and r["activeShadow"]), key=spatial_score))
    selected.append(next(r for r in original["rows"] if not r["activeShadow"]))
    selected.append(max((r for r in original["rows"] if r["activeShadow"]), key=lambda r: prop(r, "_Shadow3rdColor")["w"]))
    selected = list({r["targetGuid"]: r for r in selected}.values())
    plan = copy.deepcopy(original)
    plan["projectRoot"] = str(host)
    plan["rows"] = copy.deepcopy(selected)
    used = {r["rampAssetPath"] for r in selected}
    plan["ramps"] = [copy.deepcopy(r) for r in original["ramps"] if r["assetPath"] in used]
    selected_guids = {r["targetGuid"] for r in selected}
    for ramp in plan["ramps"]:
        ramp["targetGuids"] = [g for g in ramp["targetGuids"] if g in selected_guids]
    redirects = {}
    for ramp in plan["ramps"]:
        if ramp["isPackagePreset"]:
            path = ramp["assetPath"]
            family = path.split("/")[1].replace("com.hoshader.", "")
            redirects[path] = f"Assets/HoRecoveryFixturePresets/{family}/ShadowRamp-Neutral.asset"
            ramp["assetPath"] = redirects[path]
        if args.suffix:
            old_path = ramp["assetPath"]
            new_path = old_path.removesuffix(".asset") + "-" + args.suffix + ".asset"
            redirects.update({key: new_path for key, value in redirects.items() if value == old_path})
            redirects[old_path] = new_path
            ramp["assetPath"] = new_path
    for row in plan["rows"]:
        row["rampAssetPath"] = redirects.get(row["rampAssetPath"], row["rampAssetPath"])

    paths = {r["targetPath"] for r in plan["rows"]}
    paths.update(p["texturePath"] for r in plan["rows"] for p in r["sourceProperties"] if p["textureGuid"])
    copied = []
    for path in sorted(paths):
        if not path.startswith("Assets/"):
            raise RuntimeError(f"Fixture requires a project asset: {path}")
        for suffix in ("", ".meta"):
            src, dst = source / (path + suffix), host / (path + suffix)
            if not src.is_file():
                raise RuntimeError(f"Missing source file: {src}")
            if dst.exists():
                if digest(dst) != digest(src):
                    raise RuntimeError(f"Preserve different existing host input: {dst}")
            else:
                dst.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(src, dst)
            copied.append(path + suffix)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(plan, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"rows": len(selected), "rampAssets": len(plan["ramps"]), "copiedFiles": len(copied), "bytes": sum((host / p).stat().st_size for p in copied), "plan": str(args.output)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
