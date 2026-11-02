"""Independent mmd_tools foot oracle; run Blender in background with --python.

Blender ... --background --factory-startup --python this.py -- \
    --pmx White.pmx --vmd dance.vmd --output sour-foot.json
No source assets or .blend files are written.
"""
import argparse
import hashlib
import json
import pathlib
import struct
import sys
import tempfile

import bpy
from mathutils import Matrix, Vector


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--pmx", required=True, type=pathlib.Path)
    parser.add_argument("--vmd", required=True, type=pathlib.Path)
    parser.add_argument("--output", required=True, type=pathlib.Path)
    parser.add_argument("--canonical-interpolation", action="store_true",
                        help="Diagnostic only: mirror authoritative first-16 curves into mmd_tools' redundant blocks.")
    parser.add_argument("--disable-ik", action="store_true",
                        help="Diagnostic FK sample: mute imported IK constraints after VMD assignment.")
    parser.add_argument("--start-frame", type=float, default=225.0)
    parser.add_argument("--end-frame", type=float, default=315.0)
    parser.add_argument("--render-output", type=pathlib.Path)
    parser.add_argument("--render-frame", type=float, default=120.0)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1 :])
    if args.end_frame < args.start_frame:
        parser.error("--end-frame must be >= --start-frame")
    bpy.ops.preferences.addon_enable(module="mmd_tools")
    import mmd_tools

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.mmd_tools.import_model(
        directory=str(args.pmx.parent),
        files=[{"name": args.pmx.name}],
        types={"ARMATURE", "MESH"} if args.render_output else {"ARMATURE"},
        scale=0.08,
        rename_bones=False,
    )
    armature = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    mapping = {
        (b.mmd_bone.name_j or b.name): b for b in armature.pose.bones
        if not b.name.startswith("_")
    }
    names = [n for n in ["全ての親", "センター", "グルーブ", "腰", "下半身", "腰キャンセル左",
        "腰キャンセル右", "左足IK親", "右足IK親", "左足D", "左ひざD", "左足首D",
        "左足先EX", "右足D", "右ひざD", "右足首D", "右足先EX", *[
        side + part for side in ("左", "右")
        for part in ("足", "ひざ", "足首", "つま先", "足ＩＫ", "つま先ＩＫ")
    ]] if n in mapping]

    def unity(vector):
        # mmd_tools PMXImporter uses Vector(v).xzy; Ho uses (-x,y,-z).
        return [-vector.x, vector.z, -vector.y]

    conversion = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))

    def unity_rotation(matrix):
        rotation = conversion @ matrix.to_3x3() @ conversion.transposed()
        q = rotation.to_quaternion()
        return [q.x, q.y, q.z, q.w]

    def channels(bone):
        basis = bone.bone.matrix_local.to_quaternion()
        delta = basis @ bone.rotation_quaternion @ basis.inverted()
        return {
            "positionDelta": unity(basis @ bone.location),
            "rotationDelta": [delta.x, -delta.z, delta.y, delta.w],
        }

    rest = {n: {
        "position": unity(armature.matrix_world @ mapping[n].head),
        "rotation": unity_rotation(armature.matrix_world @ mapping[n].bone.matrix_local),
        "parent": mapping[n].parent.mmd_bone.name_j if mapping[n].parent else None,
    } for n in names}
    bpy.ops.object.select_all(action="DESELECT")
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    original = args.vmd.read_bytes()
    copied = bytearray(original)
    divergent = 0
    if args.canonical_interpolation:
        if not original.startswith(b"Vocaloid Motion Data 0002"):
            raise ValueError("Diagnostic normalizer supports VMD 0002 only.")
        count = struct.unpack_from("<I", original, 50)[0]
        for index in range(count):
            offset = 54 + index * 111 + 47
            block = original[offset:offset + 64]
            mismatch = False
            for channel in range(1, 4):
                for control in (0, 4, 8, 12):
                    dst = offset + channel * 16 + control
                    value = block[channel + control]
                    mismatch |= copied[dst] != value
                    copied[dst] = value
            divergent += int(mismatch)
            if copied[offset:offset + 16] != original[offset:offset + 16]:
                raise AssertionError("Authoritative curves changed")
    with tempfile.TemporaryDirectory(prefix="ho-vmd-oracle-") as temporary:
        motion = pathlib.Path(temporary) / args.vmd.name
        motion.write_bytes(copied)
        bpy.ops.mmd_tools.import_vmd(
            directory=str(motion.parent),
            files=[{"name": motion.name}],
            margin=0,
            bone_mapper="PMX",
            scale=0.08,
            use_pose_mode=False,
        )
    if args.disable_ik:
        for bone in armature.pose.bones:
            for constraint in bone.constraints:
                if constraint.type == "IK":
                    constraint.mute = True

    rows = []
    first_half_frame = round(args.start_frame * 2)
    last_half_frame = round(args.end_frame * 2)
    for half_frame in range(first_half_frame, last_half_frame + 1):
        frame = half_frame * 0.5
        # mmd_tools adds one Blender frame even when margin=0.
        bpy.context.scene.frame_set(int(frame) + 1, subframe=frame % 1)
        bpy.context.view_layer.update()
        evaluated = armature.evaluated_get(bpy.context.evaluated_depsgraph_get())
        row = {"vmdFrame": frame, "bones": {}, "rotations": {}, "channels": {}}
        for name in names:
            bone = evaluated.pose.bones[mapping[name].name]
            row["bones"][name] = unity(evaluated.matrix_world @ bone.head)
            row["rotations"][name] = unity_rotation(evaluated.matrix_world @ bone.matrix)
            row["channels"][name] = channels(mapping[name])
        rows.append(row)
    result = {
        "blender": bpy.app.version_string,
        "mmdTools": list(mmd_tools.bl_info["version"]),
        "pmx": str(args.pmx),
        "vmd": str(args.vmd),
        "originalVmdSha256": hashlib.sha256(original).hexdigest(),
        "oracleVmdSha256": hashlib.sha256(copied).hexdigest(),
        "canonicalInterpolationCopies": args.canonical_interpolation,
        "divergentBoneFrames": divergent,
        "sourceUnchanged": args.vmd.read_bytes() == original,
        "scale": 0.08,
        "vmdFrameOffset": 1,
        "physics": False,
        "ikConstraintsMuted": args.disable_ik,
        "rest": rest,
        "frames": rows,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print("HO_SOUR_FOOT_AUDIT", str(args.output))
    if args.render_output:
        scene = bpy.context.scene
        scene.frame_set(int(args.render_frame) + 1, subframe=args.render_frame % 1)
        bpy.context.view_layer.update()
        scene.render.engine = "BLENDER_EEVEE_NEXT"
        scene.render.resolution_x = 720
        scene.render.resolution_y = 1280
        scene.render.resolution_percentage = 100
        scene.render.image_settings.file_format = "PNG"
        scene.render.film_transparent = False
        for collection in scene.collection.children:
            collection.hide_render = False
        for obj in scene.objects:
            obj.hide_render = False
        meshes = [obj for obj in scene.objects if obj.type == "MESH"]
        for obj in meshes:
            print("HO_SOUR_RENDER_MESH", obj.name, "hide=", obj.hide_render,
                  "loc=", tuple(obj.matrix_world.translation), "dims=", tuple(obj.dimensions))
        bpy.context.view_layer.update()
        corners = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
        if not corners:
            raise RuntimeError("PMX import produced no renderable mesh objects.")
        low = Vector(tuple(min(point[i] for point in corners) for i in range(3)))
        high = Vector(tuple(max(point[i] for point in corners) for i in range(3)))
        target = (low + high) * 0.5
        height = max(high.z - low.z, 0.1)
        print("HO_SOUR_RENDER_BOUNDS", tuple(low), tuple(high), "target=", tuple(target), "height=", height)
        world = scene.world or bpy.data.worlds.new("Sour foot audit")
        scene.world = world
        world.use_nodes = True
        background = world.node_tree.nodes.get("Background")
        background.inputs["Color"].default_value = (0.14, 0.16, 0.20, 1)
        background.inputs["Strength"].default_value = 0.7
        camera_data = bpy.data.cameras.new("Sour audit camera")
        camera = bpy.data.objects.new("Sour audit camera", camera_data)
        scene.collection.objects.link(camera)
        camera_data.type = "ORTHO"
        camera_data.ortho_scale = height * 1.35
        camera_data.clip_end = 1000
        scene.camera = camera
        light_data = bpy.data.lights.new("Sour audit key", "AREA")
        light = bpy.data.objects.new("Sour audit key", light_data)
        scene.collection.objects.link(light)
        light_data.energy = 500
        light_data.shape = "DISK"
        light_data.size = 3
        args.render_output.parent.mkdir(parents=True, exist_ok=True)
        for side, suffix in ((1, "plusY"), (-1, "minusY")):
            camera.location = target + Vector((0, side * max(height * 2.5, 3), 0.1))
            camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
            light.location = target + Vector((1, side * max(height * 1.7, 2), 2))
            light.rotation_euler = (target - light.location).to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = str(args.render_output.with_name(
                args.render_output.stem + "-" + suffix + args.render_output.suffix))
            bpy.ops.render.render(write_still=True)
            print("HO_SOUR_FOOT_RENDER", scene.render.filepath)


if __name__ == "__main__":
    main()
