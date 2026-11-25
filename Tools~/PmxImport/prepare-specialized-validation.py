"""Prepare a disposable stock-URP host for synthetic no-lil PMX regeneration tests."""
import argparse
import json
from pathlib import Path
import shutil

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--project", type=Path, required=True)
parser.add_argument("--hoshader", type=Path, default=Path("D:/Unity_Fork/HoShader"))
args = parser.parse_args()
repo = Path(__file__).resolve().parents[2]
host = args.project.resolve()
if host == repo or host in repo.parents or repo in host.parents:
    raise ValueError("Use a separate disposable project")
if (host / "Assets/fresh.pmx").exists():
    raise ValueError("This test creates fixed fixture paths; choose a fresh project")
editor = host / "Assets/Editor/PmxImport"
editor.mkdir(parents=True, exist_ok=True)
for source in (repo / "Editor/PmxImport").rglob("*"):
    if source.is_file() and ".asmdef" not in source.name:
        destination = editor / source.relative_to(repo / "Editor/PmxImport")
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
for source in [repo / "Runtime/MmdMotion/HoPmxPoseRig.cs", repo / "Runtime/RigConstraints/HoIK/HoIkConstraint.cs"]:
    shutil.copy2(source, host / "Assets" / source.name)
shutil.copy2(repo / "Tests~/PmxImport/HoPmxSpecializedValidation.cs", editor.parent / "HoPmxSpecializedValidation.cs")
packages = host / "Packages"
packages.mkdir(exist_ok=True)
deps = {"com.unity.render-pipelines.universal": "17.3.0"}
for module in ("animation", "imgui", "imageconversion", "jsonserialize", "physics", "uielements"):
    deps["com.unity.modules." + module] = "1.0.0"
for directory in ("Cloth_URP", "Hair_URP", "Skin_URP", "Environment_URP"):
    package = args.hoshader / directory
    deps[json.loads((package / "package.json").read_text(encoding="utf-8-sig"))["name"]] = "file:" + package.as_posix()
(packages / "manifest.json").write_text(json.dumps({"dependencies": deps}, indent=2), encoding="utf-8")
settings = host / "ProjectSettings"
settings.mkdir(exist_ok=True)
(settings / "ProjectVersion.txt").write_text("m_EditorVersion: 6000.3.15f1\n", encoding="utf-8")
(settings / "HoPmxSpecializedValidationHost").write_text("Synthetic fixtures only.\n", encoding="utf-8")
print(host)
