"""Read and probe the authored chest attachment without saving the source blend."""
import json
import math
import pathlib
import sys

import bpy
from mathutils import Matrix


def matrix(value):
    return [float(value[r][c]) for r in range(4) for c in range(4)]


armature = bpy.data.objects["Armature-Swimsuit"]
names = [
    bone.name
    for bone in armature.pose.bones
    if "ChestAddon" in bone.name or bone.name in ("breast_L", "breast_R", "chest")
]
authored = {}
for name in names:
    bone = armature.pose.bones[name]
    constraints = []
    for constraint in bone.constraints:
        item = {
            "name": constraint.name,
            "type": constraint.type,
            "mute": constraint.mute,
            "influence": constraint.influence,
        }
        for key in ("subtarget", "track_axis", "lock_axis", "owner_space", "target_space"):
            if hasattr(constraint, key):
                item[key] = getattr(constraint, key)
        if hasattr(constraint, "targets"):
            item["targets"] = [
                {"bone": target.subtarget, "weight": target.weight}
                for target in constraint.targets
            ]
        constraints.append(item)
    authored[name] = {
        "parent": bone.parent.name if bone.parent else None,
        "rest": matrix(bone.bone.matrix_local),
        "constraints": constraints,
    }

# Freeze only this in-memory armature; all source files remain read-only.
armature.animation_data_clear()
baseline = {bone.name: bone.matrix_basis.copy() for bone in armature.pose.bones}
samples = []
cases = [
    ("rest", 0, 0),
    ("left_15", 15, 0),
    ("right_15", 0, 15),
    ("opposed_15", 15, -15),
    ("both_15", 15, 15),
    ("left_minus_15", -15, 0),
]
for label, left, right in cases:
    for bone in armature.pose.bones:
        bone.matrix_basis = baseline[bone.name]
    for name, angle in (("breast_L", left), ("breast_R", right)):
        armature.pose.bones[name].matrix_basis = baseline[name] @ Matrix.Rotation(
            math.radians(angle), 4, "X"
        )
    bpy.context.view_layer.update()
    evaluated = armature.evaluated_get(bpy.context.evaluated_depsgraph_get())
    samples.append(
        {
            "name": label,
            "bones": {
                name: matrix(evaluated.pose.bones[name].matrix) for name in names
            },
        }
    )

meshes = []
for obj in bpy.data.objects:
    if obj.type != "MESH":
        continue
    groups = {
        group.index: group.name
        for group in obj.vertex_groups
        if "ChestAddon" in group.name
    }
    if not groups:
        continue
    counts = {name: 0 for name in groups.values()}
    for vertex in obj.data.vertices:
        for weight in vertex.groups:
            if weight.group in groups and weight.weight > 0.001:
                counts[groups[weight.group]] += 1
    meshes.append({"name": obj.name, "weightedVertices": counts})

output = pathlib.Path(sys.argv[sys.argv.index("--") + 1])
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(
    json.dumps(
        {
            "blender": bpy.app.version_string,
            "source": bpy.data.filepath,
            "authored": authored,
            "samples": samples,
            "meshes": meshes,
        },
        ensure_ascii=False,
        indent=2,
    ),
    encoding="utf-8",
)
print("CHEST_AUDIT_WRITTEN", output)
