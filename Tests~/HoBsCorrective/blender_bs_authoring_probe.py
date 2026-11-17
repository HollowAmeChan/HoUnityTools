"""Headless smoke test for the HoTools BS corrective authoring controller."""
import argparse
import json
import sys
from pathlib import Path

import bpy


ADDON_DIR = Path(r"C:\Users\hhh12\AppData\Roaming\Blender Foundation\Blender\4.5\scripts\addons\HoTools")
if str(ADDON_DIR) not in sys.path:
    sys.path.insert(0, str(ADDON_DIR))


def build_scene():
    arm_data = bpy.data.armatures.new("BsAuthoringArmatureData")
    arm = bpy.data.objects.new("BsAuthoringArmature", arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bone = arm_data.edit_bones.new("UpperArm.L")
    bone.head = (0, 0, 0)
    bone.tail = (0, 1, 0)
    bpy.ops.object.mode_set(mode="OBJECT")

    mesh_data = bpy.data.meshes.new("CoatMesh")
    mesh_data.from_pydata([(0, 0, 0), (1, 0, 0), (0, 1, 0)], [], [(0, 1, 2)])
    mesh = bpy.data.objects.new("Coat", mesh_data)
    bpy.context.scene.collection.objects.link(mesh)
    mesh.shape_key_add(name="Basis")
    corrective = mesh.shape_key_add(name="ArmRaise.L")
    corrective.data[1].co.x = 1.2
    return arm, mesh


def main(output):
    bpy.ops.preferences.addon_enable(module="HoTools")
    from HoTools.Rbf.bs_corrective import build_export_config, generate_native_drivers

    arm, mesh = build_scene()
    settings = arm.hotools_bs_corrective
    rule = settings.rules.add()
    rule.id = "probe-rule"
    rule.bone = "UpperArm.L"
    target = rule.targets.add()
    target.mesh = mesh
    target.shape_key = "ArmRaise.L"
    point = rule.pose_points.add()
    point.x = 1.57079632679
    point.weight = 1.0

    assert generate_native_drivers(arm) == 1
    assert len(arm.data.bones) == 1
    assert mesh.data.shape_keys.animation_data is not None
    assert len(mesh.data.shape_keys.animation_data.drivers) == 1

    config = build_export_config(arm, measurement_bones={"UpperArm.L": "MCH_UpperArm.L"})
    assert config["version"] == 2
    assert config["poseSpace"] == "LOCAL"
    assert config["rules"][0]["bone"] == "UpperArm.L"
    assert config["rules"][0]["measurementBone"] == "MCH_UpperArm.L"

    output.write_text(json.dumps(config, indent=2, ensure_ascii=False), encoding="utf-8")
    print("BS_AUTHORING_PROBE_OK")


parser = argparse.ArgumentParser()
parser.add_argument("--output", type=Path, required=True)
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
args = parser.parse_args(argv)
args.output.parent.mkdir(parents=True, exist_ok=True)
main(args.output)
