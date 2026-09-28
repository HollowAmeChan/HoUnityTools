# reorder-output-rows.py -- 按「归属分组」重排 profile 的 outputs 行序
#
# 为什么需要它：`out("…")` **只能朝上读**（`HoFaceOutputTable`：行按顺序写进表、后写覆盖先写）
#   ⇒ 行序既是求值顺序也是依赖顺序，不能手工乱挪 —— 这份脚本搬完**逐行验依赖**，断了就报错、不写盘。
#
# 分组口径（2026-09-29 用户定）：
#   出口 90 行 → Ho/Drive 各区域成块（嘴 / 眼睑 / 注视 / 眉 / 颊 / 鼻）→ 区域门 → 切片
#   → **style 整区靠下**，区内按大类成块（每条形态的判定行 + 开关行 + 发布行 + 该形态自己的处理行），
#     最后是三类共享的仲裁行与形态门。
#
# 用法：
#   python reorder-output-rows.py <profile.json>            # 干跑：打新排布 + 验依赖
#   python reorder-output-rows.py <profile.json> --write    # 落盘（只重排行，字段一个都不动）
#   python reorder-output-rows.py <profile.json> --check    # 自检：按原序重发一遍应与原文件逐字节相同
import io
import json
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

# ── 分组表：自上而下（先匹配到的赢；组内保持原相对顺序 —— 同名的链行天然保持"先判定后合成"）
PUCKER = ('Ho/Style/InvertedV', 'Ho/Drive/Style/InvertedV', 'Ho/Style/MouthWidth', 'Ho/Drive/Mouth/Pucker')


def is_pucker(n):
    return n in PUCKER or n.startswith('Ho/Style/InvertedV/') or n.startswith('Ho/Style/MouthWidth/')


GROUPS = [
    ('① 出口（ARKit 52 / VTS 20 / VB 5 / 姿态 12 / 信号 1）', lambda n: not n.startswith('Ho/')),
    ('② Ho/Drive/Mouth',        lambda n: n.startswith('Ho/Drive/Mouth/') and not is_pucker(n)),
    ('③ Ho/Drive/Lid',          lambda n: n.startswith('Ho/Drive/Lid/')),
    ('④ Ho/Drive/Gaze',         lambda n: n.startswith('Ho/Drive/Gaze/')),
    ('⑤ Ho/Drive/Brow',         lambda n: n.startswith('Ho/Drive/Brow/')),
    ('⑥ Ho/Drive/Cheek',        lambda n: n.startswith('Ho/Drive/Cheek/')),
    ('⑦ Ho/Drive/Nose',         lambda n: n.startswith('Ho/Drive/Nose/')),
    ('⑧ Ho/Drive/Gate（区域门）', lambda n: n.startswith('Ho/Drive/Gate/') and n != 'Ho/Drive/Gate/MouthStyle'),
    ('⑨ Ho/Drive/Slice',        lambda n: n.startswith('Ho/Drive/Slice/')),
    ('⑩ style · Cheek',         lambda n: n.startswith('Ho/Style/Cheek') or n == 'Ho/Drive/Style/Cheek'),
    ('⑪ style · CatMouth',      lambda n: n.startswith('Ho/Style/CatMouth') or n == 'Ho/Drive/Style/CatMouth'),
    ('⑫ style · 噘嘴（读数 / 门 / 判定 / 开关 / 发布 / 宽度换算 / 归中 / 转发）', is_pucker),
    ('⑬ style · 共享（三类仲裁 + 形态门）', lambda n: n in ('Ho/Style/MouthGate', 'Ho/Drive/Gate/MouthStyle')),
]
FALLBACK = '⓪ 其它（没归进任何组，请补规则）'


def group_of(name):
    for i, (label, pred) in enumerate(GROUPS):
        if pred(name):
            return i, label
    return len(GROUPS), FALLBACK


def match_brace(text, i):
    """text[i] 是 { 或 [，返回配对下标（跳过字符串）"""
    depth = 0
    while True:
        c = text[i]
        if c == '"':
            i += 1
            while True:
                if text[i] == '\\':
                    i += 2
                    continue
                if text[i] == '"':
                    break
                i += 1
        elif c in '[{':
            depth += 1
        elif c in ']}':
            depth -= 1
            if depth == 0:
                return i
        i += 1


def split_outputs(text):
    """→ (头, [行块…], [每块**后面**的分隔…], 数组起点, 数组终点)
    分隔符**跟位置走**（最后那个没有逗号）⇒ 只换块不换分隔，重排后排版与语法都不会坏。"""
    at = text.index('"outputs"')
    arr = text.index('[', at)
    body_start = arr + 1
    arr_end = match_brace(text, arr)
    body = text[body_start:arr_end]
    head = body[:len(body) - len(body.lstrip())]
    blocks, seps, i = [], [], len(head)
    while i < len(body):
        assert body[i] == '{', '数组里出现了非行文本：%r' % body[i:i + 40]
        j = match_brace(body, i)
        blocks.append(body[i:j + 1])
        k = j + 1
        while k < len(body) and body[k] != '{':
            k += 1
        seps.append(body[j + 1:k])
        i = k
    assert len(seps) == len(blocks)
    return head, blocks, seps, body_start, arr_end


def emit(head, blocks, seps):
    assert len(blocks) == len(seps)
    return head + ''.join(b + s for b, s in zip(blocks, seps))


path = sys.argv[1]
write = '--write' in sys.argv
check = '--check' in sys.argv
raw = io.open(path, 'rb').read()
bom = raw.startswith(b'\xef\xbb\xbf')
text = io.open(path, encoding='utf-8-sig', newline='').read()      # newline='' ⇒ CRLF 原样保留
assert '\r\n' in text, '期望 CRLF'

head, blocks, seps, body_start, arr_end = split_outputs(text)
rows = []
for k, blk in enumerate(blocks):
    obj = json.loads(blk)
    rows.append({'i': k, 'name': obj['parameter'], 'expr': obj.get('expression') or ''})
print('  outputs 行数 %d' % len(rows))

if check:
    same = emit(head, blocks, seps) == text[body_start:arr_end]
    print('  自检（按原序重发 == 原文件）: %s' % ('逐字节相同 ✓' if same else '✗ 不同 —— 别用这份工具'))
    sys.exit(0 if same else 1)

order = sorted(range(len(rows)), key=lambda k: (group_of(rows[k]['name'])[0], k))

table, bad = set(), []
for k in order:
    r = rows[k]
    for ref in set(re.findall(r'out\("([^"]+)"\)', r['expr'])):
        if ref not in table:
            bad.append((r['name'], ref))
    table.add(r['name'])
if bad:
    print('  ✗ 新序里有 %d 处依赖断了（out() 只能朝上读）：' % len(bad))
    for name, ref in bad:
        print('      %s 读 %s' % (name, ref))
    sys.exit(1)
print('  依赖校验：%d 行的 out() 全部指向上面的行 ✓' % len(rows))

counts = {}
for k in order:
    label = group_of(rows[k]['name'])[1]
    counts[label] = counts.get(label, 0) + 1
cur, shown = None, 0
for k in order:
    label = group_of(rows[k]['name'])[1]
    if label != cur:
        print('  %s（%d 行）' % (label, counts[label]))
        cur, shown = label, 0
    if shown < 6 or shown >= counts[label] - 2:
        print('      #%-4d %s' % (rows[k]['i'], rows[k]['name']))
    elif shown == 6:
        print('      …')
    shown += 1

if write:
    out = text[:body_start] + emit(head, [blocks[k] for k in order], seps) + text[arr_end:]
    json.loads(out)
    io.open(path, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + out.encode('utf-8'))
    print('  写出 %s（BOM=%s，CRLF 保留）' % (path, bom))
else:
    print('  （干跑；加 --write 落盘）')
