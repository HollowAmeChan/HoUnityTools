"""Prepare a disposable migration host with the legacy input and dedicated targets."""
import argparse
import json
from pathlib import Path
import shutil


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--shader-root", type=Path, required=True)
    parser.add_argument("--legacy-root", type=Path, required=True)
    parser.add_argument("--pipeline-root", type=Path, required=True)
    parser.add_argument("--urp-root", type=Path, required=True)
    parser.add_argument("--config-root", type=Path, required=True)
    parser.add_argument("--native-owners", action="store_true", help="Snapshot full tools and native importer tests, without installing legacy lilToon. Uses a tiny synthetic source only for transaction identity.")
    args = parser.parse_args()
    tool_root = Path(__file__).resolve().parents[2]
    output = args.output.resolve()
    if output.exists() and any(output.iterdir()):
        parser.error("Output must be new or empty; no existing project is rewritten.")
    if any(root.resolve() == output or root.resolve() in output.parents for root in [tool_root, args.shader_root, args.legacy_root, args.pipeline_root, args.urp_root, args.config_root]):
        parser.error("Use an output outside source repositories.")
    package = output / "MigrationValidationPackage"
    editor = package / "Editor/MaterialMigration"
    if args.native_owners:
        for directory in ["Editor", "Runtime"]:
            shutil.copytree(tool_root / directory, package / directory)
        shutil.copy2(tool_root / "package.json", package / "package.json")
    else:
        editor.mkdir(parents=True)
        for source in (tool_root / "Editor/MaterialMigration").iterdir():
            if source.is_file():
                shutil.copy2(source, editor / source.name)
        (package / "package.json").write_text(json.dumps({"name": "com.hollow.hounitytools", "version": "0.2.0", "displayName": "Ho Migration Validation Subset"}, indent=2), encoding="utf-8")
        (package / "Editor/HoMigrationValidation.Editor.asmdef").write_text(json.dumps({"name": "HoMigrationValidation.Editor", "references": ["Unity.RenderPipelines.Core.Runtime", "Unity.RenderPipelines.Universal.Runtime"], "includePlatforms": ["Editor"], "autoReferenced": True}), encoding="utf-8")
    project = output / "Project"
    (project / "Assets/Editor").mkdir(parents=True)
    owner_tests = {"HoMigrationReferenceOwnerChecks.cs", "HoMigrationOwnerTransactionChecks.cs"}
    for source in (tool_root / "Tests~/MaterialMigration").glob("*.cs"):
        if args.native_owners != (source.name in owner_tests):
            continue
        destination = package / "Editor/Validation" if source.name in ("HoConverterChecks.cs", "HoConverterSurfaceChecks.cs") else project / "Assets/Editor"
        destination.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination / source.name)
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    dependencies = {"com.hollow.hounitytools": "file:" + package.as_posix(),
                    "jp.lilxyzw.liltoon.urp.extensions": "file:" + args.pipeline_root.resolve().as_posix(),
                    "com.unity.render-pipelines.universal": "file:" + args.urp_root.resolve().as_posix(),
                    "com.unity.render-pipelines.universal-config": "file:" + args.config_root.resolve().as_posix()}
    if not args.native_owners:
        dependencies["jp.lilxyzw.liltoon"] = "file:" + args.legacy_root.resolve().as_posix()
    for folder in ["Cloth_URP", "Hair_URP", "Skin_URP", "Environment_URP"]:
        root = args.shader_root.resolve() / folder
        identity = json.loads((root / "package.json").read_text(encoding="utf-8-sig"))["name"]
        dependencies[identity] = "file:" + root.as_posix()
    for module in ["imgui", "animation", "uielements", "physics", "jsonserialize", "audio", "imageconversion", "particlesystem", "screencapture"]:
        dependencies["com.unity.modules." + module] = "1.0.0"
    (project / "Packages/manifest.json").write_text(json.dumps({"dependencies": dependencies}, indent=2), encoding="utf-8")
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 6000.3.15f1\n", encoding="utf-8")
    print(project)


if __name__ == "__main__":
    main()
