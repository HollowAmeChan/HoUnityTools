"""Installer contract tests; no Unity license or third-party Python modules required."""

from datetime import datetime, timezone
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest


SPEC = importlib.util.spec_from_file_location(
    "build_release", Path(__file__).resolve().parents[1] / "scripts/build_release.py"
)
release = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release)
VERSION = "20261121-120000"
COMMIT = "a" * 40


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        subprocess.run(["git", "init", "-q", str(self.root)], check=True)
        self.config = {"repository": "HollowAmeChan/Test", "packages": [{"path": "."}]}
        self.put("package.json", json.dumps({"name": "com.hollow.test", "version": "17.3.0",
                                              "dependencies": {"com.unity.test": "17.3.0"}}))
        self.put("Runtime/Shader.shader", "// shader")
        self.put("version_info.json", '{"version":"dev"}')
        self.put("version_info.json.meta", "fileFormatVersion: 2\nguid: " + "1" * 32 + "\n")
        self.put("Tests~/private.txt", "not an installer asset")
        self.put("Tools~/private.py", "not an installer asset")
        self.put("Tools~.meta", "not an installer asset")
        self.put(".github/secret.txt", "not an installer asset")
        self.put("Documentation~/guide.md", "installation guide")
        self.put("LICENSE.md", "license")
        self.save_config()
        self.track()

    def put(self, path, data):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(data, encoding="utf-8")

    def save_config(self):
        self.put(".github/release-config.json", json.dumps(self.config))

    def track(self):
        subprocess.run(["git", "-C", str(self.root), "add", "."], check=True,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    def build(self, output="dist"):
        return release.build(self.root, self.root / output, VERSION, COMMIT)

    def test_installer_metadata_dependencies_and_checksums(self):
        self.put("untracked-private.txt", "never distribute untracked files")
        manifest = self.build()
        self.assertEqual(manifest["version"], VERSION)
        self.assertEqual(manifest["commit"], COMMIT)
        artifact = manifest["packages"][0]["artifacts"][0]
        path = self.root / "dist" / artifact["file"]
        self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), artifact["sha256"])
        with tarfile.open(path) as archive:
            names = archive.getnames()
            self.assertTrue(all(name.startswith("package/") for name in names))
            self.assertFalse(any(p in name for name in names for p in
                                 ("Tests~", "Tools~", ".github", "untracked")))
            self.assertIn("package/Documentation~/guide.md", names)
            self.assertIn("package/LICENSE.md", names)
            package = json.load(archive.extractfile("package/package.json"))
            self.assertEqual(package["version"], "17.3.0")
            self.assertEqual(package["dependencies"], {"com.unity.test": "17.3.0"})
            info = json.load(archive.extractfile("package/version_info.json"))
            self.assertEqual(info["version"], VERSION)
            self.assertEqual(info["release_tag"], "v" + VERSION)
            self.assertEqual(info["channel"], "release")
        self.assertEqual(json.loads((self.root / "version_info.json").read_text())["version"], "dev")
        for line in (self.root / "dist/SHA256SUMS.txt").read_text().splitlines():
            digest, filename = line.split("  ")
            self.assertEqual(digest, hashlib.sha256((self.root / "dist" / filename).read_bytes()).hexdigest())

    def test_reproducible_archives(self):
        first = self.build("one")["packages"][0]["artifacts"][0]
        second = self.build("two")["packages"][0]["artifacts"][0]
        self.assertEqual(first["sha256"], second["sha256"])

    def test_lfs_pointer_rejected(self):
        self.put("Runtime/texture.png", release.LFS_POINTER.decode() + "\noid sha256:abc\n")
        self.track()
        with self.assertRaisesRegex(ValueError, "LFS pointer"):
            self.build()

    def test_invalid_date_rejected(self):
        for value in ("latest", "20261121-12", "20260230-120000", "20261121-120000\nhello"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                release.parse_version(value)

    def test_refuses_stale_output(self):
        self.build()
        with self.assertRaisesRegex(ValueError, "empty"):
            self.build()

    def test_worktree_includes_new_assets_and_respects_deletions(self):
        self.put("Runtime/New.cs", "class New {}")
        self.put("Runtime/New.cs.meta", "guid: " + "5" * 32 + "\n")
        (self.root / "Runtime/Shader.shader").unlink()
        manifest = release.build(self.root, self.root / "worktree", VERSION, COMMIT, worktree=True)
        self.assertTrue(manifest["worktree_dirty"])
        self.assertEqual(manifest["source_mode"], "worktree")
        with tarfile.open(self.root / "worktree" / manifest["packages"][0]["artifacts"][0]["file"]) as archive:
            self.assertIn("package/Runtime/New.cs", archive.getnames())
            self.assertNotIn("package/Runtime/Shader.shader", archive.getnames())
            info = json.load(archive.extractfile("package/version_info.json"))
            self.assertEqual(len(info["content_sha256"]), 64)

    def test_package_path_cannot_escape(self):
        self.config["packages"][0]["path"] = "../other"
        self.save_config()
        with self.assertRaisesRegex(ValueError, "Unsafe package path"):
            self.build()

    def test_modules_are_isolated(self):
        self.config["packages"] = [{"path": "One"}, {"path": "Two"}]
        for folder in ("One", "Two"):
            self.put(f"{folder}/package.json", json.dumps({"name": "com.hollow." + folder.lower(), "version": "1.0.0"}))
            self.put(f"{folder}/version_info.json", '{"version":"dev"}')
            self.put(f"{folder}/version_info.json.meta", "guid: " + "2" * 32 + "\n")
            self.put(f"{folder}/Runtime/own.txt", folder)
        self.save_config()
        self.track()
        manifest = self.build()
        self.assertEqual(len(manifest["packages"]), 2)
        for package in manifest["packages"]:
            with tarfile.open(self.root / "dist" / package["artifacts"][0]["file"]) as archive:
                self.assertEqual(archive.extractfile("package/Runtime/own.txt").read().decode(), package["path"])

    def test_unitypackage_preserves_guids_and_asset_paths(self):
        files = {
            "Editor.meta": b"guid: " + b"2" * 32 + b"\nfolderAsset: yes\n",
            "Editor/Test.cs": b"class Test {}",
            "Editor/Test.cs.meta": b"guid: " + b"3" * 32 + b"\n",
        }
        output = self.root / "test.unitypackage"
        root_meta = b"guid: " + b"4" * 32 + b"\nfolderAsset: yes\n"
        epoch = int(datetime.now(timezone.utc).timestamp())
        release.write_unitypackage(output, files, "Assets/lilToon", root_meta, epoch)
        with tarfile.open(output) as archive:
            self.assertEqual(archive.extractfile("3" * 32 + "/pathname").read(), b"Assets/lilToon/Editor/Test.cs")
            self.assertEqual(archive.extractfile("3" * 32 + "/asset").read(), b"class Test {}")
            self.assertEqual(archive.extractfile("4" * 32 + "/asset.meta").read(), root_meta)
        files["Editor/Test.cs.meta"] = files["Editor.meta"]
        with self.assertRaisesRegex(ValueError, "duplicate Unity GUID"):
            release.write_unitypackage(output, files, "Assets/lilToon", root_meta, epoch)


if __name__ == "__main__":
    unittest.main()
