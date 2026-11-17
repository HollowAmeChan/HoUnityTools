"""Produce v3 authoring/Unity parity fixtures from real Blender 4.5 drivers."""
import argparse
import json
import math
import sys
import tempfile
from pathlib import Path

import bpy
from mathutils import Euler

ADDON_DIR = Path(r"C:\Users\hhh12\AppData\Roaming\Blender Foundation\Blender\4.5\scripts\addons\HoTools")
sys.path.insert(0, str(ADDON_DIR))


def mesh_object(name, names):
    data = bpy.data.meshes.new(name + "Data")
    data.from_pydata([(0, 0, 0), (1, 0, 0), (0, 1, 0)], [], [(0, 1, 2)])
    mesh = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(mesh)
    mesh.shape_key_add(name="Basis")
    for name in names:
        key = mesh.shape_key_add(name=name)
        key.data[1].co.x = 1.2
    return mesh


def main(output):
    bpy.ops.preferences.addon_enable(module="HoTools")
    from HoTools.Rbf import bs_corrective as bs
    from HoTools.Exporter.FbxExporter import FBXExporter
    data = bpy.data.armatures.new("BsAuthoringArmatureData")
    arm = bpy.data.objects.new("BsAuthoringArmature", data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    parent = data.edit_bones.new("Parent")
    parent.head, parent.tail = (0, 0, 0), (0, 1, 0)
    child = data.edit_bones.new("UpperArm.L")
    child.head, child.tail, child.parent = (0, 1, 0), (0, 2, 0), parent
    bpy.ops.object.mode_set(mode="OBJECT")
    coat = mesh_object("Coat", ("ArmRaise.L", "ArmRaise.Second", "ArmFold.L"))
    trim = mesh_object("Trim", ("StripeRaise", "StripeFold"))
    rule = arm.hotools_bs_corrective.rules.add()
    rule.id, rule.bone, rule.comment = "probe-rule", "UpperArm.L", "Two poses; one task drives several objects and shape keys."
    bs._ensure_base_point(rule)
    for index, (pose, targets) in enumerate([
        ((0.8, 0.15, -0.1), ((coat, "ArmRaise.L"), (coat, "ArmRaise.Second"), (trim, "StripeRaise"))),
        ((-0.5, 0.4, 0.2), ((coat, "ArmFold.L"), (trim, "StripeFold"))),
    ]):
        point, task = bs._add_point(arm, rule, pose)
        point.id, task.id = f"point-{index + 1}", f"task-{index + 1}"
        point.task_id, task.point_id = task.id, point.id
        for mesh, name in targets:
            target = task.targets.add()
            target.mesh, target.shape_key = mesh, name
    for mesh in (coat, trim):
        rule.influence_objects.add().mesh = mesh
    assert bs.generate_native_drivers(arm) == 5
    assert len(arm.data.bones) == 2
    config = bs.build_export_config(arm, measurement_bones={"UpperArm.L": "MCH_UpperArm.L"})
    assert config["version"] == 3
    actual_path = FBXExporter.export_bs_corrective_metadata([arm], str(Path(tempfile.gettempdir()) / "BsAuthoringProbe.fbx"),
                    mch_name_maps={arm.name: {"UpperArm.L": "MCH_UpperArm.L"}})
    assert json.loads(Path(actual_path).read_text(encoding="utf-8")) == config
    output.write_text(json.dumps(config, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    samples = []
    poses = [(0, 0, 0), (0.8, 0.15, -0.1), (-0.5, 0.4, 0.2), (0.4, 0.1, 0),
             (0.2, -0.3, 0.7), (math.radians(-179), 0, 0)]
    for pose in poses:
        quaternion = Euler(pose, "XYZ").to_quaternion()
        arm.pose.bones[rule.bone].rotation_quaternion = quaternion
        arm.pose.bones["Parent"].rotation_quaternion = Euler((0.4, -0.7, 1.1), "XYZ").to_quaternion()
        bpy.context.view_layer.update()
        weights = {}
        for task in rule.tasks:
            value = bs._driver_namespace_rbf(rule.id, task.id, *pose)
            for target in task.targets:
                assert abs(target.mesh.data.shape_keys.key_blocks[target.shape_key].value - value) < 5e-5
            weights[task.id] = value
        samples.append({"quaternion": [quaternion.x, quaternion.y, quaternion.z, quaternion.w], "weights": weights})
    output.with_name("bs_authoring_samples.json").write_text(json.dumps(samples, indent=2) + "\n", encoding="utf-8")
    print("BS_AUTHORING_PROBE_OK", actual_path)


parser = argparse.ArgumentParser()
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
args.output.parent.mkdir(parents=True, exist_ok=True)
main(args.output)
