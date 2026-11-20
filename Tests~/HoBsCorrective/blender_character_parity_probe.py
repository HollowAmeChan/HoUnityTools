"""Sample an existing corrective rig with its Humanoid Debug clip; never save it.

Run with the normal Blender preferences (not --factory-startup), supplying the
blend on the Blender command line and --output after --python's arguments.
Generated character data belongs in ignored Temp~/, not in this test directory.
"""
import argparse
import json
import sys
import time
from pathlib import Path

import bpy


def xyzw(q):
    return [float(q.x), float(q.y), float(q.z), float(q.w)]


def avatar_reference_bases(armature, avatar, rig):
    """Reconstruct the whole reference pose, including parent/child calibration.

    Fit the world-axis conversion from shared rest landmarks rather than assuming
    the zeroed FBX main bones retain Blender's original local axes.
    """
    import numpy as np
    from mathutils import Matrix, Quaternion, Vector

    skeleton = {item["name"]: item for item in avatar["skeleton"]}
    names = [bone.name for bone in armature.data.bones if bone.name in skeleton]
    rest = {name: armature.matrix_world @ armature.data.bones[name].matrix_local for name in names}
    source = np.array([list(rest[name].translation) + [1.0] for name in names])
    target = np.array([[skeleton[name]["restPosition"][axis] for axis in "xyz"] for name in names])
    affine, _, _, _ = np.linalg.lstsq(source, target, rcond=None)
    u, _, v = np.linalg.svd(affine[:3].T)
    conversion = Matrix(u @ v)
    inverse = conversion.inverted()
    translation = Vector(affine[3])
    poses = {}
    for name in names:
        item = skeleton[name]
        q = lambda key: Quaternion(tuple(item[key][axis] for axis in "wxyz"))
        delta = inverse @ (q("rotation") @ q("restRotation").inverted()).to_matrix() @ conversion
        position = inverse @ (Vector(tuple(item["position"][axis] for axis in "xyz")) - translation)
        _, rotation, scale = rest[name].decompose()
        poses[name] = armature.matrix_world.inverted() @ Matrix.LocRotScale(
            position, delta.to_quaternion() @ rotation, scale)
    return {name: rig._basis_matrix(armature.pose.bones[name], pose,
                poses.get(armature.pose.bones[name].parent.name) if armature.pose.bones[name].parent else None)
            for name, pose in poses.items()}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--step", type=int, default=1)
    parser.add_argument("--export-only", action="store_true")
    parser.add_argument("--no-mch", action="store_true", help="Export direct bones without MCH preprocessing")
    parser.add_argument("--clip", type=Path, help="Use the exact clip from the Unity test scene")
    parser.add_argument("--samples-only", action="store_true")
    parser.add_argument("--reference-pose", type=Path,
                        help="Temporarily use Unity avatar_tpose.json for the measured bones; never save")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    folder = args.output.resolve()
    folder.mkdir(parents=True, exist_ok=True)
    from HoTools.BoneTools import HumanoidDebug as debug
    from HoTools.Rbf import bs_corrective as bs

    if args.export_only:
        export_character(folder, args.no_mch)
        return

    assert bpy.app.driver_namespace.get("hotools_bs_rbf"), "HoTools namespace not restored"
    scene = bpy.context.scene
    armatures = [o for o in scene.objects if o.type == "ARMATURE" and bs.settings_for(o)
                 and len(bs.settings_for(o).rules)]
    assert len(armatures) == 1, [(o.name, len(bs.settings_for(o).rules)) for o in armatures]
    armature = armatures[0]
    info = (debug.anim_parser._read_file_header(str(args.clip), args.clip.stat().st_mtime_ns,
                                               args.clip.stat().st_size)
            if args.clip else debug._selected_clip_info(scene))
    assert info is not None, "Humanoid Debug has no selected clip"
    clip = debug.anim_parser.load_clip(info)
    original = debug._capture_state(scene, armature)
    config = bs.build_export_config(armature)
    (folder / "authoring.json").write_text(json.dumps(config, ensure_ascii=False, indent=2), encoding="utf-8")
    names = [rule.bone for rule in bs.settings_for(armature).rules]
    outputs = []
    for rule in bs.settings_for(armature).rules:
        for point in bs._authored_points(rule):
            for influence in rule.influence_objects:
                obj = influence.mesh
                key = obj.data.shape_keys.key_blocks.get(point.bs_name) if obj and obj.data.shape_keys else None
                assert key is not None, (rule.bone, obj.name if obj else None, point.bs_name)
                outputs.append((obj, key, rule.id, point.id))
    report = {
        "blend": bpy.data.filepath, "blender": bpy.app.version_string,
        "armature": armature.name, "objects": len(scene.objects),
        "clip": str(info.path), "clipId": info.identifier, "fps": scene.ho_humanoid_debug_fps,
        "duration": info.duration, "bones": names,
        "savedBasis": [{"bone": name, "rotation": xyzw(armature.pose.bones[name].matrix_basis.to_quaternion())}
                       for name in names],
        "outputs": [{"mesh": obj.name, "shape": key.name, "ruleId": rid, "pointId": pid,
                     "mute": bool(key.mute)} for obj, key, rid, pid in outputs],
        "frames": [],
    }
    reference_basis = debug.rig.cached_reference_basis
    if args.reference_pose:
        from mathutils import Matrix, Quaternion
        avatar = json.loads(args.reference_pose.read_text(encoding="utf-8"))
        overrides = {bone["name"]: bone["rotation"] for bone in avatar["bones"]}
        full_bases = avatar_reference_bases(armature, avatar, debug.rig) if avatar.get("skeleton") else {}

        def calibrated_basis(bone):
            if bone.name in full_bases:
                return full_bases[bone.name].copy()
            basis = reference_basis(bone)
            if bone.name not in overrides:
                return basis
            location, _, scale = basis.decompose()
            q = overrides[bone.name]
            # HoFBX Y_X maps Blender xyzw to Unity (x,-y,-z,w).
            rotation = Quaternion((q["w"], q["x"], -q["y"], -q["z"]))
            return Matrix.LocRotScale(location, rotation, scale)

        debug.rig.cached_reference_basis = calibrated_basis
        report["referencePoseOverride"] = str(args.reference_pose.resolve())
    started = time.monotonic()
    try:
        debug._suspend_pose_inputs(original)
        reference = debug.rig.build_reference_state(armature, clip)
        fps = scene.ho_humanoid_debug_fps
        total = info.frame_count(fps)
        for frame in range(0, total, max(1, args.step)):
            sample_time = min(frame / fps, info.duration)
            debug.rig.apply_sample(armature, reference, clip.sample(sample_time), scene.ho_humanoid_debug_muscle_scale)
            evaluated = armature.evaluated_get(bpy.context.evaluated_depsgraph_get())
            report["frames"].append({
                "frame": frame, "time": sample_time,
                "rotations": [xyzw(evaluated.pose.bones[name].matrix_basis.to_quaternion()) for name in names],
                "weights": [float(key.value) if not key.mute else 0.0 for _, key, _, _ in outputs],
            })
            if frame % 120 == 0:
                print("CHARACTER_SAMPLE", frame, total, flush=True)
    finally:
        debug.rig.cached_reference_basis = reference_basis
        debug._restore_state(original)
    report["sampleSeconds"] = time.monotonic() - started
    report["drivers"] = [
        {"mesh": obj.name, "path": fc.data_path, "expression": fc.driver.expression,
         "valid": bool(fc.driver.is_valid), "mute": bool(fc.mute)}
        for obj in {item[0] for item in outputs}
        for fc in obj.data.shape_keys.animation_data.drivers
    ]
    (folder / "blender_samples.json").write_text(json.dumps(report, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    if not args.samples_only:
        export_character(folder, args.no_mch)
    print("CHARACTER_HOFBX_PARITY_EXPORTED", len(report["frames"]), len(outputs), folder, flush=True)


def export_character(folder, no_mch=False):
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="SELECT")
    # Only filepath is supplied: exercise the operator's actual defaults.
    options = {"generateMCHBones": False} if no_mch else {}
    result = bpy.ops.ho.final_fbx_export(filepath=str(folder / "Yuqiu.fbx"), **options)
    assert result == {"FINISHED"}, result
    assert (folder / "Yuqiu.fbx").is_file()
    manifest = folder / "HoFBX" / "Yuqiu_unity.json"
    assert manifest.is_file(), "Missing HoFBX manifest"
    print("CHARACTER_HOFBX_EXPORT_OK", folder, flush=True)


if __name__ == "__main__":
    main()
