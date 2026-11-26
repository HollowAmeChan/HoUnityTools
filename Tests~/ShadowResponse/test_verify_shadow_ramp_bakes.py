import copy
import json
from pathlib import Path
import struct
import tempfile
import unittest

import verify_shadow_ramp_bakes as verify


def ramp(**changes):
    value = {"assetPath": "Assets/Response.asset", "isNeutral": False,
             "border1": .5, "blur1": 1.0, "border2": .5, "blur2": 1.0,
             "border3": .5, "blur3": 1.0, "borderRange": 0.0}
    value.update(changes)
    return value


def texture_yaml(width, components, *, neutral=False):
    payload = struct.pack("<" + "e" * len(components), *components).hex()
    authoring = "" if neutral else f"""--- !u!114 &11400000
MonoBehaviour:
  lookupTexture: {{fileID: 2800000}}
  resolution: {width}
  sampling: 1
  responseEncoding: 1
"""
    return f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
{authoring}--- !u!28 &2800000
Texture2D:
  m_Name: HoShadowResponse.Weights
  m_Width: {width}
  m_Height: 1
  m_CompleteImageSize: {width * 8}
  m_TextureFormat: 17
  m_MipCount: 1
  m_ImageCount: 1
  m_TextureDimension: 2
  m_AlphaIsTransparency: 0
  m_TextureSettings:
    m_FilterMode: 1
    m_Aniso: 0
    m_WrapU: 1
    m_WrapV: 1
    m_WrapW: 1
  m_ColorSpace: 0
  image data: {width * 8}
  _typelessdata: {payload}
  m_StreamData:
    offset: 0
    size: 0
    path:
"""


class BakeVerificationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.path = Path(self.temporary.name) / "Response.asset"

    def write_identity(self):
        # A known identity ramp, generated without the verifier's tooning code.
        values = [value for i in range(1024) for value in [i / 1023.0] * 4]
        self.path.write_text(texture_yaml(1024, values), encoding="utf-8")
        return values

    def test_actual_half_payload_matches_analytical_identity(self):
        self.write_identity()
        before = self.path.read_bytes()
        result = verify.verify_texture(self.path, ramp())
        self.assertEqual(result["componentsChecked"], 4096)
        self.assertLessEqual(result["maxHalfUlpError"], .501)
        self.assertEqual(before, self.path.read_bytes())

    def test_one_wrong_channel_is_rejected(self):
        values = self.write_identity()
        values[512 * 4 + 1] = 0.0
        self.path.write_text(texture_yaml(1024, values), encoding="utf-8")
        with self.assertRaisesRegex(verify.VerificationError, "texel 512, G:second-lit"):
            verify.verify_texture(self.path, ramp())

    def test_wrong_linear_sampler_and_ownership_metadata_are_rejected(self):
        self.write_identity()
        original = self.path.read_text(encoding="utf-8")
        for old, new, message in (
            ("m_ColorSpace: 0", "m_ColorSpace: 1", "m_ColorSpace"),
            ("m_FilterMode: 1", "m_FilterMode: 0", "m_FilterMode"),
            ("m_WrapU: 1", "m_WrapU: 0", "m_WrapU"),
            ("responseEncoding: 1", "responseEncoding: 0", "not Weights"),
            ("sampling: 1", "sampling: 0", "not Endpoints"),
            ("lookupTexture: {fileID: 2800000}", "lookupTexture: {fileID: 2}", "does not own"),
            ("size: 0", "size: 64", "external stream"),
            ("m_Name: HoShadowResponse.Weights", "m_Name: ordinary", "Weights name marker"),
        ):
            with self.subTest(field=old):
                self.path.write_text(original.replace(old, new), encoding="utf-8")
                with self.assertRaisesRegex(verify.VerificationError, message):
                    verify.verify_texture(self.path, ramp())

    def test_neutral_requires_exactly_four_white_weights(self):
        neutral = ramp(isNeutral=True)
        self.path.write_text(texture_yaml(16, [1.0] * 64, neutral=True), encoding="utf-8")
        self.assertEqual(verify.verify_texture(self.path, neutral)["componentsChecked"], 64)
        self.path.write_text(texture_yaml(16, [1.0] * 63 + [.5], neutral=True), encoding="utf-8")
        with self.assertRaisesRegex(verify.VerificationError, "exactly white"):
            verify.verify_texture(self.path, neutral)

    def test_hard_step_texels_pass_but_bilinear_boundary_error_is_separate(self):
        source = ramp(blur1=0.0, blur2=0.0, blur3=0.0)
        width = 16384
        values = [value for i in range(width) for value in [float(i / (width - 1) >= .5)] * 4]
        self.path.write_text(texture_yaml(width, values), encoding="utf-8")
        result = verify.verify_texture(self.path, source)
        self.assertEqual(result["maxAbsoluteTexelError"], 0.0)
        self.assertEqual(len(result["boundarySamplingErrors"]), 4)
        self.assertTrue(all(item["kind"] == "hard-step" for item in result["boundarySamplingErrors"]))
        self.assertAlmostEqual(max(item["maxLocalError"] for item in result["boundarySamplingErrors"]), .5)

    def test_very_narrow_boundary_is_quantified_without_relaxing_texel_validation(self):
        source = ramp(border1=.49995, blur1=.0001)
        transitions = verify.channel_bounds(source)
        width = 16384
        values = [verify.exact_tooning(verify.f32(i / (width - 1)), lower, upper)
                  for i in range(width) for lower, upper in transitions]
        self.path.write_text(texture_yaml(width, values), encoding="utf-8")
        result = verify.verify_texture(self.path, source)
        errors = result["boundarySamplingErrors"]
        self.assertEqual(len(errors), 2)
        self.assertTrue(all(item["kind"] == "very-narrow" for item in errors))
        self.assertGreater(max(item["maxLocalError"] for item in errors), .15)
        self.assertLess(max(item["maxLocalError"] for item in errors), .16)

    def test_reference_is_clipped_linear_with_source_step_rule(self):
        lower, upper = verify.bounds(.5, .4)
        self.assertAlmostEqual(verify.exact_tooning(.4, lower, upper), .25, places=6)
        lower, upper = verify.bounds(0.0, .4)
        self.assertEqual(lower, 0.0)
        self.assertAlmostEqual(verify.exact_tooning(.1, lower, upper), .5, places=6)
        self.assertEqual(verify.exact_tooning(.5, .5, .5), 1)
        self.assertEqual(verify.exact_tooning(.499, .5, .5), 0)

    def test_ramp_shapes_are_cross_checked_against_original_source_properties(self):
        source = ramp()
        properties = [{"name": name, "scalar": source[key]} for key, name in verify.SOURCE_FIELDS]
        properties.append({"name": "_UseShadow", "scalar": 1})
        plan = {"ramps": [source], "rows": [{"targetGuid": "test", "rampAssetPath": source["assetPath"],
                                                "activeShadow": True, "sourceProperties": properties}]}
        verify.verify_source_shapes(plan)
        wrong = copy.deepcopy(plan)
        wrong["ramps"][0]["border2"] = .9
        with self.assertRaisesRegex(verify.VerificationError, "differs from source _Shadow2ndBorder"):
            verify.verify_source_shapes(wrong)

    def test_all_four_helper_bytes_are_checked(self):
        root = Path(self.temporary.name)
        (root / "Packages").mkdir()
        dependencies = {}
        for package in verify.HELPER_PACKAGES:
            directory = root / package
            helper = directory / "Runtime/Shaders/HoShadowResponse.hlsl"
            helper.parent.mkdir(parents=True)
            helper.write_bytes(b"same helper\n")
            (directory / "package.json").write_text(json.dumps({"name": package}), encoding="utf-8")
            dependencies[package] = "file:" + str(directory.resolve())
        (root / "Packages/manifest.json").write_text(json.dumps({"dependencies": dependencies}), encoding="utf-8")
        self.assertEqual(len(verify.verify_helpers(verify.ProjectPaths(root))), 4)
        helper.write_bytes(b"different helper\n")
        with self.assertRaisesRegex(verify.VerificationError, "Canonical HLSL helper differs"):
            verify.verify_helpers(verify.ProjectPaths(root))


if __name__ == "__main__":
    unittest.main()
