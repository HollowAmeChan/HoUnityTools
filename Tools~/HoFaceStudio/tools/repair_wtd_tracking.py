"""Prepare a repaired WTD test pair; never overwrite the source documents."""

import argparse
import copy
import json
from pathlib import Path


CHEEK_KNEE = 0.40
CHEEK_FULL = 0.60
CHEEK_PUCKER_CEILING = 0.1769
CHEEK_MAX = 0.65
MOUTH_SHIFT_X = 0.6
JAW_SHIFT_X = 0.52
EYE_WIDE_END = -0.2
EYE_HALF = 0.5
EYE_CLOSED = 1.0
EYE_OVER_END = 1.2
EYE_OLD_TO_NEW = {-1.0: EYE_WIDE_END, 1.0: EYE_HALF, 2.0: EYE_CLOSED}
ANGER_GATE = "Gate/Anger"
ANGER_INPUT = "AngerEnabled"
# LookAt 读的是接收器发布的**输出**参数（Rotation_x/y/z、Eye*_x/y/z），不是 raw。
# 迁移过来的 value 文档只留了这些名字的输入行，于是 HoVtsLookAt 一条都读不到。
# x / z 取反沿用旧 profile 的符号约定，±180 曲线是旧行的原样值域。
HEAD_POSE_ROWS = (
    ("Rotation_x", "-Rotation_x"), ("Rotation_y", "Rotation_y"), ("Rotation_z", "-Rotation_z"),
    ("Position_x", "Position_x"), ("Position_y", "Position_y"), ("Position_z", "Position_z"),
    ("EyeLeft_x", "EyeLeft_x"), ("EyeLeft_y", "EyeLeft_y"), ("EyeLeft_z", "EyeLeft_z"),
    ("EyeRight_x", "EyeRight_x"), ("EyeRight_y", "EyeRight_y"), ("EyeRight_z", "EyeRight_z"),
)
# 2026-11-09 实机采样（90 秒 180 帧）：eyeWide 峰值 0.681；但 Studio 里读到 0.6 的“输入”
# 是**曲线之后**的值，对应原值 0.30。静息原值就有 0.10 左右（会直接让眼睛挂着 35% 宽），
# 所以曲线先压掉 0.12 的死区，再从 0.12 线性到 0.30 = 满档。
# 闭眼只用 blink 一根：2026-11-09 录音里笑的时候 blink 中位数只有 0.10（旧的 3.9×squint
# 去污项一下减掉 0.6，才是“闭不死”的真凶），而完全闭眼能到 0.99。所以 0.10 以下不动、
# 0.80 就算满闭，中间线性。
EYE_BLINK_DEAD = 0.10
EYE_BLINK_FULL = 0.80
# 睁大：静息中位只有 0.036，p75 0.146、p95 0.418、峰值 0.513。死区只压掉静息噪声，
# 0.08 起算、0.45 满档（一度是 0.12/0.40，低段被吃掉太多，小幅度睁大几乎没反应）。
EYE_WIDE_DEAD = 0.08
EYE_WIDE_FULL = 0.45
# 眯眼（笑眼证据）也一样：VTS 的 eyeSquint 峰值只有 0.34（203 帧实测），
# 直接当 0…1 用的话喜悦轴永远只走三分之一；0.06 以下是静息，0.28 以上算笑满。
EYE_SMILE_DEAD = 0.06
EYE_SMILE_FULL = 0.28
# 睁大眼只在眼睛真要闭上时才让位：blink 0.20 以下全给，0.50 归零。
# 旧的 (0.48-blink)/0.48 在静息 blink≈0.19 时就已经砍掉四成，主动睁大永远到不了满档。
EYE_WIDE_GATE_START = 0.20
EYE_WIDE_GATE_END = 0.50
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
            positions = {round(float(child[coordinate]), 4) for child in node["children"]}
            old = {-1.0, 0.0, 1.0, 2.0}
            new = {EYE_WIDE_END, 0.0, EYE_HALF, EYE_CLOSED}
            if node.get(axis) != f"Eye/{side}/Open" or positions not in (old, new):
                raise ValueError(
                    f"Expected the WTD {side}/{branch} eyelid knots at -1, 0, 1, 2 "
                    f"or {EYE_WIDE_END}, 0, {EYE_HALF}, {EYE_CLOSED}."
                )
            closed_at = {1.0: "半闭", 2.0: "紧闭"} if positions == old else {EYE_HALF: "半闭", EYE_CLOSED: "紧闭"}
            for child in node["children"]:
                label = closed_at.get(round(float(child[coordinate]), 4))
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


def cheek_terms(knee, full):
    """Return the puff amount as a 0..1 gate and as the cheek-axis amount."""
    span = full - knee
    gate = f"clamp((cheekPuff - {knee:g}) / {span:g}, 0, 1)"
    amount = f"{CHEEK_MAX:g} * {gate}"
    return gate, amount


def repair_cheek(value, ir, *, knee=CHEEK_KNEE, full=CHEEK_FULL):
    """One symmetric cheek puff, biased by the mouth shift, instead of a left/right input.

    ARKit only has one cheekPuff channel, so there is no honest left/right puff to
    sample; a real sideways mouth move also lifts cheekPuff, but only up to about
    0.35, which the knee stays above. Both cheeks now puff from cheekPuff alone and
    the mouth shift only weakens the cheek the lips moved toward, which keeps a
    single-side puff readable without claiming a separate left/right signal.
    The puff pose is reached at the axis maximum so that the weakening is visible
    instead of saturating at the old 0.39 knot.
    """
    if not CHEEK_PUCKER_CEILING < knee < full <= 1:
        raise ValueError(
            f"Require {CHEEK_PUCKER_CEILING} < knee < full <= 1 (pucker ceiling is measured)."
        )
    gate, amount = cheek_terms(knee, full)
    rows = value["outputs"]
    by_name = {row["parameter"]: row for row in rows}
    move_index = rows.index(by_name["Mouth/MoveX"])
    for side, direction in (("Left", ""), ("Right", "0 - ")):
        row = by_name[f"Cheek/{side}"]
        if rows.index(row) <= move_index:
            raise ValueError(f"Cheek/{side} must be evaluated after Mouth/MoveX.")
        row["expression"] = f'{amount} * (1 - clamp({direction}out("Mouth/MoveX"), 0, 1))'
        row["notes"] = (
            f"{side} 侧鼓嘴轴：不给左右独立信号，中位时两侧都鼓；"
            "嘴巴往这一侧平移多少，就按同比例削弱这一侧的鼓嘴（平移那一侧的颊被让开）。"
        )
    move = by_name["Gate/MouthMove"]
    move["expression"] = f"MouthMoveEnabled * (1 - {gate})"
    move["notes"] = (
        "嘴整体位移的 IR 分支权重，读取输入 MouthMoveEnabled；鼓嘴量上来时按 "
        "(1 − 鼓嘴量) 关闭位移，避免鼓嘴自己推嘴唇又被位移推一次（歪嘴）。"
    )

    nodes = {node["id"]: node for node in ir["nodes"]}
    for side in ("left", "right"):
        node = nodes[f"graph.cheek.{side}"]
        pose = f"pose.cheek.{side}"
        children = {child["node"]: child for child in node["children"]}
        if pose not in children:
            raise ValueError(f"{node['id']} is missing the child {pose}.")
        children[pose]["threshold"] = CHEEK_MAX


def repair_wide_calibration(value):
    """Calibrate the raw eyeWide channel so a deliberate widen actually reaches the wide end.

    The device rests around 0.10 and only needs about 0.30 for a full widen, so an
    unscaled pass-through left the axis at half of the -0.2 band while also keeping a
    permanent wide bias at rest. Deadzone first, then a linear ramp to the top; the
    input curve is the place for this (calibration lives on the input row) and stays
    draggable in the Studio.
    """
    span = EYE_WIDE_FULL - EYE_WIDE_DEAD
    slope = 1.0 / span
    for side in ("Left", "Right"):
        name = f"eyeWide{side}"
        row = next((item for item in value["inputs"] if item["parameter"] == name), None)
        if row is None:
            raise ValueError(f"Missing input row {name}.")
        row["curve"] = {"keys": [
            {"t": 0.0, "v": 0.0, "inT": 0, "outT": 0},
            {"t": EYE_WIDE_DEAD, "v": 0.0, "inT": 0, "outT": slope},
            {"t": EYE_WIDE_FULL, "v": 1.0, "inT": slope, "outT": 0},
        ]}
        row["notes"] = (
            f"睁大眼输入标定：{EYE_WIDE_DEAD:g} 以下当 0（静息原值就有 0.10 左右），"
            f"{EYE_WIDE_DEAD:g}→{EYE_WIDE_FULL:g} 线性到满档；曲线外按端点算。"
        )


def repair_head_pose(value):
    """Publish the head/eye pose channels HoVtsLookAt consumes.

    The receiver hands consumers its *output* parameters; the migrated document only kept
    the raw pose channels as inputs, so the LookAt component had nothing to read and stopped
    driving the head. These rows pass the calibrated input values through unchanged.
    """
    rows = value["outputs"]
    have = {row["parameter"] for row in rows}
    for name, expression in HEAD_POSE_ROWS:
        if name in have:
            continue
        rows.append({
            "parameter": name,
            "expression": expression,
            "defaultValue": 0,
            "notes": "LookAt 消费的姿态通道；原样透传（x / z 取反沿用旧 profile 的符号约定）。",
            "curve": {"keys": [
                {"t": -180, "v": -180, "inT": 1, "outT": 1},
                {"t": 180, "v": 180, "inT": 1, "outT": 1},
            ]},
            "modifiers": [],
        })


def repair_anger_gate(value):
    """Sadness owns the brow; anger only happens behind a manual switch (Live2D habit).

    Wired as a value-layer gate instead of an IR branch weight: blend1d/blend2d children
    ignore weightParameter, so a gate on the anger branch would fade the whole eye table.
    """
    inputs = value["inputs"]
    rows = value["outputs"]
    if not any(row["parameter"] == ANGER_INPUT for row in inputs):
        inputs.append({
            "parameter": ANGER_INPUT,
            "expression": ANGER_INPUT,
            "defaultValue": 0,
            "notes": "外部手动开关：1 = 允许皱眉进愤怒，0（默认）= 皱眉走悲伤。",
            "curve": {"keys": [
                {"t": 0, "v": 0, "inT": 1, "outT": 1},
                {"t": 1, "v": 1, "inT": 1, "outT": 1},
            ]},
            "modifiers": [],
        })
    by_name = {row["parameter"]: row for row in rows}
    if ANGER_GATE not in by_name:
        gate = {
            "parameter": ANGER_GATE,
            "expression": ANGER_INPUT,
            "defaultValue": 0,
            "notes": "愤怒手动开关；开着时皱眉才进愤怒表，关着时皱眉全归悲伤。",
            "curve": {"keys": [
                {"t": 0, "v": 0, "inT": 1, "outT": 1},
                {"t": 1, "v": 1, "inT": 1, "outT": 1},
            ]},
            "modifiers": [],
        }
        anchor = by_name.get("Gate/Eye/Right")
        rows.insert(rows.index(anchor) + 1 if anchor else len(rows), gate)
        by_name = {row["parameter"]: row for row in rows}
    gate_index = rows.index(by_name[ANGER_GATE])
    for side in ("Left", "Right"):
        for key in ("Emotion", "Anger"):
            if rows.index(by_name[f"Eye/{side}/{key}"]) <= gate_index:
                raise ValueError(f"Eye/{side}/{key} must be evaluated after {ANGER_GATE}.")


def repair_mouth_direction(ir):
    """Put the lateral tables back on ARKit's first-person left/right.

    Pose and target names are ARKit first-person (Left is the character's own
    left), and so are the wire channels, but both lateral tables were migrated
    with the two ends swapped: a mouth that moves left selected the right pose.
    Mouth/MoveX is positive toward the character's left; Jaw/Side is positive
    toward the character's right, so the jaw table points the other way.
    """
    nodes = {node["id"]: node for node in ir["nodes"]}

    def place(node_id, signs, reach):
        node = nodes[node_id]
        if node.get("type") not in ("blend1d", "blend2d"):
            raise ValueError(f"{node_id} is not a blend node.")
        seen = set()
        for child in node.get("children", []):
            sign = signs.get(child["node"])
            if sign is None:
                continue
            seen.add(child["node"])
            child["x"] = sign * (abs(child.get("x", 0)) or reach)
        missing = set(signs) - seen
        if missing:
            raise ValueError(f"{node_id} is missing the children {sorted(missing)}.")

    place("graph.mouth.move", {"pose.mouth.move.left": 1, "pose.mouth.move.right": -1}, MOUTH_SHIFT_X)
    place("graph.jaw", {"pose.jaw.left": -1, "pose.jaw.right": 1}, JAW_SHIFT_X)


def _jelly_settings(row, default):
    """Reuse the jelly values already authored on the row, so re-running keeps hand tuning."""
    for modifier in row.get("modifiers") or []:
        if modifier.get("kind") == "jelly":
            return tuple(modifier.get(key, default[i]) for i, key in enumerate(
                ("strength", "frequency", "damping", "minimum", "maximum")))
    return default


def repair_eye_axis(ir):
    """Rescale the eyelid axis to 0 open / 0.5 half / 1 closed, with -0.2 and 1.2 for jelly.

    The authored tables used -1 / 0 / 1 / 2 (open, half, closed); the axis now uses
    0 / 0.5 / 1 and keeps 1.2 free for the over-close shape that is still being made.
    """
    for name in ("Eye/Left/Open", "Eye/Right/Open"):
        for parameter in ir["parameters"]:
            if parameter["name"] == name:
                parameter["min"] = EYE_WIDE_END
                parameter["max"] = EYE_OVER_END
    nodes = {node["id"]: node for node in ir["nodes"]}
    old_positions = set(EYE_OLD_TO_NEW)

    def rescale(node, coordinate):
        positions = {round(float(child.get(coordinate, 0)), 4) for child in node["children"]}
        if positions != old_positions:
            return  # already on the new scale; mapping again would collide 1.0 with the half knot
        for child in node["children"]:
            child[coordinate] = EYE_OLD_TO_NEW[round(float(child.get(coordinate, 0)), 4)]

    for side in ("left", "right"):
        rescale(nodes[f"graph.eye.{side}.main"], "y")
        rescale(nodes[f"graph.eye.{side}.anger"], "threshold")


def repair(value, ir, *, width_neutral_min=-0.14, width_neutral_max=0.40,
           cheek_knee=CHEEK_KNEE, cheek_full=CHEEK_FULL,
           jelly_strength=None, jelly_frequency=None, jelly_damping=None):
    if (value.get("format"), value.get("version"), ir.get("format"), ir.get("version")) != (
        "ho-face-value", 1, "ho-face-ir", 2
    ):
        raise ValueError("Expected a HoFace Value v1 / IR v2 pair.")
    if not -0.93 < width_neutral_min < 0 < width_neutral_max < 2:
        raise ValueError("The mouth neutral interval must include zero and lie between -0.93 and 2.")
    value, ir = copy.deepcopy((value, ir))
    nodes = {node["id"]: node for node in ir["nodes"]}
    verify_graph_contract(nodes, ir["root"])
    strip_eye_effects(value, ir, nodes)

    rows = value["outputs"]
    by_name = {row["parameter"]: row for row in rows}
    repair_anger_gate(value)
    repair_wide_calibration(value)
    repair_head_pose(value)
    by_name = {row["parameter"]: row for row in value["outputs"]}
    width = by_name["Mouth/Width"]
    target_width = "2 * (mouthDimpleLeft + mouthDimpleRight) - mouthPucker"
    if width.get("expression") != target_width:
        width["expression"] = target_width
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
        # 0 常态 / 0.5 半闭 / 1 闭眼。闭眼只看 blink 的一根线性斜坡（不再减 squint：
        # 实测笑的时候 blink 中位才 0.10，旧去污项减 0.6 会把闭合吃掉）。眼宽直接压到 −0.2；
        # 1.2 过闭没有对应输入，仍然只由果冻推出去。
        opened["expression"] = (
            f"clamp(({blink} - {EYE_BLINK_DEAD:g}) / {EYE_BLINK_FULL - EYE_BLINK_DEAD:g}, 0, 1) "
            f"- {abs(EYE_WIDE_END):g} * clamp({wide}, 0, 1) "
            f"* clamp(({EYE_WIDE_GATE_END:g} - {blink}) / {EYE_WIDE_GATE_END - EYE_WIDE_GATE_START:g}, 0, 1)"
        )
        opened["curve"] = {"keys": [
            {"t": EYE_WIDE_END, "v": EYE_WIDE_END, "inT": 1, "outT": 1},
            {"t": EYE_OVER_END, "v": EYE_OVER_END, "inT": 1, "outT": 1},
        ]}
        tuned = _jelly_settings(opened, (0.0, 6.0, 0.3, EYE_WIDE_END, EYE_OVER_END))
        strength = jelly_strength if jelly_strength is not None else tuned[0]
        frequency = jelly_frequency if jelly_frequency is not None else tuned[1]
        damping = jelly_damping if jelly_damping is not None else tuned[2]
        opened["modifiers"] = [
            {"kind": "smooth", "seconds": 0.007, "steps": []},
            {"kind": "jelly", "seconds": 0, "steps": [], "strength": strength,
             "frequency": frequency, "damping": damping,
             "minimum": EYE_WIDE_END, "maximum": EYE_OVER_END},
        ]
        opened["notes"] = (
            "0 常态睁眼，0.5 半闭，1 闭眼。闭眼只看 blink 一根线性斜坡："
            f"{EYE_BLINK_DEAD:g} 以下算睁（笑的时候 blink 中位只有 0.10），"
            f"{EYE_BLINK_FULL:g} 就算满闭（实测完全闭眼 0.99）—— 不再减 squint，"
            "旧的 3.9×squint 去污项会把闭合整段吃掉。"
            "眼宽输入直接压到 −0.2 张大（闭眼时自动失效）；"
            "1.2 过闭没有对应输入，只由果冻（轴值修饰器）推出去："
            f"强度 0 = 直通，绝不会越过 {EYE_WIDE_END:g}…{EYE_OVER_END:g}；强度越大过冲越远。"
        )
        # 这两行原本按旧刻度（半闭 = 1）写的：半闭以前等于"已闭"，现在缩到 0.5。
        emotion["expression"] = (
            f'clamp(max(max(out("Mouth/Emotion"), 0), '
            f'clamp(({squint} - {EYE_SMILE_DEAD:g}) / {EYE_SMILE_FULL - EYE_SMILE_DEAD:g}, 0, 1) '
            f'* clamp((0.5 - out("{axis}")) / 0.5, 0, 1)) '
            '- max(-out("Mouth/Emotion"), 0) '
            f'- (1 - out("{ANGER_GATE}")) * clamp((browDown{side} - 0.45) / 0.15, 0, 1), -1, 1)'
        )
        emotion["notes"] = (
            "−1 悲伤，0 常态，1 喜悦。喜悦证据取「嘴的喜悦」和「眯眼证据」的较大值，"
            "这样眼睛和嘴的值天然对齐（实测：大张嘴笑时 eyeSquint 只有 0.05，光靠眯眼永远到不了满档）；"
            f"眯眼那一路按实测标定：{EYE_SMILE_DEAD:g} 以下当 0、{EYE_SMILE_FULL:g} 以上算满，"
            "并且只在眼睛没闭时才算数。嘴的悲伤照常叠加；"
            "愤怒开关关着时，同侧皱眉按悲伤算（Live2D 的优先级）。"
        )
        anger["expression"] = (
            f'out("{ANGER_GATE}") * clamp((browDown{side} - 0.45) / 0.15, 0, 1) * '
            f'max(clamp(1 - 2 * out("{axis}"), 0, 1), clamp((browDown{other} - 0.6) / 0.25, 0, 1))'
        )
        anger["notes"] = (
            "只有手动开关 Gate/Anger 打开时皱眉才进愤怒表；关着时恒 0，皱眉全走悲伤。"
            "同侧单独压眉只在眼睛睁开时保留，避免单眼 wink 切入愤怒睁眼。"
            "闭眼时，只有对侧也强压眉才保留愤怒闭眼表现。"
        )
        before = min(rows.index(emotion), rows.index(anger))
        if rows.index(opened) > before:
            rows.remove(opened)
            rows.insert(before, opened)

    repair_mouth_direction(ir)
    repair_eye_axis(ir)
    repair_cheek(value, ir, knee=cheek_knee, full=cheek_full)

    old_description = (
        "瞳孔和眼仁保留默认 0 的手调 Value/IR 轴。高光果冻由 Unity 专用组件读取 IR 最终形态键结果，"
        "按同眼分组加权、左右组取最大值；不另外发布 blink 直通输出，也不声明高光 IR 输出。"
    )
    fix_note = (
        "瞳孔、眼仁及高光果冻不由面捕 Value/IR 控制；保留眼睑表情。"
        "眨眼强度与压眉分开，强 blink 到达闭眼；嘴宽静置归中。"
        f"鼓嘴量只取 cheekPuff（{cheek_knee:g} 起、{cheek_full:g} 满），嘴巴左右平移不再触发鼓嘴，"
        "鼓嘴生效时关闭嘴整体位移，避免叠成歪嘴。"
        f"眼睑轴改为 0 常态 / {EYE_HALF:g} 半闭 / {EYE_CLOSED:g} 闭眼，"
        f"眼宽输入压到 {EYE_WIDE_END:g} 张大（闭眼时失效），{EYE_OVER_END:g} 过闭只由果冻修饰器推出。"
        f"皱眉默认算悲伤；愤怒要手动开 {ANGER_INPUT}（{ANGER_GATE}），默认关。"
    )
    value["notes"] = value.get("notes", "").replace(old_description, "").rstrip()
    kept = [line for line in value["notes"].splitlines() if not line.startswith("2026-11-09：")]
    value["notes"] = "\n".join(kept + ["2026-11-09：" + fix_note]).strip()
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
    parser.add_argument("--cheek-knee", type=float, default=CHEEK_KNEE)
    parser.add_argument("--cheek-full", type=float, default=CHEEK_FULL)
    parser.add_argument("--jelly-strength", type=float, default=None, help="留空 = 沿用行上已有的果冻强度")
    parser.add_argument("--jelly-frequency", type=float, default=None)
    parser.add_argument("--jelly-damping", type=float, default=None)
    args = parser.parse_args()
    before_value, before_ir = (json.loads(p.read_text(encoding="utf-8-sig")) for p in (args.value, args.ir))
    value, ir = repair(
        before_value, before_ir, width_neutral_min=args.width_neutral_min, width_neutral_max=args.width_neutral_max,
        cheek_knee=args.cheek_knee, cheek_full=args.cheek_full,
        jelly_strength=args.jelly_strength, jelly_frequency=args.jelly_frequency,
        jelly_damping=args.jelly_damping,
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
