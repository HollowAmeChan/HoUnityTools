# jaw-side-fit.py -- 「平移下巴」那批录样的算账工具。回答一件事：
#   **横向那两格（现在钉在 Y=0.75）该摆在哪？摆一个还是摆两对？**
# 看点：① 各组横向振幅（jawRight − jawLeft）的 raw 与过曲线之后的值；
#       ② 各组当时的 (side, Jaw) 落点 —— 这就是「格子该摆哪」的锚点；
#       ③ 伪影对照通道（dimple / press / smile / roll）—— 判断要不要门；
#       ④ 现有 MouthJaw 格子的位置 + 落点的重心权重估计（⚠️ 估计：Unity 的 2D 权重是它自己的距离加权）。
# 用法: python jaw-side-fit.py [labels.json] [--controller x.controller] [--trace-dir DIR]
import argparse
import io
import json
import math
import os
import re
import sys
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

TRACE_DEFAULT = r'D:\Unity_Project\BREAK_URP\Logs\HoFaceTraces'
CTRL_DEFAULT = r'D:\Unity_Project\BREAK_URP\Assets\Hollow\土豆\FT\PTP_CTR_Face_VTS.controller'

ap = argparse.ArgumentParser()
ap.add_argument('labels', nargs='?', default=None)
ap.add_argument('--trace-dir', default=TRACE_DEFAULT)
ap.add_argument('--controller', default=CTRL_DEFAULT)
a = ap.parse_args()

trace_dir = Path(a.trace_dir)
labels = a.labels
if not labels:
    for cand in ('recording-labels-jawside.json', 'recording-labels-20260929.json'):
        p = trace_dir / cand
        if p.is_file():
            labels = str(p)
            break
assert labels and os.path.isfile(labels), '找不到清单文件（--labels 指定）'
man = json.loads(io.open(labels, encoding='utf-8-sig').read())
print('清单：%s' % labels)
print('标签依据：%s' % (man.get('labelBasis') or '')[:120])

STATE = ['jawOpen', 'mouthClose', 'jawForward']
SIDE = ['jawLeft', 'jawRight']
ART = ['mouthDimpleLeft', 'mouthDimpleRight', 'mouthPressLeft', 'mouthPressRight',
       'mouthSmileLeft', 'mouthSmileRight', 'mouthFrownLeft', 'mouthFrownRight',
       'mouthRollLower', 'mouthRollUpper', 'cheekPuff', 'mouthStretchLeft']
WANT = STATE + SIDE + ART


def read_take(path):
    """→ (interval, [ {channel: value, 'out:<行名>': value} ])"""
    dt = 0.05
    frames = []
    for line in io.open(path, encoding='utf-8-sig', errors='replace'):
        line = line.strip()
        if not line:
            continue
        try:
            o = json.loads(line)
        except Exception:
            continue
        if not isinstance(o, dict):
            continue
        if o.get('kind') != 'sample':
            dt = float(o.get('intervalSeconds') or dt)
            continue
        f = {}
        for it in o.get('inputs') or []:
            f[it['name']] = float(it.get('effective') or 0.0)
        for it in o.get('outputs') or []:
            f['out:' + it['name']] = float(it.get('published') or 0.0)
        frames.append(f)
    return dt, frames


def pct(v, q):
    v = sorted(v)
    return v[min(len(v) - 1, int(q * len(v)))] if v else float('nan')


def mean(v):
    return sum(v) / len(v) if v else float('nan')


def side_raw(f):
    return f.get('jawRight', 0) - f.get('jawLeft', 0)


def jaw_raw(f):
    jo, mc = f.get('jawOpen', 0), f.get('mouthClose', 0)
    return jo - max(mc - jo, 0.0)


groups = {}
for r in man['recordings']:
    p = trace_dir / r['filename']
    if not p.is_file():
        p = trace_dir / r.get('originalFilename', r['filename'])
    if not p.is_file():
        print('!! 缺文件：%s' % r['filename'])
        continue
    dt, frames = read_take(p)
    groups.setdefault(r['group'], []).append((r['filename'], dt, frames))

print('')
print('=== ① 各组振幅与状态（每格 = p50 / p90 / max）===')
hdr = '%-22s %3s %5s | %-19s | %-19s | %-13s | %-13s | %-13s' % (
    '组', '次', '帧', 'side_raw jawR-jawL', 'Jaw 表达式值', 'jawOpen', 'mouthClose', 'dimple 均', )
print(hdr)
for g in sorted(groups):
    takes = groups[g]
    allf = [f for _, _, fr in takes for f in fr]
    sr = [side_raw(f) for f in allf]
    jw = [jaw_raw(f) for f in allf]
    jo = [f.get('jawOpen', 0) for f in allf]
    mc = [f.get('mouthClose', 0) for f in allf]
    di = [(f.get('mouthDimpleLeft', 0) + f.get('mouthDimpleRight', 0)) / 2 for f in allf]
    pr = [(f.get('mouthPressLeft', 0) + f.get('mouthPressRight', 0)) / 2 for f in allf]
    print('%-22s %3d %5d | %5.3f/%5.3f/%5.3f | %5.3f/%5.3f/%5.3f | %5.3f/%5.3f | %5.3f/%5.3f | %5.3f/%5.3f' % (
        g[:22], len(takes), len(allf),
        pct(sr, .5), pct(sr, .9), max(sr) if sr else 0,
        pct(jw, .5), pct(jw, .9), max(jw) if jw else 0,
        pct(jo, .5), pct(jo, .9), pct(mc, .5), pct(mc, .9),
        pct(di, .5), pct(di, .9)))

print('')
print('=== ② 落点锚点（每组取 |side_raw| 最大的 10% 帧的均值）—— 横向格子该摆的地方 ===')
anchors = {}
for g in sorted(groups):
    allf = [f for _, _, fr in groups[g] for f in fr]
    if not allf:
        continue
    for sign, tag in ((1, 'side+ 下巴左'), (-1, 'side- 下巴右')):
        sel = sorted(allf, key=lambda f: sign * side_raw(f), reverse=True)
        sel = sel[:max(1, len(sel) // 10)]
        s = mean([side_raw(f) for f in sel])
        j = mean([jaw_raw(f) for f in sel])
        jo = mean([f.get('jawOpen', 0) for f in sel])
        mc = mean([f.get('mouthClose', 0) for f in sel])
        anchors[(g, tag)] = (s, j)
        print('   %-22s %s  side=%+5.3f  Jaw=%5.3f   (jawOpen %5.3f / mouthClose %5.3f)' % (g[:22], tag, s, j, jo, mc))

print('')
print('=== ③ 伪影对照（各组 p50/p90，用来判断要不要门）===')
for g in sorted(groups):
    allf = [f for _, _, fr in groups[g] for f in fr]
    def av(ch):
        return [(f.get(ch + 'Left', 0) + f.get(ch + 'Right', 0)) / 2 for f in allf]
    row = []
    for ch in ('mouthDimple', 'mouthPress', 'mouthSmile', 'mouthFrown', 'mouthStretch'):
        v = av(ch)
        row.append('%s %5.3f/%5.3f' % (ch.replace('mouth', ''), pct(v, .5), pct(v, .9)))
    rl = [f.get('mouthRollLower', 0) for f in allf]
    row.append('rollLow %5.3f' % pct(rl, .5))
    print('   %-22s %s' % (g[:22], '  '.join(row)))

# ---- ④ 现有格子 + 落点权重（重心估计）----
cells = []
try:
    text = io.open(a.controller, encoding='utf-8-sig', errors='replace').read()
    for c in re.split(r'(?m)^--- !u!', text):
        if 'BlendTree:' not in c:
            continue
        nm = re.search(r'(?m)^\s*m_Name:\s*(.+?)\s*$', c)
        if not nm or nm.group(1).strip() != 'MouthJaw':
            continue
        for k in re.finditer(r'(?ms)^  - serializedVersion:.*?m_Position: \{x: ([-\d.e]+), y: ([-\d.e]+)\}', c):
            cells.append((float(k.group(1)), float(k.group(2))))
        break
except Exception as e:
    print('!! 读控制器失败：%s' % e)

print('')
print('=== ④ 现有 MouthJaw 格子：%s ===' % ('  '.join('(%.2f, %.2f)' % c for c in cells) or '<没读到>'))
if len(cells) >= 3:
    xs = sorted(set(round(x, 4) for x, _ in cells))
    ys = sorted(set(round(y, 4) for _, y in cells))
    top = [c for c in cells if abs(c[1] - max(ys)) < 1e-6]
    if len(top) >= 3 and len(set(x for x, _ in top)) == len(top):
        print('   ⚠️ 顶行 %d 个格子共线（Y=%.2f）—— 共线点在 FreeformCartesian2D 里是退化输入（项目里舌头那棵树记过：共线/退化点集不可预测、可能出负权重）。' % (len(top), max(ys)))
    for (g, tag), (s, j) in sorted(anchors.items()):
        # 重心估计：取离落点最近的两个格子与 (0,0)，按三角形面积给权重
        tri = [(0.0, 0.0), (cells[1] if len(cells) > 1 else cells[0], cells[-1])]
        # 直接用「落点 vs 每个格子的距离」给一个粗略可比的数，别假装精确
        d = sorted(((math.hypot(s - cx, j - cy), (cx, cy)) for cx, cy in cells))[:2]
        print('   %-22s %s 落点 (%+.2f, %.2f) → 最近格子 %s 距离 %.3f ；次近 %s 距离 %.3f' % (
            g[:22], tag, s, j, '(%.2f, %.2f)' % d[0][1], d[0][0], '(%.2f, %.2f)' % d[1][1], d[1][0]))

print('')
print('⚠️ 权重是**几何估计**（距离/重心），不是 Unity FreeformCartesian2D 的真实权重（那套是它自己的距离加权，')
print('   项目实测与双线性差约 6%）⇒ 精确值以编辑器里 Blend Tree 预览的权重为准。')
print('   这份表要的是**落点**：哪一组落在哪、离现有格子多远 —— 格子该摆哪由它决定。')
