"""Independent mmd_tools foot oracle; run Blender in background with --python.

Blender ... --background --factory-startup --python this.py -- \
    --pmx White.pmx --vmd dance.vmd --output sour-foot.json
No source assets or .blend files are written.
"""
import argparse
import json
import pathlib
import sys

import bpy


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--pmx", required=True, type=pathlib.Path)
    parser.add_argument("--vmd", required=True, type=pathlib.Path)
    parser.add_argument("--output", required=True, type=pathlib.Path)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1 :])
    bpy.ops.preferences.addon_enable(module="mmd_tools")
    import mmd_tools

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.mmd_tools.import_model(
        directory=str(args.pmx.parent),
        files=[{"name": args.pmx.name}],
        types={"ARMATURE"},
        scale=0.08,
        rename_bones=False,
    )
    armature = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    mapping = {
        (b.mmd_bone.name_j or b.name): b for b in armature.pose.bones
        if not b.name.startswith("_")
    }
    bpy.ops.object.select_all(action="DESELECT")
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.mmd_tools.import_vmd(
        directory=str(args.vmd.parent),
        files=[{"name": args.vmd.name}],
        margin=0,
        bone_mapper="PMX",
        scale=0.08,
        use_pose_mode=False,
    )

    def unity(vector):
        # mmd_tools PMXImporter uses Vector(v).xzy; Ho uses (-x,y,-z).
        return [-vector.x, vector.z, -vector.y]

    rows = []
    for half_frame in range(450, 631):
        frame = half_frame * 0.5
        # mmd_tools adds one Blender frame even when margin=0.
        bpy.context.scene.frame_set(int(frame) + 1, subframe=frame % 1)
        bpy.context.view_layer.update()
        evaluated = armature.evaluated_get(bpy.context.evaluated_depsgraph_get())
        row = {"vmdFrame": frame, "bones": {}}
        for name in ["下半身", *[
            side + part for side in ("左", "右")
            for part in ("足", "ひざ", "足首", "つま先", "足ＩＫ", "つま先ＩＫ")
        ]]:
            bone = evaluated.pose.bones[mapping[name].name]
            row["bones"][name] = unity(evaluated.matrix_world @ bone.head)
        rows.append(row)
    result = {
        "blender": bpy.app.version_string,
        "mmdTools": list(mmd_tools.bl_info["version"]),
        "pmx": str(args.pmx),
        "vmd": str(args.vmd),
        "scale": 0.08,
        "vmdFrameOffset": 1,
        "physics": False,
        "frames": rows,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print("HO_SOUR_FOOT_AUDIT", str(args.output))


if __name__ == "__main__":
    main()
