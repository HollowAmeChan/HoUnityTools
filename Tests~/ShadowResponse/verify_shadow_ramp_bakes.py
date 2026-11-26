#!/usr/bin/env python3
"""Read-only verification of serialized recovery LUTs, independent of Unity/LWGUI.

The reference is the retired shader's clipped linear lilTooning function, not
AnimationCurve evaluation or a reimplementation of the production baker.
Only the optional report is written. No Editor is opened and no asset is changed.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path, PurePosixPath
import re
import struct
import sys


RGBA_HALF = 17
WEIGHT_NAME = "HoShadowResponse.Weights"
CHANNELS = ("R:first-lit", "G:second-lit", "B:third-lit", "A:first-border")
SOURCE_FIELDS = (
    ("border1", "_ShadowBorder"), ("blur1", "_ShadowBlur"),
    ("border2", "_Shadow2ndBorder"), ("blur2", "_Shadow2ndBlur"),
    ("border3", "_Shadow3rdBorder"), ("blur3", "_Shadow3rdBlur"),
    ("borderRange", "_ShadowBorderRange"),
)
HELPER_PACKAGES = (
    "com.hoshader.cloth-urp", "com.hoshader.hair-urp",
    "com.hoshader.skin-urp-stylized", "com.hoshader.environment-urp",
)


class VerificationError(ValueError):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise VerificationError(message)


def f32(value: float) -> float:
    return struct.unpack("<f", struct.pack("<f", value))[0]


def clamp01(value: float) -> float:
    return max(0.0, min(1.0, value))


def bounds(border: float, blur: float, border_range: float = 0.0) -> tuple[float, float]:
    """Float32 shader arithmetic, including clipping each transition endpoint."""
    half_blur = f32(f32(blur) * f32(0.5))
    lower = clamp01(f32(f32(f32(border) - half_blur) - f32(border_range)))
    upper = clamp01(f32(f32(border) + half_blur))
    return lower, upper


def exact_tooning(value: float, lower: float, upper: float) -> float:
    if upper <= lower:
        return float(value >= lower)
    return clamp01(f32(f32(f32(value) - lower) / f32(upper - lower)))


def channel_bounds(ramp: dict) -> tuple[tuple[float, float], ...]:
    return (
        bounds(ramp["border1"], ramp["blur1"]),
        bounds(ramp["border2"], ramp["blur2"]),
        bounds(ramp["border3"], ramp["blur3"]),
        bounds(ramp["border1"], ramp["blur1"], ramp["borderRange"]),
    )


def expected_width(ramp: dict) -> int:
    if ramp.get("isNeutral"):
        return 16
    smallest = min(f32(ramp[key]) for key in ("blur1", "blur2", "blur3"))
    return 16384 if smallest <= f32(0.001) else 4096 if smallest < f32(0.02) else 1024


def half_tolerance(value: float) -> tuple[float, float]:
    """Half round-to-nearest plus a small, explicitly reported float32 budget.

    AnimationCurve uses float32 polynomial evaluation of a linear segment. Its
    rounding can differ from the shader's division. Eight ULPs of float32 at 1
    bound that arithmetic without hiding float16-scale shape/channel mistakes.
    """
    bits = struct.unpack("<H", struct.pack("<e", value))[0]
    rounded = struct.unpack("<e", struct.pack("<H", bits))[0]
    above = struct.unpack("<e", struct.pack("<H", bits + 1))[0]
    below = struct.unpack("<e", struct.pack("<H", max(0, bits - 1)))[0]
    half_ulp = max(above - rounded, rounded - below)
    return half_ulp * 0.5 + 8 * 2.0 ** -23, half_ulp


def scalar(block: str, field: str, kind=int):
    matches = re.findall(r"^  +" + re.escape(field) + r":\s*([^\r\n]*)$", block, re.MULTILINE)
    require(len(matches) == 1, f"Expected one {field}; found {len(matches)}")
    try:
        return kind(matches[0])
    except (TypeError, ValueError) as error:
        raise VerificationError(f"Invalid {field}: {matches[0]!r}") from error


def texture_document(path: Path) -> tuple[str, str, str]:
    text = path.read_text(encoding="utf-8-sig")
    headers = list(re.finditer(r"^--- !u!(\d+) &(-?\d+)[^\r\n]*\r?\n", text, re.MULTILINE))
    documents = [(header.group(1), header.group(2), text[header.end():headers[i + 1].start() if i + 1 < len(headers) else len(text)])
                 for i, header in enumerate(headers)]
    textures = [(local_id, block) for class_id, local_id, block in documents if class_id == "28"]
    require(len(textures) == 1, f"Expected exactly one Texture2D document in {path}; found {len(textures)}")
    local_id, texture = textures[0]
    authoring = "\n".join(block for class_id, _, block in documents if class_id == "114")
    return texture, authoring, local_id


def load_pixels(path: Path, ramp: dict) -> tuple[int, tuple[float, ...], dict]:
    texture, authoring, local_id = texture_document(path)
    width = scalar(texture, "m_Width")
    require(width == expected_width(ramp), f"{path}: width {width} != expected {expected_width(ramp)}")
    expected = {
        "m_Height": 1, "m_TextureFormat": RGBA_HALF, "m_MipCount": 1,
        "m_ImageCount": 1, "m_TextureDimension": 2, "m_FilterMode": 1,
        "m_Aniso": 0, "m_WrapU": 1, "m_WrapV": 1, "m_WrapW": 1,
        "m_ColorSpace": 0, "m_AlphaIsTransparency": 0,
        "m_CompleteImageSize": width * 8, "image data": width * 8,
    }
    for field, value in expected.items():
        actual = scalar(texture, field)
        require(actual == value, f"{path}: {field}={actual}, expected {value}")
    require(scalar(texture, "m_Name", str).strip('"') == WEIGHT_NAME, f"{path}: missing Weights name marker")
    stream = re.search(r"^  m_StreamData:\s*\r?\n(?P<body>(?:^    .*\r?\n?)*)", texture, re.MULTILINE)
    require(stream is not None, f"{path}: missing stream metadata")
    require(scalar(stream.group("body"), "size") == 0, f"{path}: external stream data is unsupported")
    require(scalar(stream.group("body"), "path", str).strip('"') == "", f"{path}: unexpected external stream path")
    payload = re.findall(r"^  _typelessdata:[ \t]*([0-9a-fA-F]*)[ \t]*$", texture, re.MULTILINE)
    require(len(payload) == 1, f"{path}: expected inline _typelessdata")
    try:
        raw = bytes.fromhex(payload[0])
    except ValueError as error:
        raise VerificationError(f"{path}: malformed half-float payload") from error
    require(len(raw) == width * 8, f"{path}: payload has {len(raw)} bytes, expected {width * 8}")
    pixels = struct.unpack("<" + "e" * (width * 4), raw)
    require(all(math.isfinite(value) and 0.0 <= value <= 1.0 for value in pixels), f"{path}: nonfinite/out-of-range weight")
    if ramp.get("isNeutral"):
        require(not authoring, f"{path}: neutral preset unexpectedly depends on a ScriptableObject")
        require(all(value == 1.0 for value in pixels), f"{path}: neutral preset must be exactly white in all four lanes")
    else:
        require(bool(authoring), f"{path}: editable ramp has no authoring document")
        require(scalar(authoring, "responseEncoding") == 1, f"{path}: authoring encoding is not Weights")
        require(scalar(authoring, "sampling") == 1, f"{path}: authoring sampling is not Endpoints")
        require(scalar(authoring, "resolution") == width, f"{path}: authoring resolution differs from its texture")
        require(re.search(r"^  lookupTexture: \{fileID: " + re.escape(local_id) + r"\}$", authoring, re.MULTILINE) is not None,
                f"{path}: authoring object does not own the checked texture local ID")
    return width, pixels, {"width": width, "height": 1, "textureLocalId": local_id,
                           "assetSha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                           "payloadSha256": hashlib.sha256(raw).hexdigest()}


def sample_bilinear(pixels: tuple[float, ...], width: int, channel: int, coordinate: float) -> float:
    # The runtime's endpoint UV remap gives this exact texel-space coordinate.
    x = clamp01(coordinate) * (width - 1)
    first = math.floor(x)
    second = min(first + 1, width - 1)
    fraction = x - first
    return pixels[4 * first + channel] * (1 - fraction) + pixels[4 * second + channel] * fraction


def boundary_errors(ramp: dict, pixels: tuple[float, ...], width: int, samples: int = 8193) -> list[dict]:
    if ramp.get("isNeutral"):
        return []
    result = []
    for channel, (lower, upper) in enumerate(channel_bounds(ramp)):
        if upper > lower and upper - lower > 0.0001001:
            continue
        left = max(0.0, lower - 2.0 / (width - 1))
        right = min(1.0, upper + 2.0 / (width - 1))
        # Uniform local sampling plus exact endpoints and adjacent float32
        # neighbours catches the two sides of a discontinuity deterministically.
        points = {f32(left + (right - left) * i / (samples - 1)) for i in range(samples)}
        for endpoint in (lower, upper):
            bits = struct.unpack("<I", struct.pack("<f", endpoint))[0]
            for neighbour in (max(0, bits - 1), bits, min(0x3F800000, bits + 1)):
                points.add(struct.unpack("<f", struct.pack("<I", neighbour))[0])
        maximum, coordinate = -1.0, 0.0
        for point in sorted(points):
            difference = abs(sample_bilinear(pixels, width, channel, point) - exact_tooning(point, lower, upper))
            if difference > maximum:
                maximum, coordinate = difference, point
        result.append({"channel": CHANNELS[channel], "kind": "hard-step" if upper <= lower else "very-narrow",
                       "lower": lower, "upper": upper, "width": width, "denseSampleCount": len(points),
                       "maxLocalError": maximum, "coordinateAtMaxError": coordinate})
    return result


def verify_texture(path: Path, ramp: dict) -> dict:
    width, pixels, result = load_pixels(path, ramp)
    result.update({"assetPath": ramp["assetPath"], "isNeutral": bool(ramp.get("isNeutral")),
                   "texelsChecked": width, "componentsChecked": width * 4,
                   "maxAbsoluteTexelError": 0.0, "maxHalfUlpError": 0.0})
    if not ramp.get("isNeutral"):
        transitions = channel_bounds(ramp)
        for index in range(width):
            coordinate = f32(index / f32(width - 1))
            for channel, (lower, upper) in enumerate(transitions):
                expected = exact_tooning(coordinate, lower, upper)
                actual = pixels[index * 4 + channel]
                difference = abs(actual - expected)
                tolerance, half_ulp = half_tolerance(expected)
                require(difference <= tolerance,
                        f"{path}: texel {index}, {CHANNELS[channel]}, h={coordinate:.9g}: "
                        f"actual={actual:.9g}, exact={expected:.9g}, error={difference:.9g}, tolerance={tolerance:.9g}")
                result["maxAbsoluteTexelError"] = max(result["maxAbsoluteTexelError"], difference)
                result["maxHalfUlpError"] = max(result["maxHalfUlpError"], difference / half_ulp)
    result["boundarySamplingErrors"] = boundary_errors(ramp, pixels, width)
    return result


class ProjectPaths:
    def __init__(self, project_root: Path):
        self.root = project_root.resolve()
        self.manifest = json.loads((self.root / "Packages/manifest.json").read_text(encoding="utf-8-sig"))
        self.packages: dict[str, Path] = {}

    def package(self, name: str) -> Path:
        if name in self.packages:
            return self.packages[name]
        embedded = self.root / "Packages" / name
        if embedded.is_dir():
            resolved = embedded.resolve()
        else:
            dependency = self.manifest.get("dependencies", {}).get(name, "")
            if dependency.startswith("file:"):
                local = Path(dependency[5:])
                resolved = (local if local.is_absolute() else self.root / "Packages" / local).resolve()
            else:
                matches = [path for path in (self.root / "Library/PackageCache").glob(name + "@*") if path.is_dir()]
                require(len(matches) == 1, f"Cannot unambiguously resolve package {name}: {matches}")
                resolved = matches[0].resolve()
        package_json = resolved / "package.json"
        require(package_json.is_file(), f"No package manifest for {name} at {resolved}")
        require(json.loads(package_json.read_text(encoding="utf-8-sig")).get("name") == name,
                f"Package identity mismatch for {name} at {resolved}")
        self.packages[name] = resolved
        return resolved

    def asset(self, asset_path: str) -> Path:
        relative = PurePosixPath(asset_path.replace("\\", "/"))
        require(not relative.is_absolute() and ".." not in relative.parts, f"Invalid asset path: {asset_path}")
        if relative.parts[0] == "Assets":
            return self.root.joinpath(*relative.parts)
        require(relative.parts[0] == "Packages" and len(relative.parts) > 2, f"Unsupported asset path: {asset_path}")
        return self.package(relative.parts[1]).joinpath(*relative.parts[2:])


def verify_source_pins(plan: dict) -> list[dict]:
    pins = plan.get("sourceMathFiles", [])
    require(len(pins) == 4, "Expected the normalized plan's four retired shader math SHA pins")
    result = []
    for pin in pins:
        path = Path(pin["path"])
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        require(digest == pin["sha256"], f"Retired shader source pin changed: {path}")
        result.append({"path": str(path), "sha256": digest})
    return result


def verify_source_shapes(plan: dict) -> None:
    by_asset = {ramp["assetPath"]: ramp for ramp in plan["ramps"]}
    require(len(by_asset) == len(plan["ramps"]), "Duplicate ramp asset paths in plan")
    used = set()
    for row in plan["rows"]:
        ramp = by_asset.get(row["rampAssetPath"])
        require(ramp is not None, f"Target references an unplanned ramp: {row['rampAssetPath']}")
        used.add(row["rampAssetPath"])
        properties = {prop["name"]: prop for prop in row["sourceProperties"]}
        active = properties.get("_UseShadow", {}).get("scalar", 0.0) > 0.5
        require(active == row["activeShadow"], f"Source activation differs from snapshot: {row['targetGuid']}")
        require(bool(ramp.get("isNeutral")) != active, f"Active/neutral assignment mismatch: {row['targetGuid']}")
        if not active:
            continue
        for key, source_name in SOURCE_FIELDS:
            require(source_name in properties, f"Snapshot lacks {source_name}: {row['targetGuid']}")
            require(f32(ramp[key]) == f32(properties[source_name]["scalar"]),
                    f"Ramp {key} differs from source {source_name}: {row['targetGuid']}")
    require(used == set(by_asset), "Plan includes an unreferenced ramp")


def verify_helpers(paths: ProjectPaths) -> list[dict]:
    records = []
    baseline = None
    for package in HELPER_PACKAGES:
        path = paths.package(package) / "Runtime/Shaders/HoShadowResponse.hlsl"
        data = path.read_bytes()
        require(baseline is None or data == baseline, f"Canonical HLSL helper differs: {path}")
        baseline = data
        records.append({"package": package, "path": str(path), "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()})
    return records


def verify_plan(plan_path: Path, project_root: Path) -> dict:
    plan_data = plan_path.read_bytes()
    plan = json.loads(plan_data.decode("utf-8-sig"))
    require(plan.get("schemaVersion") == 1 and plan.get("kind") == "HoShader.ShadowResponseRecovery", "Unsupported normalized plan")
    require(Path(plan["projectRoot"]).resolve() == project_root.resolve(), "Plan projectRoot differs from the supplied project")
    require(bool(plan.get("ramps")) and bool(plan.get("rows")), "Empty recovery plan")
    pins = verify_source_pins(plan)
    verify_source_shapes(plan)
    paths = ProjectPaths(project_root)
    helpers = verify_helpers(paths)
    textures = [verify_texture(paths.asset(ramp["assetPath"]), ramp) for ramp in plan["ramps"]]
    boundaries = [dict(item, assetPath=texture["assetPath"]) for texture in textures for item in texture["boundarySamplingErrors"]]
    return {
        "passed": True, "planPath": str(plan_path.resolve()), "planSha256": hashlib.sha256(plan_data).hexdigest(),
        "projectRoot": str(project_root.resolve()), "texturesChecked": len(textures),
        "neutralTexturesChecked": sum(texture["isNeutral"] for texture in textures),
        "texelsChecked": sum(texture["texelsChecked"] for texture in textures),
        "componentsChecked": sum(texture["componentsChecked"] for texture in textures),
        "maxAbsoluteTexelError": max(texture["maxAbsoluteTexelError"] for texture in textures),
        "maxHalfUlpError": max(texture["maxHalfUlpError"] for texture in textures),
        "texelTolerance": "0.5 local float16 ULP + 8 float32 ULP at 1 (9.5367431640625e-7)",
        "sourceMathPins": pins, "canonicalHelpers": helpers,
        "boundarySampling": {
            "scope": "Dense local sampling of each hard step or transition <=0.0001001 wide, including exact endpoints and neighbouring float32 coordinates. These finite bilinear footprint errors do not fail exact texel verification.",
            "hardStepChannels": sum(item["kind"] == "hard-step" for item in boundaries),
            "veryNarrowChannels": sum(item["kind"] == "very-narrow" for item in boundaries),
            "maxHardStepError": max((item["maxLocalError"] for item in boundaries if item["kind"] == "hard-step"), default=0.0),
            "maxVeryNarrowError": max((item["maxLocalError"] for item in boundaries if item["kind"] == "very-narrow"), default=0.0),
            "details": boundaries,
        },
        "textures": textures,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("normalized_plan", type=Path)
    parser.add_argument("project_root", type=Path)
    parser.add_argument("--expect-count", type=int, help="Additionally require this texture count, e.g. 69 for the full production plan")
    parser.add_argument("--report", type=Path, help="Write a new JSON evidence file; refuses to overwrite an existing file")
    args = parser.parse_args()
    try:
        result = verify_plan(args.normalized_plan, args.project_root)
        require(args.expect_count is None or result["texturesChecked"] == args.expect_count,
                f"Verified {result['texturesChecked']} textures, expected {args.expect_count}")
        if args.report:
            with args.report.open("x", encoding="utf-8", newline="\n") as stream:
                json.dump(result, stream, ensure_ascii=False, indent=2)
                stream.write("\n")
        summary = {key: result[key] for key in ("passed", "texturesChecked", "neutralTexturesChecked", "texelsChecked", "componentsChecked", "maxAbsoluteTexelError", "maxHalfUlpError")}
        summary["canonicalHelpersChecked"] = len(result["canonicalHelpers"])
        summary["maxHardStepSamplingError"] = result["boundarySampling"]["maxHardStepError"]
        summary["maxVeryNarrowSamplingError"] = result["boundarySampling"]["maxVeryNarrowError"]
        if args.report:
            summary["report"] = str(args.report.resolve())
        print(json.dumps(summary, ensure_ascii=True))
        return 0
    except (VerificationError, OSError, KeyError, ValueError, struct.error) as error:
        print(json.dumps({"passed": False, "error": str(error)}, ensure_ascii=True), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
