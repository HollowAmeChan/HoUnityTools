"""Real FBX transforms, with nontrivial bind orientation and MCH preprocessing."""
import argparse
import json
import sys
from pathlib import Path

import bpy
from mathutils import Euler

ADDON = Path(r"C:\Users\hhh12\AppData\Roaming\Blender Foundation\Blender\4.5\scripts\addons\HoTools")
sys.path.insert(0, str(ADDON))
bpy.ops.preferences.addon_enable(module="HoTools")
from HoTools.Exporter.FbxExporter import FBXExporter
from HoTools.Rbf import bs_corrective as bs

parser = argparse.ArgumentParser()
parser.add_argument("--output", type=Path, required=True)
folder = parser.parse_args(sys.argv[sys.argv.index("--") + 1:]).output
folder.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
data = bpy.data.armatures.new("AxisRigData")
arm = bpy.data.objects.new("AxisRig", data)
bpy.context.scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
parent = data.edit_bones.new("Parent")
parent.head, parent.tail, parent.roll = (0, 0, 0), (.3, .8, .2), .37
bone = data.edit_bones.new("Main")
bone.head, bone.tail, bone.roll, bone.parent = parent.tail, (1.2, 1.5, .7), -.61, parent
bpy.ops.object.mode_set(mode="OBJECT")
mesh_data = bpy.data.meshes.new("AxisMeshData")
mesh_data.from_pydata([(0, 0, 0), (1, 0, 0), (0, 1, 0)], [], [(0, 1, 2)])
mesh = bpy.data.objects.new("AxisMesh", mesh_data)
bpy.context.scene.collection.objects.link(mesh)
mesh.parent = arm
mesh.select_set(True)
mesh.shape_key_add(name="Basis")
shape = mesh.shape_key_add(name="Corrective")
shape.data[1].co.z = .2
rule = arm.hotools_bs_corrective.rules.add()
rule.id, rule.bone = "axis-rule", "Main"
rule.influence_objects.add().mesh = mesh
point = bs._add_point(arm, rule, (.8, .15, -.1))
point.id, point.bs_name, point.radius, point.weight = "axis-pose", "Corrective", 1.7453292519943295, .7
assert bs.generate_native_drivers(arm) == 1
for kind in ("direct", "mch"):
    arm.pose.bones["Main"].rotation_quaternion = (1, 0, 0, 0)
    bpy.context.view_layer.update()
    mapping = {}
    rest_local = (data.bones["Parent"].matrix_local.inverted() @ data.bones["Main"].matrix_local).to_quaternion()
    if kind == "mch":
        data.bones["Main"].hotools_boneprops.generateMCH = True
        mapping = FBXExporter.clear_armatures_bone_rotation([arm], [arm, mesh], arm)[arm.name]
        assert mapping == {"Main": "MCH_Main"}
        assert len(data.bones) == 3
    sidecar = FBXExporter.export_bs_corrective_metadata(
        [arm], str(folder / f"{kind}_neutral.fbx"), mch_name_maps={arm.name: mapping},
    )
    (folder / f"{kind}.json").write_text(Path(sidecar).read_text(encoding="utf-8"), encoding="utf-8")
    for state, angles in (("neutral", (0, 0, 0)), ("pose", (.8, .15, -.1))):
        rotation = Euler(angles, "XYZ").to_quaternion()
        # The pose is authored before MCH clears Main's rest axes. Preserve that
        # physical pose on the zeroed Main, instead of assigning the same numeric
        # quaternion to a different coordinate frame (which hid the old read bug).
        if kind == "mch":
            rotation = rest_local @ rotation @ rest_local.inverted()
        arm.pose.bones["Main"].rotation_quaternion = rotation
        bpy.context.view_layer.update()
        bpy.ops.export_scene.fbx(
            filepath=str(folder / f"{kind}_{state}.fbx"),
            use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
            primary_bone_axis="Y", secondary_bone_axis="X", bake_anim=False,
            axis_forward="-Z", axis_up="Y", use_mesh_modifiers=False,
        )
# Also exercise the user-facing HoFBX operator, including its metadata manifest,
# temporary constraint cleanup, fixed export settings and scene restoration.
arm.pose.bones["Main"].rotation_quaternion = (1, 0, 0, 0)
bpy.context.view_layer.update()
assert bpy.ops.ho.final_fbx_export(
    filepath=str(folder / "hofbx_neutral.fbx"),
    applyArmaturePose=False, exportUnityMetadata=True,
) == {"FINISHED"}
assert (folder / "hofbx_neutral.fbx").exists()
metadata = folder / "HoFBX/hofbx_neutral_bs_corrective.json"
assert metadata.exists()
(folder / "hofbx.json").write_text(metadata.read_text(encoding="utf-8"), encoding="utf-8")
print("BS_FBX_AXIS_FIXTURES_OK", folder)
