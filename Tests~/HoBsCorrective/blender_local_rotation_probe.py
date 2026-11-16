"""Headless Blender measurement of corrective driver rotation spaces.

Run: blender --background --factory-startup --python this_file.py -- --output DIR
Creates a standalone demonstrator and JSON; never reads a user's scene.
"""
import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Euler, Matrix, Quaternion


def arguments():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    return parser.parse_args(sys.argv[sys.argv.index("--") + 1:])


def close(actual, expected, tolerance=3e-5):
    assert abs(actual - expected) < tolerance, (actual, expected)


def create_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    data = bpy.data.armatures.new("BS_LocalRotation_RigData")
    arm = bpy.data.objects.new("BS_LocalRotation_Rig", data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    parent = data.edit_bones.new("Parent")
    parent.head = (0, 0, 0)
    parent.tail = (0.7, 0.8, 1.2)
    parent.roll = 0.37
    child = data.edit_bones.new("Main")
    child.head = parent.tail
    child.tail = (1.6, 1.4, 1.1)
    child.roll = -0.61
    child.parent = parent
    bpy.ops.object.mode_set(mode="OBJECT")
    for p in arm.pose.bones:
        p.rotation_mode = "XYZ"
    arm.rotation_mode = "XYZ"
    arm.location = (2.0, -3.0, 1.0)
    arm.rotation_euler = (0.24, -0.38, 0.15)
    arm.scale = (1.3, 0.8, 1.1)

    mesh_data = bpy.data.meshes.new("BS_LocalRotation_MeshData")
    mesh_data.from_pydata([(0, 0, 0), (1, 0, 0), (0, 0, 1)], [], [(0, 1, 2)])
    mesh = bpy.data.objects.new("BS_LocalRotation_Mesh", mesh_data)
    bpy.context.scene.collection.objects.link(mesh)
    mesh.shape_key_add(name="Basis")
    probes = []
    for space in ("TRANSFORM_SPACE", "LOCAL_SPACE", "WORLD_SPACE"):
        for rotation in ("XYZ", "SWING_TWIST_X", "SWING_TWIST_Y", "SWING_TWIST_Z"):
            for axis in "XYZ":
                key = mesh.shape_key_add(name=f"{space}_{rotation}_{axis}")
                key.slider_min = -10
                key.slider_max = 10
                fc = key.driver_add("value")
                driver = fc.driver
                driver.type = "SCRIPTED"
                var = driver.variables.new()
                var.name = "angle"
                var.type = "TRANSFORMS"
                target = var.targets[0]
                target.id = arm
                target.bone_target = "Main"
                target.transform_type = "ROT_" + axis
                target.transform_space = space
                target.rotation_mode = rotation
                driver.expression = "angle"
                probes.append((key.name, key))
    corrective = mesh.shape_key_add(name="Corrective_X_90deg")
    corrective.data[2].co.x += 0.6
    fc = corrective.driver_add("value")
    var = fc.driver.variables.new()
    var.name = "rx"
    var.type = "TRANSFORMS"
    target = var.targets[0]
    target.id = arm
    target.bone_target = "Main"
    target.transform_type = "ROT_X"
    target.transform_space = "LOCAL_SPACE"
    target.rotation_mode = "XYZ"
    fc.driver.expression = "max(0,min(1,rx/1.5707963267948966))"
    return arm, mesh, probes


def refresh():
    bpy.context.view_layer.update()
    bpy.context.scene.frame_set(bpy.context.scene.frame_current + 1)
    bpy.context.view_layer.update()


def measure(arm, mesh, probes, name):
    refresh()
    evaluated = arm.evaluated_get(bpy.context.evaluated_depsgraph_get())
    p = evaluated.pose.bones["Main"]
    rest = p.bone.matrix_local
    relative = p.parent.matrix.inverted_safe() @ p.matrix
    basis_ordinary = rest.inverted_safe() @ p.parent.bone.matrix_local @ relative
    basis_native = p.bone.convert_local_to_pose(
        p.matrix, p.bone.matrix_local,
        parent_matrix=p.parent.matrix,
        parent_matrix_local=p.parent.bone.matrix_local,
        invert=True,
    )
    return {
        "name": name,
        "values": {name: float(key.value) for name, key in probes},
        "corrective": float(mesh.data.shape_keys.key_blocks["Corrective_X_90deg"].value),
        "channels_xyz": list(arm.pose.bones["Main"].rotation_euler),
        "basis_xyz": list(p.matrix_basis.to_euler("XYZ")),
        "evaluated_local_xyz": list(basis_native.to_euler("XYZ")),
        "ordinary_local_xyz": list(basis_ordinary.to_euler("XYZ")),
        "parent_relative_xyz": list(relative.to_euler("XYZ")),
        "main_local_rotation": list(basis_native.to_quaternion()),
    }


def run():
    args = arguments()
    args.output.mkdir(parents=True, exist_ok=True)
    arm, mesh, probes = create_scene()
    parent, main = arm.pose.bones["Parent"], arm.pose.bones["Main"]
    rows = [measure(arm, mesh, probes, "rest")]
    parent.rotation_euler = (0.7, -0.4, 0.9)
    rows.append(measure(arm, mesh, probes, "parent_rotation_only"))
    parent.scale = (1.5, 0.7, 1.2)
    rows.append(measure(arm, mesh, probes, "parent_rotation_nonuniform_scale"))
    main.rotation_euler = (math.pi / 4, 0, 0)
    rows.append(measure(arm, mesh, probes, "main_x_45_parent_scaled"))
    parent.rotation_euler = (-0.5, 0.8, -1.2)
    parent.scale = (0.6, 1.8, 1.1)
    arm.rotation_euler = (-1.1, 0.4, 0.8)
    rows.append(measure(arm, mesh, probes, "main_x_45_other_parent_object"))
    main.rotation_mode = "QUATERNION"
    main.rotation_quaternion = Quaternion((0, 1, 0), 0.6) @ Quaternion((1, 0, 0), 0.9)
    rows.append(measure(arm, mesh, probes, "main_compound_quaternion"))
    main.rotation_mode = "XYZ"
    main.rotation_euler = (0.0, 0, 0)
    parent.rotation_euler = (0, 0, 0)
    parent.scale = (1, 1, 1)
    limit = main.constraints.new("LIMIT_ROTATION")
    limit.owner_space = "LOCAL"
    limit.use_limit_x = True
    limit.min_x = limit.max_x = math.pi / 3
    rows.append(measure(arm, mesh, probes, "local_constraint_x_60_channels_zero"))
    parent.rotation_euler = (0.4, 0.9, -0.7)
    parent.scale = (1.2, 0.8, 1.6)
    rows.append(measure(arm, mesh, probes, "local_constraint_x_60_parent_moved"))
    limit.mute = True
    main.rotation_euler = (0.0, 0, math.radians(179))
    rows.append(measure(arm, mesh, probes, "main_z_179"))
    main.rotation_euler.z = math.radians(181)
    rows.append(measure(arm, mesh, probes, "main_z_181"))

    # Contract: LOCAL_SPACE cancels parent/rest/object and includes local constraints.
    for row in rows[:3]:
        for axis in "XYZ":
            close(row["values"][f"LOCAL_SPACE_XYZ_{axis}"], 0)
    for row in rows[3:5]:
        close(row["values"]["LOCAL_SPACE_XYZ_X"], math.pi / 4)
        close(row["corrective"], 0.5)
    for row in rows[6:8]:
        close(row["values"]["LOCAL_SPACE_XYZ_X"], math.pi / 3)
        close(row["values"]["TRANSFORM_SPACE_XYZ_X"], 0)
        close(row["corrective"], 2 / 3)
    for row in rows:
        for index, axis in enumerate("XYZ"):
            close(row["values"][f"LOCAL_SPACE_XYZ_{axis}"], row["evaluated_local_xyz"][index])

    result = {"blender_version": bpy.app.version_string, "rows": rows}
    (args.output / "local_rotation_results.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    # Demonstrator opens at 45 degrees with arbitrary transformed parent.
    main.rotation_euler = (math.pi / 4, 0, 0)
    refresh()
    mesh.active_shape_key_index = len(mesh.data.shape_keys.key_blocks) - 1
    bpy.ops.wm.save_as_mainfile(filepath=str(args.output / "bs_local_rotation_demo.blend"))
    print("BS_LOCAL_ROTATION_PROBE_OK", bpy.app.version_string)
    for row in rows:
        print(row["name"], "localXYZ=", [round(row["values"][f"LOCAL_SPACE_XYZ_{a}"], 6) for a in "XYZ"], "corrective=", round(row["corrective"], 6))


if __name__ == "__main__":
    run()
