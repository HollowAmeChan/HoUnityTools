#!/usr/bin/env python3
"""Prepare a guarded, JsonUtility-readable shadow recovery plan; never edit Unity assets.

Run outside Unity. Private material snapshots and generated plans belong outside
both the package checkout and the production project. Colors stay raw sRGB.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import math
import re
from pathlib import Path, PurePosixPath


KIND = "HoShader.ShadowResponseRecovery"
PACKAGES = {
    "Cloth": "com.hoshader.cloth-urp",
    "Hair": "com.hoshader.hair-urp",
    "Skin": "com.hoshader.skin-urp-stylized",
    "Eye": "com.hoshader.skin-urp-stylized",
    "Environment": "com.hoshader.environment-urp",
}
SHAPES = ("_ShadowBorder", "_ShadowBlur", "_Shadow2ndBorder", "_Shadow2ndBlur",
          "_Shadow3rdBorder", "_Shadow3rdBlur", "_ShadowBorderRange")
MATH_FILES = ("lil_common_functions.hlsl", "lil_common_frag.hlsl",
              "lil_pass_forward_normal.hlsl", "lil_common_input.hlsl")
PBR_NAMES = {"_UseReflection", "_Smoothness", "_SmoothnessTex", "_Metallic",
             "_MetallicGlossMap", "_Reflectance", "_ReflectanceTex", "_BumpMap",
             "_BumpScale", "_Bump2ndMap", "_Bump2ndScale", "_MainTex",
             "_MainTex_ScrollRotate", "_GSAAStrength", "_SpecularStrength",
             "_ApplySpecular", "_UseSilk", "_SilkStrength", "_SilkAnisotropy",
             "_SilkViewShift", "_ApplyReflection", "_ReflectionCubeTex",
             "_ReflectionCubeColor", "_ReflectionCubeOverride",
             "_ReflectionCubeEnableLighting", "_UseAODarkening", "_UseShadow",
             "_BackfaceForceShadow"}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def read_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def fingerprint(value) -> str:
    data = json.dumps(value, sort_keys=True, ensure_ascii=True, separators=(",", ":"), allow_nan=False)
    return hashlib.sha256(data.encode("utf-8")).hexdigest()


def asset_path(value: str, prefix: str = "Assets") -> str:
    value = value.replace("\\", "/")
    p = PurePosixPath(value)
    require(bool(p.parts) and not p.is_absolute() and ".." not in p.parts and p.parts[0] == prefix,
            "Invalid Unity asset path: " + value)
    return p.as_posix()


def asset_guid(path: Path) -> str:
    text = Path(str(path) + ".meta").read_text(encoding="utf-8-sig")
    match = re.search(r"(?m)^guid:\s*([0-9a-f]{32})\s*$", text)
    require(match is not None, "Missing GUID: " + str(path))
    return match.group(1)


def scalar(props: dict, name: str) -> float:
    require(name in props, "Missing effective source property: " + name)
    value = float(props[name]["scalar"])
    require(math.isfinite(value), "Non-finite source property: " + name)
    return value


def property_record(p: dict) -> dict:
    v, scale, offset = p.get("value", {}), p.get("scale", {}), p.get("offset", {})
    return {
        "name": p["name"], "type": p["type"], "scalar": p.get("scalar", 0),
        "x": v.get("x", 0), "y": v.get("y", 0), "z": v.get("z", 0), "w": v.get("w", 0),
        "textureGuid": p.get("textureGuid", ""), "texturePath": p.get("texturePath", ""),
        "textureLocalId": p.get("textureLocalId", 0),
        "textureSha256": p.get("textureSha256", ""),
        "textureMetaSha256": p.get("textureMetaSha256", ""),
        "dependencyHash": p.get("dependencyHash", ""),
        "scaleX": scale.get("x", 0), "scaleY": scale.get("y", 0),
        "offsetX": offset.get("x", 0), "offsetY": offset.get("y", 0),
    }


def selected_property(name: str) -> bool:
    return name.startswith(("_Shadow", "_AO", "_PBR")) or name in PBR_NAMES


def load_effective_snapshots(inventory: dict) -> dict:
    """Keep full snapshots in the external provenance sidecar, not in this repository."""
    snapshots = {}
    preview_paths = sorted({s["sourcePreviewPath"] for s in inventory["sourceRows"]})
    for preview_path in preview_paths:
        preview = read_json(Path(preview_path))
        for proposal in preview["proposals"]:
            guid = proposal["sourceGuid"]
            inputs = proposal["sourceInputs"]
            identity = fingerprint(sorted(inputs, key=lambda p: p["name"]))
            if guid in snapshots:
                require(snapshots[guid]["inputsFingerprint"] == identity,
                        "Inconsistent effective source snapshots for " + guid)
                continue
            snapshots[guid] = {
                "sourceGuid": guid, "previewPath": str(Path(preview_path).resolve()),
                "previewSha256": sha(Path(preview_path)), "inputsFingerprint": identity,
                "sourceInputs": inputs, "sourceState": proposal.get("sourceState"),
            }
    require(set(snapshots) == {s["sourceGuid"] for s in inventory["sourceRows"]},
            "Effective snapshot source set does not match inventory")
    return snapshots


def directory_manifest(project: Path, active_assets: list[dict]) -> list[dict]:
    wanted = set()
    for ramp in active_assets:
        if ramp["isPackagePreset"]:
            continue
        parent = PurePosixPath(ramp["assetPath"]).parent
        while str(parent) != "Assets":
            wanted.add(parent.as_posix())
            parent = parent.parent
    result = []
    for path in sorted(wanted, key=lambda p: (len(PurePosixPath(p).parts), p)):
        parent = PurePosixPath(path).parent.as_posix()
        full = project / path
        require(not full.exists() or full.is_dir(), "Directory collides with file: " + path)
        result.append({"path": path, "parentPath": parent, "exists": full.is_dir(),
                       "metaExists": Path(str(full) + ".meta").exists(), "create": not full.exists(),
                       "parentWillBeCreated": parent in wanted and not (project / parent).exists(),
                       "depth": len(PurePosixPath(path).parts)})
    return result


def generate(inventory_path: Path, project: Path, provenance_path: Path, generated_at: str):
    inventory = read_json(inventory_path)
    require(not inventory.get("errors"), "Inventory contains unresolved errors")
    sources = {s["sourceGuid"]: s for s in inventory["sourceRows"]}
    require(len(sources) == len(inventory["sourceRows"]), "Duplicate source GUID")
    snapshots = load_effective_snapshots(inventory)
    shader_evidence = {s["sourceShaderPath"]: s for s in inventory["sourceShaderEvidence"]}
    math_paths = {Path(s["currentSourceFile"]).resolve().parent / "Includes" / name
                  for s in shader_evidence.values() for name in MATH_FILES}
    for path in math_paths:
        require(path.is_file(), "Missing original source math include: " + str(path))
    source_math_files = [{"path": str(path.resolve()), "sha256": sha(path)}
                         for path in sorted(math_paths, key=lambda p: str(p))]
    rows, ramps = [], {}
    neutral_presets = [{"targetFamily": f, "packageId": package,
                        "assetPath": f"Packages/{package}/Runtime/Ramps/ShadowRamp-Neutral.asset"}
                       for f, package in sorted(PACKAGES.items())]
    neutral_paths = {x["targetFamily"]: x["assetPath"] for x in neutral_presets}
    for target in sorted(inventory["targetRows"], key=lambda t: t["targetPath"]):
        source = sources[target["sourceGuid"]]
        family = target["targetFamily"]
        require(family in PACKAGES, "Unknown target family: " + family)
        path = asset_path(target["targetPath"])
        full = project / path
        require(full.is_file(), "Missing target material: " + path)
        guid = asset_guid(full)
        require(guid == target["targetGuid"], "Target GUID changed: " + path)
        backup = Path(source["sourceBackup"])
        require(backup.is_file() and sha(backup) == source["sourceSha256"],
                "Original source backup identity changed: " + str(backup))
        props = {p["name"]: p for p in snapshots[source["sourceGuid"]]["sourceInputs"]}
        shape = {name: scalar(props, name) for name in SHAPES}
        shape_signature = fingerprint(shape)
        require(shape_signature == source["fourLaneLUTShapeSignature"],
                "Source shape signature mismatch: " + source["sourceGuid"])
        active = scalar(props, "_UseShadow") != 0 and scalar(props, "_ShadowStrength") > 0
        require(active == target["activeShadow"], "Active-shadow mismatch: " + path)
        ramp_path = (asset_path(target["suggestedRampDirectory"]) + "/ShadowRamp-" + shape_signature[:12] + ".asset"
                     if active else neutral_paths[family])
        lut = {"assetPath": ramp_path, "shapeSignature": shape_signature if active else "neutral",
               "isPackagePreset": not active, "isNeutral": not active,
               "border1": shape["_ShadowBorder"] if active else 0,
               "blur1": shape["_ShadowBlur"] if active else 0,
               "border2": shape["_Shadow2ndBorder"] if active else 0,
               "blur2": shape["_Shadow2ndBlur"] if active else 0,
               "border3": shape["_Shadow3rdBorder"] if active else 0,
               "blur3": shape["_Shadow3rdBlur"] if active else 0,
               "borderRange": shape["_ShadowBorderRange"] if active else 0,
               "targetGuids": []}
        if ramp_path in ramps:
            require({k: v for k, v in ramps[ramp_path].items() if k != "targetGuids"} ==
                    {k: v for k, v in lut.items() if k != "targetGuids"},
                    "Truncated shape name collision: " + ramp_path)
        else:
            ramps[ramp_path] = lut
        ramps[ramp_path]["targetGuids"].append(guid)
        evidence = shader_evidence[source["sourceShaderPath"]]
        require(evidence["expectedSha256"] == evidence["currentSha256"], "Original shader evidence mismatch")
        shader_file = Path(evidence["currentSourceFile"])
        require(sha(shader_file) == evidence["expectedSha256"], "Original shader changed since inventory")
        source_props = [property_record(p) for _, p in sorted(props.items()) if selected_property(p["name"])]
        rows.append({
            "sourceGuid": source["sourceGuid"], "sourcePath": source["sourcePath"],
            "sourceSha256": source["sourceSha256"], "sourceMetaSha256": source["sourceMetaSha256"],
            "sourceShaderGuid": source["sourceShaderGuid"], "sourceShaderPath": source["sourceShaderPath"],
            "sourceShaderSha256": evidence["expectedSha256"], "sourceShaderFilePath": str(shader_file.resolve()),
            "sourceBackupPath": str(backup.resolve()),
            "sourceBackupSha256": sha(backup), "sourceInputsFingerprint": snapshots[source["sourceGuid"]]["inputsFingerprint"],
            "targetGuid": guid, "targetPath": path, "targetSha256": sha(full),
            "targetMetaSha256": sha(Path(str(full) + ".meta")),
            "inventoryTargetSha256": target["targetSha256"],
            "targetChangedSinceInventory": sha(full) != target["targetSha256"],
            "targetFamily": family, "activeShadow": active, "isFork": target["isFork"],
            "shapeSignature": shape_signature, "rampAssetPath": ramp_path, "rampIsPackagePreset": not active,
            "sourceAOParams": {"x": scalar(props, "_AOSource"), "y": scalar(props, "_AORealtimeWeight"),
                               "z": scalar(props, "_AOLevel"), "w": scalar(props, "_AOContrast")},
            "sourceAOMapGuid": props["_AOMap"].get("textureGuid", ""),
            "sourceAOMaskGuid": props["_AOMask"].get("textureGuid", ""),
            "sourceProperties": source_props,
        })
    require(len({r["targetGuid"] for r in rows}) == len(rows), "Duplicate target GUID")
    active_ramps = [r for r in ramps.values() if not r["isNeutral"]]
    provenance = {"schemaVersion": 1, "kind": KIND + ".Provenance", "inventoryPath": str(inventory_path.resolve()),
                  "inventorySha256": sha(inventory_path), "sourceSnapshots": [snapshots[g] for g in sorted(snapshots)],
                  "sourceRows": inventory["sourceRows"], "sourceShaderEvidence": inventory["sourceShaderEvidence"],
                  "inventoryValidation": inventory.get("validation", {})}
    provenance_bytes = (json.dumps(provenance, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode("utf-8")
    plan = {"schemaVersion": 1, "kind": KIND, "generatedAtUtc": generated_at,
            "projectRoot": str(project.resolve()), "inventoryPath": str(inventory_path.resolve()),
            "inventorySha256": sha(inventory_path), "provenancePath": str(provenance_path.resolve()),
            "provenanceSha256": hashlib.sha256(provenance_bytes).hexdigest(),
            "colorEncoding": "raw-sRGB-Color; no CPU gamma conversion",
            "sourceMathFiles": source_math_files,
            "rows": rows, "ramps": sorted(ramps.values(), key=lambda r: r["assetPath"]),
            "directories": directory_manifest(project, active_ramps), "neutralPresets": neutral_presets,
            "summary": {"sourceCount": len(sources), "targetCount": len(rows),
                        "activeTargetCount": sum(r["activeShadow"] for r in rows),
                        "disabledTargetCount": sum(not r["activeShadow"] for r in rows),
                        "activeShapeCount": len({r["shapeSignature"] for r in active_ramps}),
                        "activeOwnedRampCount": len(active_ramps),
                        "usedNeutralPresetCount": sum(r["isNeutral"] for r in ramps.values()),
                        "directoriesToCreate": 0,
                        "targetsChangedSinceInventory": sum(r["targetChangedSinceInventory"] for r in rows)}}
    plan["summary"]["directoriesToCreate"] = sum(x["create"] for x in plan["directories"])
    plan["inputFingerprint"] = fingerprint({k: v for k, v in plan.items() if k not in ["generatedAtUtc", "inputFingerprint"]})
    return plan, provenance_bytes


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inventory", required=True, type=Path)
    parser.add_argument("--project", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--provenance", required=True, type=Path)
    parser.add_argument("--generated-at", help="Optional UTC string for reproducible preparation")
    args = parser.parse_args()
    checkout = Path(__file__).resolve().parents[2]
    identity = read_json(args.inventory)
    protected = {args.inventory.resolve()}
    protected.update(Path(s[name]).resolve() for s in identity["sourceRows"]
                     for name in ["sourceBackup", "sourcePreviewPath"])
    protected.update(Path(s["currentSourceFile"]).resolve() for s in identity["sourceShaderEvidence"])
    protected.update(Path(s["currentSourceFile"]).resolve().parent / "Includes" / name
                     for s in identity["sourceShaderEvidence"] for name in MATH_FILES)
    for out in [args.output, args.provenance]:
        require(not out.resolve().is_relative_to(checkout), "Private output must stay outside the package checkout")
        require(not out.resolve().is_relative_to(args.project.resolve()), "Preparation must not modify the Unity project")
        require(out.resolve() not in protected, "Preparation must not overwrite any inventory, snapshot, backup or source shader")
    require(args.output.resolve() != args.provenance.resolve(), "Plan and provenance paths must differ")
    generated_at = args.generated_at or dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds")
    plan, provenance_bytes = generate(args.inventory.resolve(), args.project.resolve(), args.provenance.resolve(), generated_at)
    # All validation finishes before either output is written. Only these explicit external files are touched.
    for out in [args.output, args.provenance]:
        out.parent.mkdir(parents=True, exist_ok=True)
    args.provenance.write_bytes(provenance_bytes)
    args.output.write_text(json.dumps(plan, ensure_ascii=False, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    print(json.dumps({"output": str(args.output.resolve()), "provenance": str(args.provenance.resolve()),
                      "inputFingerprint": plan["inputFingerprint"], "summary": plan["summary"]}, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
