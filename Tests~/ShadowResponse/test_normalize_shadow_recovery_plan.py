import copy
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("normalizer", Path(__file__).with_name("normalize_shadow_recovery_plan.py"))
normalizer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(normalizer)


class RecoveryPlanChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.project = self.root / "Project"
        (self.project / "Assets/Actor/Mat").mkdir(parents=True)
        self.shader = self.root / "Original.shader"
        self.shader.write_text('_ShadowColor ("Shadow", Color) = (1,1,1,1)', encoding="utf-8")
        (self.root / "Includes").mkdir()
        for name in normalizer.MATH_FILES:
            (self.root / "Includes" / name).write_text("original-math-" + name, encoding="utf-8")
        self.preview = self.root / "preview.json"
        self.inventory = self.root / "inventory.json"
        sources, targets, proposals = [], [], []
        for index, active in enumerate([True, False]):
            guid = str(index + 1) * 32
            backup = self.root / (guid + ".bin")
            backup.write_bytes(("original-material-" + guid).encode())
            values = {name: value for name, value in zip(normalizer.SHAPES, [.5, .1, .15, 0, .25, .1, .08])}
            values.update({"_UseShadow": float(active), "_ShadowStrength": .75, "_AOSource": 3,
                           "_AORealtimeWeight": 1, "_AOLevel": -.272, "_AOContrast": 1.41})
            inputs = [{"name": name, "type": "Float", "scalar": value} for name, value in values.items()]
            inputs += [{"name": name, "type": "Texture", "textureGuid": "", "texturePath": ""}
                       for name in ["_AOMap", "_AOMask"]]
            inputs.append({"name": "_ShadowColor", "type": "Color", "value": {"x": .82, "y": .76, "z": .85, "w": 1}})
            shape = {name: float(values[name]) for name in normalizer.SHAPES}
            sources.append({"sourceGuid": guid, "sourcePath": "Assets/Original" + str(index) + ".mat",
                            "sourceSha256": normalizer.sha(backup), "sourceMetaSha256": "a" * 64,
                            "sourceShaderGuid": "f" * 32, "sourceShaderPath": "Packages/source/Original.shader",
                            "sourceBackup": str(backup), "sourcePreviewPath": str(self.preview),
                            "fourLaneLUTShapeSignature": normalizer.fingerprint(shape)})
            proposals.append({"sourceGuid": guid, "sourceInputs": inputs})
            for fork in range(2 if active else 1):
                target = self.project / ("Assets/Actor/Mat/Target" + str(index) + str(fork) + ".mat")
                target.write_bytes(b"unchanged target")
                target_guid = (str(index + 3) + str(fork + 6)) * 16
                Path(str(target) + ".meta").write_text("guid: " + target_guid + "\n", encoding="utf-8")
                targets.append({"sourceGuid": guid, "targetGuid": target_guid,
                                "targetPath": target.relative_to(self.project).as_posix(),
                                "targetSha256": normalizer.sha(target), "targetFamily": "Cloth",
                                "activeShadow": active, "isFork": bool(fork),
                                "suggestedRampDirectory": "Assets/Actor/Tex/ShadowRamps"})
        self.data = {"sourceRows": sources, "targetRows": targets, "errors": [],
                     "sourceShaderEvidence": [{"sourceShaderPath": "Packages/source/Original.shader",
                                               "currentSourceFile": str(self.shader),
                                               "expectedSha256": normalizer.sha(self.shader),
                                               "currentSha256": normalizer.sha(self.shader)}]}
        self.preview.write_text(json.dumps({"proposals": proposals}), encoding="utf-8")
        self.write_inventory()

    def write_inventory(self):
        self.inventory.write_text(json.dumps(self.data), encoding="utf-8")

    def run_plan(self):
        return normalizer.generate(self.inventory, self.project, self.root / "provenance.json", "fixed-UTC")

    def test_dedup_raw_colors_ao_provenance_and_no_asset_writes(self):
        before = {p: p.read_bytes() for p in self.project.rglob("*") if p.is_file()}
        plan, provenance = self.run_plan()
        self.assertEqual(plan["summary"]["activeOwnedRampCount"], 1)
        self.assertEqual(plan["summary"]["usedNeutralPresetCount"], 1)
        self.assertEqual(plan["rows"][0]["sourceAOParams"], {"x": 3, "y": 1, "z": -.272, "w": 1.41})
        color = next(p for p in plan["rows"][0]["sourceProperties"] if p["name"] == "_ShadowColor")
        self.assertEqual([color[k] for k in ["x", "y", "z", "w"]], [.82, .76, .85, 1])
        self.assertEqual(len(json.loads(provenance)["sourceSnapshots"]), 2)
        self.assertEqual(len(plan["sourceMathFiles"]), 4)
        for source in plan["sourceMathFiles"]:
            self.assertTrue(Path(source["path"]).is_absolute())
            self.assertEqual(source["sha256"], normalizer.sha(Path(source["path"])))
        self.assertEqual(plan, self.run_plan()[0])
        self.assertEqual(before, {p: p.read_bytes() for p in self.project.rglob("*") if p.is_file()})
        created = [p["path"] for p in plan["directories"] if p["create"]]
        self.assertEqual(created, ["Assets/Actor/Tex", "Assets/Actor/Tex/ShadowRamps"])

    def test_corrupt_original_backup_is_rejected(self):
        Path(self.data["sourceRows"][0]["sourceBackup"]).write_bytes(b"tampered original")
        with self.assertRaisesRegex(ValueError, "backup identity changed"):
            self.run_plan()

    def test_changed_target_guid_is_rejected(self):
        target = self.project / self.data["targetRows"][0]["targetPath"]
        Path(str(target) + ".meta").write_text("guid: " + "a" * 32 + "\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "Target GUID changed"):
            self.run_plan()

    def test_current_material_hash_is_refreshed_without_hiding_inventory_change(self):
        target = self.project / self.data["targetRows"][0]["targetPath"]
        target.write_bytes(b"legitimate post-inventory material edit")
        plan, _ = self.run_plan()
        row = plan["rows"][0]
        self.assertTrue(row["targetChangedSinceInventory"])
        self.assertEqual(row["targetSha256"], normalizer.sha(target))
        self.assertNotEqual(row["targetSha256"], row["inventoryTargetSha256"])

    def test_path_traversal_and_directory_file_collision_are_rejected(self):
        for value in ["", "../Assets/Out.mat", "Assets/../../Out.mat", "/Assets/Out.mat", "Packages/Out.mat"]:
            with self.assertRaises(ValueError):
                normalizer.asset_path(value)
        (self.project / "Assets/Actor/Tex").write_bytes(b"file, not a directory")
        with self.assertRaisesRegex(ValueError, "Directory collides"):
            self.run_plan()

    def test_conflicting_effective_snapshots_are_rejected(self):
        second = self.root / "second-preview.json"
        proposal = copy.deepcopy(json.loads(self.preview.read_text())["proposals"][0])
        proposal["sourceInputs"][0]["scalar"] = .99
        second.write_text(json.dumps({"proposals": [proposal]}), encoding="utf-8")
        self.data["sourceRows"][1]["sourcePreviewPath"] = str(second)
        self.write_inventory()
        with self.assertRaisesRegex(ValueError, "Inconsistent effective source snapshots"):
            self.run_plan()

    def test_missing_math_is_rejected_and_changed_math_changes_plan_fingerprint(self):
        before = self.run_plan()[0]
        source = self.root / "Includes" / normalizer.MATH_FILES[0]
        source.write_text("changed-math", encoding="utf-8")
        after = self.run_plan()[0]
        self.assertNotEqual(before["inputFingerprint"], after["inputFingerprint"])
        source.unlink()
        with self.assertRaisesRegex(ValueError, "Missing original source math include"):
            self.run_plan()


if __name__ == "__main__":
    unittest.main()
