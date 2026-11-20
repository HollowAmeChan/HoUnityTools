"""Replay Unity's measured local rotations through the original Blender drivers.

This is a numerical test. It never saves the blend or changes its calibration.
"""
import argparse
import json
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", type=Path, required=True)
    parser.add_argument("--samples", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    read = lambda path: json.loads(path.read_text(encoding="utf-8"))
    unity, samples = read(args.unity), read(args.samples)
    assert len(unity["frames"]) == len(samples["frames"]), "Incomplete Unity result"

    from HoTools.BoneTools import HumanoidDebug as debug
    armature = bpy.data.objects[samples["armature"]]
    outputs = [bpy.data.objects[row["mesh"]].data.shape_keys.key_blocks[row["shape"]]
               for row in samples["outputs"]]
    original = debug._capture_state(bpy.context.scene, armature)
    report = {"frameCount": len(unity["frames"]), "outputCount": len(outputs),
              "maxWeightError": 0.0, "meanWeightError": 0.0, "worst": None}
    total = 0.0
    try:
        debug._suspend_pose_inputs(original)
        assert bpy.app.driver_namespace.get("hotools_bs_rbf"), "Missing driver namespace"
        for frame in unity["frames"]:
            for name, q in zip(samples["bones"], frame["rotations"]):
                rotation = Quaternion(tuple(q[axis] for axis in "wxyz"))
                armature.pose.bones[name].matrix_basis = Matrix.LocRotScale(
                    Vector(), rotation, Vector((1.0, 1.0, 1.0)))
            armature.update_tag(refresh={"OBJECT"})
            bpy.context.view_layer.update()
            for index, (key, expected) in enumerate(zip(outputs, frame["weights"])):
                actual = float(key.value) if not key.mute else 0.0
                error = abs(actual - expected)
                total += error
                if error > report["maxWeightError"]:
                    report["maxWeightError"] = error
                    report["worst"] = {**samples["outputs"][index], "time": frame["time"],
                                       "blender": actual, "unity": expected}
    finally:
        debug._restore_state(original)
    report["meanWeightError"] = total / (len(outputs) * len(unity["frames"]))
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    assert report["maxWeightError"] < 1e-5, report
    print("BLENDER_UNITY_INPUT_REPLAY_OK", report["maxWeightError"], flush=True)


if __name__ == "__main__":
    main()
