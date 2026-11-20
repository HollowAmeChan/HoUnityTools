"""Export calibrated reference fixtures in both direct and MCH coordinates."""
import json
import sys
from pathlib import Path

import bpy
from mathutils import Euler, Matrix, Vector

folder = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
folder.mkdir(parents=True, exist_ok=True)
bpy.ops.preferences.addon_enable(module="HoTools")
from HoTools.Exporter.HumanoidMappingExporter import HumanoidMappingExporter as Exporter
from HoTools.Exporter.FbxExporter import FBXExporter

for kind in ("direct", "mch"):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.context.scene.unit_settings.scale_length = .01
    data = bpy.data.armatures.new("ReferenceRigData")
    rig = bpy.data.objects.new("ReferenceRig", data)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    rig.rotation_euler = (.2, -.35, .17)
    rig.scale = (1.7, 1.7, 1.7)
    bpy.ops.object.mode_set(mode="EDIT")
    parent = data.edit_bones.new("Root")
    parent.head, parent.tail, parent.roll = (0, 0, 0), (.3, .8, .2), .37
    bone = data.edit_bones.new("Main")
    bone.head, bone.tail, bone.roll, bone.parent = parent.tail, (1.2, 1.5, .7), -.61, parent
    child = data.edit_bones.new("Tip")
    child.head, child.tail, child.parent = bone.tail, (1.5, 2.0, 1.1), bone
    bpy.ops.object.mode_set(mode="OBJECT")
    for name, slot in (("Root", "hips"), ("Main", "spine"), ("Tip", "chest")):
        data.bones[name].hotools_boneprops.humanoidMapping = slot
    for i, pose_bone in enumerate(rig.pose.bones):
        pose_bone.matrix_basis = Matrix.LocRotScale(Vector((.1*i, .03, -.02)),
            Euler((.25+i*.1, -.1, .2), "XYZ").to_quaternion(), Vector((1.05, 1.05, 1.05)))
        props = pose_bone.bone.hotools_boneprops
        props.humanoidTPoseCached = True
        props.humanoidTPoseMatrixBasis = [v for row in pose_bone.matrix_basis for v in row]
    bpy.context.view_layer.update()
    reference = Exporter.capture_reference_poses([rig])
    mapping = Exporter.build_export_dict([rig], reference)
    for pose_bone in rig.pose.bones:
        pose_bone.matrix_basis = Matrix.Identity(4)
    maps = {}
    if kind == "mch":
        data.bones["Main"].hotools_boneprops.generateMCH = True
        maps = FBXExporter.clear_armatures_bone_rotation([rig], [rig], rig)
        for pose_bone in rig.pose.bones:
            for constraint in list(pose_bone.constraints):
                pose_bone.constraints.remove(constraint)
    Exporter.update_reference_poses(mapping, [rig], reference, maps)
    (folder / f"{kind}.json").write_text(json.dumps(mapping), encoding="utf-8")
    options = dict(use_selection=True, object_types={"ARMATURE"}, add_leaf_bones=False,
        primary_bone_axis="Y", secondary_bone_axis="X", bake_anim=False, axis_forward="-Z", axis_up="Y",
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL")
    bpy.context.view_layer.update()
    bpy.ops.export_scene.fbx(filepath=str(folder / f"{kind}_rest.fbx"), **options)
    inverse = rig.matrix_world.inverted()
    poses = {}
    sources = {target: source for source, target in maps.get(rig.name, {}).items()}
    for pose_bone in rig.pose.bones:
        rest = rig.matrix_world @ pose_bone.bone.matrix_local
        item = reference[rig.name][sources.get(pose_bone.name, pose_bone.name)]
        pose = inverse @ item["pose"] @ item["rest"].inverted() @ rest
        poses[pose_bone.name] = pose
        kwargs = {}
        if pose_bone.parent:
            kwargs = {"parent_matrix": poses[pose_bone.parent.name],
                      "parent_matrix_local": pose_bone.parent.bone.matrix_local}
        pose_bone.matrix_basis = pose_bone.bone.convert_local_to_pose(pose, pose_bone.bone.matrix_local, invert=True, **kwargs)
    bpy.context.view_layer.update()
    bpy.ops.export_scene.fbx(filepath=str(folder / f"{kind}_tpose.fbx"), **options)
print("HUMANOID_TPOSE_FBX_FIXTURES_OK", flush=True)
