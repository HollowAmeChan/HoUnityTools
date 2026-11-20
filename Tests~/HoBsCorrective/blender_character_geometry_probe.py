"""Read corrective mesh geometry from the original blend without modifying the file."""
import json
import sys
from pathlib import Path

import bpy
import numpy as np
from Utils import shapekey_utils
from HoTools.BoneTools.HumanoidDebug.rig import cached_reference_basis

folder = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
folder.mkdir(parents=True, exist_ok=True)
armature = bpy.data.objects["Armature-Chu2u"]
armature.data.pose_position = "REST"
report = {"bones": [{"name": bone.name,
                      "position": list(armature.matrix_world @ bone.head_local)}
                     for bone in armature.data.bones], "meshes": []}
report["tpose"] = []
for name in ("upper_arm.L", "upper_arm.R"):
    q = cached_reference_basis(armature.pose.bones[name]).to_quaternion()
    report["tpose"].append({"name": name, "rotation": [q.x, q.y, q.z, q.w]})
for name in ("外衣", "外衣-额外带子"):
    obj = bpy.data.objects[name]
    for modifier in list(obj.modifiers):
        if (not modifier.show_viewport or modifier.type == "NODES" or
                modifier.type == "SOLIDIFY" and modifier.use_flip_normals):
            obj.modifiers.remove(modifier)
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    keys = [key.name for key in obj.data.shape_keys.key_blocks]
    basis = None
    count = None
    with (folder / (name + ".bin")).open("wb") as stream:
        for index, key in enumerate(keys):
            mesh, coordinates = shapekey_utils._evaluated_positions_for_key(obj, index, depsgraph)
            points = coordinates.reshape((-1, 3)).astype(np.float64)
            matrix = np.array(obj.matrix_world)
            points = points @ matrix[:3, :3].T + matrix[:3, 3]
            if basis is None:
                basis = points.copy()
                count = len(points)
                points.astype("<f4").tofile(stream)
            else:
                (points - basis).astype("<f4").tofile(stream)
            bpy.data.meshes.remove(mesh)
    report["meshes"].append({"name": name, "vertices": count, "keys": keys[1:]})
(folder / "geometry.json").write_text(json.dumps(report, ensure_ascii=False), encoding="utf-8")
print("BLENDER_CHARACTER_GEOMETRY_OK", report["meshes"], flush=True)
