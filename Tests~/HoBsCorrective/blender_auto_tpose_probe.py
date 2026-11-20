"""Build full-body/finger reference poses for independent Unity solver checks."""
import json
import sys
from pathlib import Path

import bpy
from mathutils import Matrix

folder = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
folder.mkdir(parents=True, exist_ok=True)
bpy.ops.preferences.addon_enable(module="HoTools")
from HoTools.BoneTools import boneTPose
from HoTools.Exporter.HumanoidMappingExporter import HumanoidMappingExporter as Exporter

cases = []
for case in range(3):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    data = bpy.data.armatures.new("SolverRigData")
    rig = bpy.data.objects.new("SolverRig", data)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    rig.rotation_euler = (0, 0, .18) if case == 0 else (.25, -.1, .6)
    rig.scale = (1.3, 1.3, 1.3)
    rig.location = (.12, -.08, .07)
    specs = [
        ("hips", None, (0, 0, 1), (0, 0, 1.15)),
        ("spine", "hips", (0, 0, 1.15), (.02, -.03, 1.35)),
        ("chest", "spine", (.02, -.03, 1.35), (0, 0, 1.5)),
        ("upperchest", "chest", (0, 0, 1.5), (0, 0, 1.6)),
        ("neck", "upperchest", (0, 0, 1.6), (0, .01, 1.7)),
        ("head", "neck", (0, .01, 1.7), (0, .01, 1.9)),
    ]
    for side, sign in (("L", 1), ("R", -1)):
        vec = lambda x, y, z: (sign*x, y, z)
        suffix = "." + side
        specs += [
            ("upper_leg"+suffix, "hips", vec(.1, 0, 1), vec(.13, .12, .57)),
            ("lower_leg"+suffix, "upper_leg"+suffix, vec(.13, .12, .57), vec(.1, .02, .1)),
            ("foot"+suffix, "lower_leg"+suffix, vec(.1, .02, .1), vec(.1, -.15, .06)),
            ("toes"+suffix, "foot"+suffix, vec(.1, -.15, .06), vec(.1, -.25, .04)),
            ("shoulder"+suffix, "upperchest", vec(.05, 0, 1.56), vec(.23, -.01, 1.51)),
            ("upper_arm"+suffix, "shoulder"+suffix, vec(.23, -.01, 1.51), vec(.47, -.03, 1.3)),
            # Deliberately offset the joint from its parent's Blender tail.
            ("arm_helper"+suffix, "upper_arm"+suffix, vec(.48, -.03, 1.28), vec(.49, -.03, 1.27)),
            ("lower_arm"+suffix, "arm_helper"+suffix, vec(.49, -.03, 1.27), vec(.7, -.12, 1.15)),
            ("hand"+suffix, "lower_arm"+suffix, vec(.7, -.12, 1.15), vec(.83, -.17, 1.08)),
        ]
        for finger_index, finger in enumerate(("thumb", "index", "middle", "ring", "little")):
            start = vec(.77, -.20+finger_index*.032, 1.12)
            parent = "hand"+suffix
            for segment_index, segment in enumerate(("proximal", "intermediate", "distal")):
                name = finger+"_"+segment+suffix
                end = (start[0]+sign*(.035 if finger == "thumb" else .045),
                       start[1]-.025*(1+segment_index), start[2]-.02*(1+segment_index))
                specs.append((name, parent, start, end))
                parent, start = name, end
    bpy.ops.object.mode_set(mode="EDIT")
    for index, (name, parent, head, tail) in enumerate(specs):
        bone = data.edit_bones.new(name)
        bone.head, bone.tail, bone.roll = head, tail, -.7+index*.09
        if parent:
            bone.parent = data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    for name, _, _, _ in specs:
        if name.startswith("arm_helper") or (case == 2 and (name == "upperchest" or name.startswith(("index_", "little_")))):
            continue
        data.bones[name].hotools_boneprops.humanoidMapping = name
    bpy.context.view_layer.update()
    path = folder / f"case{case}.fbx"
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={"ARMATURE"},
        add_leaf_bones=True, primary_bone_axis="Y", secondary_bone_axis="X", bake_anim=False,
        axis_forward="-Z", axis_up="Y", apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL")
    before = {bone.name: bone.matrix_basis.copy() for bone in rig.pose.bones}
    assert bpy.ops.ho.humanoid_tpose_auto() == {"FINISHED"}
    errors = boneTPose.humanoid_tpose_solver.validate(rig)
    assert bpy.ops.ho.humanoid_tpose_save() == {"FINISHED"}
    (folder / f"case{case}.json").write_text(json.dumps(Exporter.build_export_dict([rig])), encoding="utf-8")
    assert bpy.ops.ho.humanoid_tpose_preview() == {"FINISHED"}
    for bone in rig.pose.bones:
        assert max(abs(bone.matrix_basis[r][c]-before[bone.name][r][c]) for r in range(4) for c in range(4)) < 1e-5
    cases.append({"name": f"case{case}", "validation": errors})
(folder / "blender_results.json").write_text(json.dumps(cases), encoding="utf-8")
print("AUTO_TPOSE_BLENDER_FIXTURES_OK", flush=True)
