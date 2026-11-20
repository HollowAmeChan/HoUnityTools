"""Produce v5 independent corrective/Unity parity from real Blender drivers."""
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
    coat = mesh_object("Coat", ("_BSC_pose1", "_BSC_pose2"))
    trim = mesh_object("Trim", ("_BSC_pose1", "_BSC_pose2"))
    rule = arm.hotools_bs_corrective.rules.add()
    rule.id, rule.bone = "probe-rule", "UpperArm.L"
    bs._ensure_base_point(rule)
    for mesh in (coat, trim):
        rule.influence_objects.add().mesh = mesh
    for index, (pose, name) in enumerate([
        ((0.8, 0.15, -0.1), "_BSC_pose1"),
        ((-0.5, 0.4, 0.2), "_BSC_pose2"),
    ]):
        point = bs._add_point(arm, rule, pose)
        point.id, point.bs_name = f"task-{index + 1}", name
    assert bs.generate_native_drivers(arm) == 4
    assert len(arm.data.bones) == 2
    config = bs.build_export_config(arm, measurement_bones={"UpperArm.L": "MCH_UpperArm.L"})
    assert config["version"] == 5
    actual_path = FBXExporter.export_bs_corrective_metadata([arm], str(Path(tempfile.gettempdir()) / "BsAuthoringProbe.fbx"),
                    mch_name_maps={arm.name: {"UpperArm.L": "MCH_UpperArm.L"}})
    assert json.loads(Path(actual_path).read_text(encoding="utf-8")) == config
    output.write_text(json.dumps(config, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    samples = []
    poses = [(0, 0, 0), (0.8, 0.15, -0.1), (-0.5, 0.4, 0.2), (0.4, 0.1, 0),
             (0.2, -0.3, 0.7), (math.radians(-179), 0, 0)]
    import random
    rng = random.Random(5149)
    poses += [tuple(rng.uniform(-math.pi, math.pi) for _ in range(3)) for _ in range(100)]
    for pose in poses:
        quaternion = Euler(pose, "XYZ").to_quaternion()
        arm.pose.bones[rule.bone].rotation_quaternion = quaternion
        arm.pose.bones["Parent"].rotation_quaternion = Euler((0.4, -0.7, 1.1), "XYZ").to_quaternion()
        bpy.context.view_layer.update()
        weights = {}
        for point in bs._authored_points(rule):
            value = bs._driver_namespace_rbf(rule.id, point.id, *bs._current_local_pose(arm, rule.bone))
            for mesh in (coat, trim):
                assert abs(mesh.data.shape_keys.key_blocks[point.bs_name].value - value) < 5e-5
            weights[point.id] = value
        samples.append({"quaternion": [quaternion.x, quaternion.y, quaternion.z, quaternion.w], "weights": weights})
    output.with_name("bs_authoring_samples.json").write_text(json.dumps(samples, indent=2) + "\n", encoding="utf-8")
    cases = []
    for radius in (30, 50, 100, 180):
        for pose_angles in ((45, 0, 0), (0, 80, 0), (60, 95, -30), (179, 0, 0)):
            pose_q = bs.euler_xyz_quaternion(tuple(math.radians(v) for v in pose_angles))
            for angles in ((0, 0, 0), pose_angles, (60, 0, 0), (90, 0, 0), (145, 0, 0),
                           (-179, 0, 0), (60, 100, -30), (-120, 85, 150)):
                current = bs.euler_xyz_quaternion(tuple(math.radians(v) for v in angles))
                cases.append({"current": current, "pose": pose_q, "radius": math.radians(radius),
                              "weight": .7, "expected": bs.pose_weight(current, pose_q, math.radians(radius), .7)})
    output.with_name("bs_independent_cases.json").write_text(json.dumps({"items": cases}, indent=2) + "\n", encoding="utf-8")
    print("BS_AUTHORING_PROBE_OK", actual_path)


parser = argparse.ArgumentParser()
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
args.output.parent.mkdir(parents=True, exist_ok=True)
main(args.output)
