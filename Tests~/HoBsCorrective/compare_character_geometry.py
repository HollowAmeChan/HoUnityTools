"""Blender background: compare HoFBX mesh and every corrective delta by spatial correspondence."""
import argparse
import json
from pathlib import Path
import sys

import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree

root = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--blender", type=Path, default=root / "Temp~/YuqiuSceneParity20261120/Geometry")
parser.add_argument("--unity", type=Path, default=root / "Temp~/YuqiuSceneParityUnity/Geometry")
parser.add_argument("--output", type=Path, default=root / "Temp~/YuqiuSceneParity20261120/geometry_comparison.json")
args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
blender, unity = args.blender, args.unity
left = json.loads((blender / "geometry.json").read_text(encoding="utf-8"))
right = json.loads((unity / "geometry.json").read_text(encoding="utf-8"))
left_bones = {bone["name"]: bone["position"] for bone in left["bones"]}
right_bones = {bone["name"]: list(bone["position"].values()) for bone in right["bones"]}
landmarks = [name for name in left_bones if name in right_bones and not name.startswith(("MCH_", "aux_"))]
src = np.array([left_bones[name] for name in landmarks])
dst = np.array([right_bones[name] for name in landmarks])
matrix, _, _, _ = np.linalg.lstsq(np.column_stack([src, np.ones(len(src))]), dst, rcond=None)
report = {"landmarks": len(landmarks), "maxLandmarkError": float(np.linalg.norm(
    np.column_stack([src, np.ones(len(src))]) @ matrix - dst, axis=1).max()),
    "blenderToUnity": matrix.tolist(), "meshes": []}
for bm in left["meshes"]:
    um = next(mesh for mesh in right["meshes"] if mesh["name"] == bm["name"])
    b = np.fromfile(blender / (bm["name"] + ".bin"), dtype="<f4").reshape((-1, bm["vertices"], 3))
    u = np.fromfile(unity / (um["name"] + ".bin"), dtype="<f4").reshape((-1, um["vertices"], 3))
    base = b[0] @ matrix[:3, :] + matrix[3, :]
    tree = KDTree(len(base))
    for i, co in enumerate(base):
        tree.insert(Vector(co), i)
    tree.balance()
    correspondence = [tree.find(Vector(co)) for co in u[0]]
    indexes = np.array([item[1] for item in correspondence])
    base_error = np.array([item[2] for item in correspondence])
    row = {"mesh": bm["name"], "blenderVertices": bm["vertices"], "unityVertices": um["vertices"],
           "maxBaseError": float(base_error.max()), "poses": []}
    for i, key in enumerate(bm["keys"]):
        j = um["keys"].index(key)
        delta = b[i+1] @ matrix[:3, :]
        errors = np.linalg.norm(delta[indexes] - u[j+1], axis=1)
        row["poses"].append({"name": key, "maxDeltaError": float(errors.max()),
                             "maxDeltaLength": float(np.linalg.norm(delta, axis=1).max())})
    row["maxDeltaError"] = max(item["maxDeltaError"] for item in row["poses"])
    report["meshes"].append(row)
    print("GEOMETRY", json.dumps(row, ensure_ascii=False), flush=True)
args.output.write_text(
    json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print("CHARACTER_GEOMETRY_COMPARISON_DONE", report["maxLandmarkError"], flush=True)
