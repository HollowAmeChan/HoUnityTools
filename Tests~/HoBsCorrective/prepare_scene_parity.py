"""Copy the user's saved scene and exact import settings into a disposable Unity project."""
import argparse
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path(r"D:/Unity_Project/BREAK_URP")
FOLDER = Path("Assets/00动画/角色/羽啾")
PROJECT = ROOT / "Temp~/YuqiuSceneParityUnity"
SAMPLES = ROOT / "Temp~/YuqiuSceneParity20261120"


def copy(source, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)


def main():
    global SOURCE, PROJECT, SAMPLES
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=SOURCE)
    parser.add_argument("--project", type=Path, default=PROJECT)
    parser.add_argument("--samples", type=Path, default=SAMPLES)
    args = parser.parse_args()
    SOURCE, PROJECT, SAMPLES = args.source.resolve(), args.project.resolve(), args.samples.resolve()
    for name in ("羽啾.fbx", "羽啾_BUILD.prefab", "羽啾.unity"):
        for suffix in ("", ".meta"):
            copy(SOURCE / FOLDER / (name + suffix), PROJECT / FOLDER / (name + suffix))
    for source in (SOURCE / FOLDER / "HoFBX").glob("*.json*"):
        copy(source, PROJECT / FOLDER / "HoFBX" / source.name)
    clip = Path("Assets/00动画/VRM动画/官方包/VRMA_01.anim")
    for suffix in ("", ".meta"):
        copy(SOURCE / (str(clip) + suffix), PROJECT / (str(clip) + suffix))
    runtime = [
        "Runtime/Constraints/HoBsCorrective.cs", "Runtime/Constraints/HoBsCorrective.UpdateLoop.cs",
        "Runtime/FaceTracking/HoFaceOutputOwnership.cs", "Runtime/AnimationTools/HoAnimationPreviewer.cs",
        "Runtime/HoEditorOnly.cs", "Runtime/CombineArmature/IHoArmatureMergeParticipant.cs",
        "Tests~/HoBsCorrective/HoBsSceneParityPlayer.cs",
    ]
    runtime += [str(p.relative_to(ROOT)) for p in (ROOT / "Runtime/RigConstraints/HoAuxRig").glob("*.cs")]
    runtime += [str(p.relative_to(ROOT)) for p in (ROOT / "Runtime/BoneRenderer").glob("*.cs")]
    for name in runtime:
        source = ROOT / name
        copy(source, PROJECT / "Assets/Runtime" / source.name)
        if source.with_suffix(source.suffix + ".meta").exists():
            copy(source.with_suffix(source.suffix + ".meta"), PROJECT / "Assets/Runtime" / (source.name + ".meta"))
    editor = ["Editor/HoFbxImport/HoFbxBsCorrectiveImportUtility.cs",
              "Editor/HoFbxImport/HoFbxHumanoidImportUtility.cs",
              "Tests~/HoBsCorrective/HoHumanoidTPoseChecks.cs",
              "Tests~/HoBsCorrective/HoBsSceneParityChecks.cs"]
    for name in editor:
        source = ROOT / name
        copy(source, PROJECT / "Assets/Editor" / source.name)
    (PROJECT / "Assets/Runtime/HoUnityTools.Runtime.asmdef").write_text(json.dumps({
        "name": "HoUnityTools.Runtime", "autoReferenced": True,
    }), encoding="utf-8")
    sample = json.loads((SAMPLES / "blender_samples.json").read_text(encoding="utf-8"))
    for frame in sample["frames"]:
        frame["rotations"] = [dict(zip("xyzw", q)) for q in frame["rotations"]]
    (PROJECT / "Assets/BlenderSamples.json").write_text(json.dumps(sample, ensure_ascii=False), encoding="utf-8")
    copy(SAMPLES / "authoring.json", PROJECT / "Assets/Authoring.json")
    settings = PROJECT / "ProjectSettings"
    packages = PROJECT / "Packages"
    settings.mkdir(exist_ok=True)
    packages.mkdir(exist_ok=True)
    (settings / "ProjectVersion.txt").write_text("m_EditorVersion: 6000.3.15f1\n")
    (packages / "manifest.json").write_text(json.dumps({"dependencies": {
        "com.unity.modules.animation": "1.0.0", "com.unity.modules.jsonserialize": "1.0.0",
        "com.unity.modules.imageconversion": "1.0.0", "com.unity.modules.imgui": "1.0.0",
    }}))
    print(PROJECT)


if __name__ == "__main__":
    main()
