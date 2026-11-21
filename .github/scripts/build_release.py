#!/usr/bin/env python3
"""Build date-stamped Unity installers without changing upstream package versions."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import gzip
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import tarfile


EXCLUDED_DIRECTORIES = {
    ".git", ".github", ".agents", ".vscode", ".idea", "__pycache__",
    "node_modules", "Tests", "Tests~", "Tools", "Tools~", "Validation",
    "ReferencePackages~", "ResearchDocs~", "research~", "Temp~", "_Release", "_dist",
}
EXCLUDED_SUFFIXES = {".pyc", ".pyo", ".tmp", ".log", ".unitypackage", ".tgz", ".exp", ".lib"}
LFS_POINTER = b"version https://git-lfs.github.com/spec/v1"


def json_bytes(value: dict) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def git(root: Path, *args: str) -> str:
    return subprocess.check_output(["git", "-C", str(root), *args]).decode("utf-8").strip()


def parse_version(value: str) -> datetime:
    if not re.fullmatch(r"\d{8}-\d{6}", value):
        raise ValueError("Version must be a UTC date: YYYYMMDD-HHMMSS")
    return datetime.strptime(value, "%Y%m%d-%H%M%S").replace(tzinfo=timezone.utc)


def is_excluded(path: PurePosixPath) -> bool:
    # Apply the same rule to a directory and its adjacent Unity .meta file.
    parts = list(path.parts)
    if parts[-1].endswith(".meta"):
        parts[-1] = parts[-1][:-5]
    return (
        parts[0] in EXCLUDED_DIRECTORIES
        or any(p in {"__pycache__", "node_modules"} or (p.startswith(".") and p != ".icon.png") for p in parts)
        or PurePosixPath(parts[-1]).suffix.lower() in EXCLUDED_SUFFIXES
        or parts[-1] in {"AGENTS.md", "RELEASING.md"}
    )


def read_file(root: Path, relative: PurePosixPath) -> bytes:
    if relative.is_absolute() or ".." in relative.parts or "\\" in str(relative):
        raise ValueError(f"Unsafe input path: {relative}")
    path = root.joinpath(*relative.parts)
    if not path.resolve().is_relative_to(root.resolve()) or path.is_symlink():
        raise ValueError(f"Input is a symlink or escapes the repository: {relative}")
    data = path.read_bytes()
    if data.startswith(LFS_POINTER):
        raise ValueError(f"Unresolved Git LFS pointer: {relative}; run git lfs pull")
    return data


def collect_package(root: Path, source: str, tracked: list[str], worktree: bool = False) -> dict[str, bytes]:
    source_path = PurePosixPath(source)
    if source_path.is_absolute() or ".." in source_path.parts or "\\" in source:
        raise ValueError(f"Unsafe package path: {source}")
    files = {}
    for name in tracked:
        path = PurePosixPath(name)
        if not path.is_relative_to(source_path):
            continue
        relative = path.relative_to(source_path)
        if not is_excluded(relative):
            if worktree and not root.joinpath(*path.parts).exists():
                continue  # Locally deleted files must not return in an internal build.
            files[str(relative)] = read_file(root, path)
    folders = {str(parent) for name in files if not name.endswith(".meta")
               for parent in PurePosixPath(name).parents}
    files = {name: data for name, data in files.items()
             if not name.endswith(".meta") or name[:-5] in files or name[:-5] in folders}
    if "package.json" not in files:
        raise ValueError(f"No tracked package.json in {source}")
    return files


def add_tar_bytes(archive: tarfile.TarFile, name: str, data: bytes, epoch: int) -> None:
    info = tarfile.TarInfo(name)
    info.size = len(data)
    info.mtime = epoch
    info.mode = 0o644
    archive.addfile(info, io.BytesIO(data))


def write_tar(path: Path, files: dict[str, bytes], epoch: int) -> None:
    with path.open("wb") as raw:
        with gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=epoch) as compressed:
            with tarfile.open(fileobj=compressed, mode="w", format=tarfile.PAX_FORMAT) as archive:
                for name, data in sorted(files.items()):
                    add_tar_bytes(archive, name, data, epoch)


def write_unitypackage(
    output: Path, files: dict[str, bytes], asset_path: str, root_meta: bytes, epoch: int
) -> None:
    asset_root = PurePosixPath(asset_path)
    if not asset_path.startswith("Assets/") or ".." in asset_root.parts or "\\" in asset_path:
        raise ValueError(f"Invalid Unity import path: {asset_path}")
    entries = {}
    used_guids = set()
    all_files = dict(files)
    all_files[".root.meta"] = root_meta
    for meta_path, meta in sorted(all_files.items()):
        if not meta_path.endswith(".meta"):
            if meta_path + ".meta" not in all_files:
                raise ValueError(f"Missing Unity GUID metadata: {asset_path}/{meta_path}")
            continue
        relative = meta_path[:-5]
        is_root = meta_path == ".root.meta"
        is_folder = is_root or re.search(rb"(?m)^folderAsset: yes\s*$", meta)
        if not is_folder and relative not in files:
            raise ValueError(f"Orphan Unity metadata: {meta_path}")
        match = re.search(rb"(?m)^guid: ([0-9a-fA-F]{32})\s*$", meta)
        if not match or match[1] in used_guids:
            raise ValueError(f"Missing or duplicate Unity GUID: {meta_path}")
        guid = match[1].decode("ascii")
        used_guids.add(match[1])
        pathname = asset_path if is_root else str(asset_root / relative)
        entries[guid + "/pathname"] = pathname.encode("utf-8")
        entries[guid + "/asset.meta"] = meta
        if not is_folder:
            entries[guid + "/asset"] = files[relative]
    write_tar(output, entries, epoch)


def build(root: Path, output: Path, version: str, commit: str | None = None,
          worktree: bool = False) -> dict:
    timestamp = parse_version(version)
    config = json.loads((root / ".github/release-config.json").read_text(encoding="utf-8"))
    repository = config["repository"]
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
        raise ValueError("Invalid repository identity")
    commit = commit or git(root, "rev-parse", "HEAD")
    if not re.fullmatch(r"[0-9a-f]{40,64}", commit):
        raise ValueError("Commit must be a complete Git object ID")
    if output.exists() and any(output.iterdir()):
        raise ValueError(f"Output directory must be empty: {output}")
    git_args = ["git", "-C", str(root), "ls-files", "-z", "--cached"]
    if worktree:
        git_args += ["--others", "--exclude-standard"]
    tracked = sorted(set(subprocess.check_output(git_args).decode("utf-8").rstrip("\0").split("\0")))
    manifest = {
        "schema": 1,
        "repository": repository,
        "version": version,
        "release_tag": "v" + version,
        "release_name": repository.split("/")[1] + "-" + version,
        "commit": commit,
        "built_at": timestamp.isoformat().replace("+00:00", "Z"),
        "timezone": "UTC",
        "source_mode": "worktree" if worktree else "tracked",
        "worktree_dirty": bool(git(root, "status", "--porcelain", "--untracked-files=normal")),
        "packages": [],
    }
    output.mkdir(parents=True, exist_ok=True)
    names = set()
    for package in config["packages"]:
        files = collect_package(root, package["path"], tracked, worktree)
        package_json = json.loads(files["package.json"].decode("utf-8-sig"))
        name = package_json["name"]
        if not re.fullmatch(r"[a-z0-9][a-z0-9._-]*", name) or name in names:
            raise ValueError(f"Invalid or duplicate package name: {name}")
        names.add(name)
        # Keep engine, dependency, shader migration and upstream SemVer contracts intact.
        content_hash = hashlib.sha256()
        for filename, data in sorted(files.items()):
            if filename == "version_info.json":
                continue  # The payload identity does not depend on its date label.
            encoded_name = filename.encode("utf-8")
            content_hash.update(len(encoded_name).to_bytes(8, "big") + encoded_name)
            content_hash.update(len(data).to_bytes(8, "big") + data)
        metadata = {k: v for k, v in manifest.items() if k != "packages"}
        metadata.update({
            "channel": "release", "package_name": name,
            "package_version": package_json["version"], "package_path": package["path"],
            "content_sha256": content_hash.hexdigest(),
        })
        files["version_info.json"] = json_bytes(metadata)
        # Committed .meta files give each package a stable GUID for the version asset.
        if "version_info.json.meta" not in files:
            raise ValueError(f"Missing tracked version_info.json.meta in {package['path']}")
        stem = f"{name}-{version}"
        tgz = output / (stem + ".tgz")
        epoch = int(timestamp.timestamp())
        write_tar(tgz, {"package/" + key: value for key, value in files.items()}, epoch)
        artifacts = [tgz]
        if package.get("unitypackage"):
            unity = output / (stem + ".unitypackage")
            root_meta = read_file(root, PurePosixPath(package["path"] + ".meta"))
            write_unitypackage(unity, files, package["unitypackage"], root_meta, epoch)
            artifacts.append(unity)
        manifest["packages"].append({
            "name": name, "package_version": package_json["version"],
            "path": package["path"], "unity": package_json.get("unity", ""),
            "content_sha256": content_hash.hexdigest(),
            "dependencies": package_json.get("dependencies", {}),
            "artifacts": [
                {"file": p.name, "sha256": hashlib.sha256(p.read_bytes()).hexdigest(),
                 "bytes": p.stat().st_size} for p in artifacts
            ],
        })
    if not names:
        raise ValueError("Release contains no packages")
    (output / "release-manifest.json").write_bytes(json_bytes(manifest))
    checksums = [
        f"{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.name}"
        for p in sorted(output.iterdir()) if p.is_file()
    ]
    (output / "SHA256SUMS.txt").write_text("\n".join(checksums) + "\n", encoding="utf-8")
    notes = [
        f"日期发布号：`{version}`（UTC），标签：`v{version}`。", "",
        f"源提交（工作区构建时为基准提交）：`{commit}`。", "",
        f"源模式：`{manifest['source_mode']}`；包含工作区差异：`{manifest['worktree_dirty']}`。", "",
        "## 安装", "",
        "下载所需模块的 `.tgz`，在 Unity 的 Package Manager 中选择 **Install package from tarball** "
        "（旧版名为 **Add package from tarball**）。请下载这里上传的安装包。", "",
        "如果提供 `.unitypackage`，也可以选择它导入 Assets；同一个包只选一种安装方式。"
        "lilToon 与 lilToon-Ho 共用包名和 GUID，只能安装其中一个。", "",
        "## 版本对齐", "",
        "配布时记录每个仓库的日期发布号和提交号；不同仓库同日发布不代表已经通过组合兼容性验证。"
        "HoUnityTools 的 `HoUnityTools > Ho 渲版本信息` 可复制已安装版本清单。", "",
        "日期号保存在每个包的 `version_info.json` 中。`package.json` 的原始版本和依赖保持不变，"
        "避免影响 Unity / URP 依赖解析。`release-manifest.json` 记录本次模块、依赖和安装包 SHA-256。", "",
        "| 包 | 原始包版本 | 安装包 |", "| --- | --- | --- |",
    ]
    for package in manifest["packages"]:
        artifacts = ", ".join("`" + p["file"] + "`" for p in package["artifacts"])
        notes.append(f"| `{package['name']}` | `{package['package_version']}` | {artifacts} |")
    (output / "RELEASE_NOTES.md").write_text("\n".join(notes) + "\n", encoding="utf-8")
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", default=os.environ.get("RELEASE_VERSION") or None)
    parser.add_argument("--output", type=Path, default=Path("_dist"))
    parser.add_argument("--commit", default=os.environ.get("GITHUB_SHA") or None)
    parser.add_argument("--worktree", action="store_true",
                        help="For local/internal releases: include untracked package files and local deletions")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    version = args.version or datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")
    manifest = build(root, args.output.resolve(), version, args.commit, args.worktree)
    print(f"Built {manifest['release_name']} ({manifest['commit']})")
    for package in manifest["packages"]:
        for artifact in package["artifacts"]:
            print(f"  {artifact['file']} ({artifact['bytes']} bytes)")
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as stream:
            for key in ("version", "release_tag", "release_name"):
                stream.write(f"{key}={manifest[key]}\n")


if __name__ == "__main__":
    main()
