"""Prepare a repaired WTD test pair; never overwrite the source documents."""

import argparse
import copy
import json
from pathlib import Path


DETAIL_ROOT = "graph.eye.details"
DETAIL_GATE = "Gate/EyeDetails"
EYE_EFFECT_PREFIXES = ("Pupil/", "Iris/", "Highlight1/", "Highlight2/")
EYE_EFFECT_TARGETS = ("HO-瞳孔", "HO-眼仁", "HO-高光")
EYE_EFFECT_INPUTS = ("HoPupil", "HoIris", "HoHighlight")


def reachable(nodes, root):
    pending, seen = [root], set()
    while pending:
        node_id = pending.pop()
        if node_id not in seen:
            seen.add(node_id)
            pending.extend(child["node"] for child in nodes[node_id].get("children", []))
    return seen


def parameters_used(nodes):
    return {
        name
        for node in nodes
        for name in [
            node.get("parameterX"),
            node.get("parameterY"),
            *(child.get("weightParameter") for child in node.get("children", [])),
        ]
        if name
    }


def targets_used(nodes):
    return {weight["target"] for node in nodes for weight in node.get("weights", [])}


def is_retired_parameter(name):
    return name == DETAIL_GATE or name.startswith(EYE_EFFECT_PREFIXES)


def verify_graph_contract(nodes, root_id):
    """Fail on a different rig rather than changing the meaning of its axes."""
    root = nodes[root_id]
    if root["type"] != "direct":
        raise ValueError("Expected the WTD direct root.")
    width = nodes["graph.mouth.width"]
    width_poses = ["pose.mouth.width.narrow", "pose.mouth.width.normal", "pose.mouth.width.wide"]
    if width["type"] != "blend1d" or width["parameterX"] != "Mouth/Width" or sorted(
        (child["threshold"], child["node"]) for child in width["children"]
    ) != list(zip((0, 0.5, 1), width_poses)):
        raise ValueError("Expected the existing WTD narrow/normal/wide knots at 0, 0.5, 1.")
    for side in ("Left", "Right"):
        for branch, axis in (("main", "parameterY"), ("anger", "parameterX")):
            node = nodes[f"graph.eye.{side.lower()}.{branch}"]
            coordinate = "y" if branch == "main" else "threshold"
            if node.get(axis) != f"Eye/{side}/Open" or {
                child[coordinate] for child in node["children"]
            } != {-1, 0, 1, 2}:
                raise ValueError(f"Expected the WTD {side}/{branch} eyelid knots at -1, 0, 1, 2.")
            for child in node["children"]:
                label = {1: "半闭", 2: "紧闭"}.get(child[coordinate])
                if label is not None and not any(
                    weight["target"].endswith(label + side) and weight["value"] == 1
                    for weight in nodes[child["node"]].get("weights", [])
                ):
                    raise ValueError(f"Expected a unit {label} BS at {child['node']}.")


def strip_eye_effects(value, ir, nodes):
    if DETAIL_ROOT in nodes:
        retired = reachable(nodes, DETAIL_ROOT)
        root = nodes[ir["root"]]
        root["children"] = [child for child in root["children"] if child["node"] != DETAIL_ROOT]
        still_reachable = retired & reachable(nodes, ir["root"])
        if still_reachable:
            raise ValueError(f"Eye details are also used by a retained rule: {sorted(still_reachable)}")
    else:
        retired = set()
    kept_nodes = [node for node in ir["nodes"] if node["id"] not in retired]
    if any(child["node"] in retired for node in kept_nodes for child in node.get("children", [])):
        raise ValueError("A retained rule still links to an eye-detail node.")
    if any(is_retired_parameter(name) for name in parameters_used(kept_nodes)):
        raise ValueError("A retained rule still uses an eye-detail parameter.")
    if any(name.startswith(EYE_EFFECT_TARGETS) for name in targets_used(kept_nodes)):
        raise ValueError("A retained pose still writes a pupil, iris or highlight BS.")
    ir["nodes"] = kept_nodes
    ir["targets"] = [target for target in ir["targets"] if not target["id"].startswith(EYE_EFFECT_TARGETS)]
    ir["parameters"] = [row for row in ir["parameters"] if not is_retired_parameter(row["name"])]
    value["inputs"] = [
        row for row in value["inputs"]
        if row["parameter"] != "EyeDetailsEnabled" and not row["parameter"].startswith(EYE_EFFECT_INPUTS)
    ]
    value["outputs"] = [row for row in value["outputs"] if not is_retired_parameter(row["parameter"])]


def repair(value, ir, *, width_neutral_min=-0.14, width_neutral_max=0.40, blink_floor=0.48, blink_full=0.80):
    if (value.get("format"), value.get("version"), ir.get("format"), ir.get("version")) != (
        "ho-face-value", 1, "ho-face-ir", 2
    ):
        raise ValueError("Expected a HoFace Value v1 / IR v2 pair.")
    if not -0.93 < width_neutral_min < 0 < width_neutral_max < 2:
        raise ValueError("The mouth neutral interval must include zero and lie between -0.93 and 2.")
    if not 0 < blink_floor < blink_full <= 1:
        raise ValueError("Require 0 < blink floor < full closure <= 1.")
    value, ir = copy.deepcopy((value, ir))
    nodes = {node["id"]: node for node in ir["nodes"]}
    verify_graph_contract(nodes, ir["root"])
    strip_eye_effects(value, ir, nodes)

    rows = value["outputs"]
    by_name = {row["parameter"]: row for row in rows}
    width = by_name["Mouth/Width"]
    width["expression"] = "2 * (mouthDimpleLeft + mouthDimpleRight) - mouthPucker"
    width["defaultValue"] = 0.5
    # Keep the measured full narrow/wide endpoints from the old profile. The
    # plateau covers both the old -0.09 rest and the current +0.356 rest sample.
    low_slope = 0.5 / (width_neutral_min + 0.93)
    high_slope = 0.5 / (2 - width_neutral_max)
    width["curve"] = {"keys": [
        {"t": -0.93, "v": 0, "inT": low_slope, "outT": low_slope},
        {"t": width_neutral_min, "v": 0.5, "inT": 0, "outT": 0},
        {"t": width_neutral_max, "v": 0.5, "inT": 0, "outT": 0},
        {"t": 2, "v": 1, "inT": high_slope, "outT": high_slope},
    ]}
    width["notes"] = (
        "嘴宽独立轴：0 缩，0.5 常态，1 宽。原值为 2×左右酒窝之和−噘嘴；"
        f"在曲线上归中，{width_neutral_min:g}～{width_neutral_max:g} 固定输出 0.5；"
        "保留旧标定的 −0.93 / +2 端点及原平滑。"
    )

    for side, other in (("Left", "Right"), ("Right", "Left")):
        axis = f"Eye/{side}/Open"
        opened, emotion, anger = (by_name[f"Eye/{side}/{key}"] for key in ("Open", "Emotion", "Anger"))
        blink, squint, wide = (f"eye{key}{side}" for key in ("Blink", "Squint", "Wide"))
        # The old squint subtraction was fitted on resting/smiling frames only.
        # A separate strong-blink lower bound must survive simultaneous squint.
        opened["expression"] = (
            f"2 * clamp(max({blink} - 3.9 * {squint} - 0.06, "
            f"({blink} - {blink_floor:g}) / {blink_full - blink_floor:g}), 0, 1) "
            f"- clamp({wide}, 0, 1) * clamp(({blink_floor:g} - {blink}) / {blink_floor:g}, 0, 1)"
        )
        opened["notes"] = (
            "-1 张大，0 睁眼，1 半闭，2 紧闭（实际合眼）。同侧独立求值。"
            f"弱眨眼保留旧去眯眼串扰；blink 达 {blink_floor:g} 后强闭合保底开始生效，"
            f"{blink_full:g} 到紧闭且不受眯眼/睁大抵消；保留 0.007 秒原平滑。"
        )
        emotion["expression"] = (
            f'clamp({squint} * clamp((1 - out("{axis}")) / 0.75, 0, 1) '
            '- max(-out("Mouth/Emotion"), 0), -1, 1)'
        )
        emotion["notes"] = "−1 悲伤，0 常态，1 喜悦。闭合时衰减同侧 Squint 的假笑证据；保留嘴部悲伤来源。"
        anger["expression"] = (
            f'clamp((browDown{side} - 0.45) / 0.15, 0, 1) * '
            f'max(clamp(1 - out("{axis}"), 0, 1), clamp((browDown{other} - 0.6) / 0.25, 0, 1))'
        )
        anger["notes"] = (
            "同侧单独压眉只在眼睛睁开时保留，避免单眼 wink 切入愤怒睁眼。"
            "闭眼时，只有对侧也强压眉才保留愤怒闭眼表现。"
        )
        before = min(rows.index(emotion), rows.index(anger))
        if rows.index(opened) > before:
            rows.remove(opened)
            rows.insert(before, opened)

    old_description = (
        "瞳孔和眼仁保留默认 0 的手调 Value/IR 轴。高光果冻由 Unity 专用组件读取 IR 最终形态键结果，"
        "按同眼分组加权、左右组取最大值；不另外发布 blink 直通输出，也不声明高光 IR 输出。"
    )
    fix_note = (
        "瞳孔、眼仁及高光果冻不由面捕 Value/IR 控制；保留眼睑表情。"
        "眨眼强度与压眉分开，强 blink 到达紧闭；嘴宽静置归中。"
    )
    value["notes"] = value.get("notes", "").replace(old_description, "").rstrip()
    if fix_note not in value["notes"]:
        value["notes"] += "\n2026-11-09：" + fix_note
    unresolved = parameters_used(ir["nodes"]) - {row["parameter"] for row in rows}
    if unresolved:
        raise ValueError(f"Retained IR parameters do not have Value outputs: {sorted(unresolved)}")
    return value, ir


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("value", type=Path, help="existing WTD .hoface.value.json")
    parser.add_argument("ir", type=Path, help="paired WTD .hoface.ir.json")
    parser.add_argument("output", type=Path, help="new output directory (will not overwrite files)")
    parser.add_argument("--width-neutral-min", type=float, default=-0.14)
    parser.add_argument("--width-neutral-max", type=float, default=0.40)
    parser.add_argument("--blink-floor", type=float, default=0.48)
    parser.add_argument("--blink-full", type=float, default=0.80)
    args = parser.parse_args()
    before_value, before_ir = (json.loads(p.read_text(encoding="utf-8-sig")) for p in (args.value, args.ir))
    value, ir = repair(
        before_value, before_ir, width_neutral_min=args.width_neutral_min, width_neutral_max=args.width_neutral_max,
        blink_floor=args.blink_floor, blink_full=args.blink_full,
    )
    args.output.mkdir(parents=True, exist_ok=True)
    paths = [args.output / name for name in ("candidate.value.json", "candidate.ir.json", "apply-payload.json")]
    if any(path.exists() for path in paths):
        parser.error("Use a fresh output directory; a candidate file already exists.")
    value_text, ir_text = (json.dumps(data, ensure_ascii=False, indent=2) + "\n" for data in (value, ir))
    paths[0].write_text(value_text, encoding="utf-8")
    paths[1].write_text(ir_text, encoding="utf-8")
    paths[2].write_text(json.dumps({"value": value_text, "ir": ir_text}, ensure_ascii=False), encoding="utf-8")
    print(json.dumps({
        "inputs": len(value["inputs"]), "outputs": len(value["outputs"]),
        "parameters": len(ir["parameters"]), "nodes": len(ir["nodes"]), "targets": len(ir["targets"]),
    }))


if __name__ == "__main__":
    main()
