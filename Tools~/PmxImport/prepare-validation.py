"""Prepare the isolated Ho PMX importer validation project from local source assets.

This copies only the seven audited models and their nearby image/effect dependencies.
The user's existing Unity project and source models are never modified.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import shutil


def sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def copy_file(source: pathlib.Path, destination: pathlib.Path, entries: list) -> None:
    before = source.stat()
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)
    source_hash = sha256(source)
    destination_hash = sha256(destination)
    after = source.stat()
    if source_hash != destination_hash:
        raise RuntimeError(f"Copy hash mismatch: {source}")
    if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise RuntimeError(f"Source changed while being copied: {source}")
    entries.append({"source": str(source), "destination": str(destination),
                    "bytes": before.st_size, "sha256": source_hash,
                    "source_stat_unchanged": True})


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=pathlib.Path,
                        default=pathlib.Path(r"D:\Unity_Fork\.codex_temp\HoPmxImportValidation"))
    parser.add_argument("--audit", type=pathlib.Path,
                        default=pathlib.Path(r"D:\Unity_Fork\lilToon-URP-Extensions\research~\PMXSceneAudit-20261028\pmx-source-audit.json"))
    parser.add_argument("--repository", type=pathlib.Path,
                        default=pathlib.Path(__file__).resolve().parents[2])
    parser.add_argument("--sync-code-only", action="store_true")
    args = parser.parse_args()
    project = args.project.resolve()
    repository = args.repository.resolve()
    if project == repository or project in repository.parents:
        raise RuntimeError("Validation project must be outside the source repository")
    (project / "Assets" / "Editor").mkdir(parents=True, exist_ok=True)
    entries = []
    implementation = repository / "Editor" / "PmxImport"
    if implementation.exists():
        for source in implementation.rglob("*"):
            if source.is_file():
                copy_file(source, project / "Assets" / "Editor" / "PmxImport" /
                          source.relative_to(implementation), entries)
    validation = repository / "Tests~" / "PmxImport"
    if validation.exists():
        for source in validation.rglob("*"):
            if source.is_file() and source.suffix in (".cs", ".asmdef", ".meta"):
                copy_file(source, project / "Assets" / "Editor" / "PmxValidation" /
                          source.relative_to(validation), entries)
    (project / "Results").mkdir(exist_ok=True)
    (project / "Results" / "code-copy.json").write_text(
        json.dumps(entries, ensure_ascii=False, indent=2), encoding="utf-8")
    if args.sync_code_only:
        print(json.dumps({"project": str(project), "codeFiles": len(entries)}))
        return
    dependencies = {
        "com.unity.render-pipelines.universal": "file:D:/Unity_Fork/HoUrp17.3.0",
        "com.unity.render-pipelines.universal-config": "file:D:/Unity_Fork/HoUrpConfig17.0.3",
        "jp.lilxyzw.liltoon": "file:D:/Unity_Fork/lilToon/Assets/lilToon",
        "jp.lilxyzw.liltoon.urp.extensions": "file:D:/Unity_Fork/lilToon-URP-Extensions",
    }
    for module in ("animation", "audio", "imgui", "imageconversion", "jsonserialize",
                   "particlesystem", "physics", "terrain", "ui", "uielements", "unitywebrequest"):
        dependencies[f"com.unity.modules.{module}"] = "1.0.0"
    (project / "Packages").mkdir(exist_ok=True)
    (project / "Packages" / "manifest.json").write_text(
        json.dumps({"dependencies": dependencies}, indent=2), encoding="utf-8")
    (project / "ProjectSettings").mkdir(exist_ok=True)
    (project / "ProjectSettings" / "ProjectVersion.txt").write_text(
        "m_EditorVersion: 6000.3.15f1\nm_EditorVersionWithRevision: 6000.3.15f1 (c1aa84e375f6)\n",
        encoding="utf-8")
    audited = json.loads(args.audit.read_text(encoding="utf-8-sig"))
    sample_roots = {}
    for item in audited:
        source = pathlib.Path(item["summary"]["path"])
        label = item["summary"]["label"]
        if label.startswith("朱木"):
            label = "朱木古堂"
        sample_roots[source.parent] = label
    entries = []
    allowed = {".pmx", ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".dds", ".exr",
               ".fx", ".fxsub", ".emd", ".txt"}
    for source_root, label in sample_roots.items():
        for source in source_root.rglob("*"):
            if not source.is_file() or source.suffix.lower() not in allowed:
                continue
            # The stage12 fallback couch is not part of the seven audited models.
            if source.suffix.lower() == ".pmx" and not any(
                    str(source) == item["summary"]["path"] for item in audited):
                continue
            copy_file(source, project / "Assets" / "Samples" / label /
                      source.relative_to(source_root), entries)
    (project / "Results" / "source-copy.json").write_text(
        json.dumps(entries, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"project": str(project), "sourceFiles": len(entries),
                      "sourceBytes": sum(entry["bytes"] for entry in entries),
                      "pmxFiles": sum(pathlib.Path(entry["source"]).suffix.lower() == ".pmx"
                                      for entry in entries)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
