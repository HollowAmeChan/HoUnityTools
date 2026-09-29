# reorder-output-rows.py -- 按「归属分组」重排 profile 的 outputs 行序
#
# 为什么需要它：`out("…")` **只能朝上读**（`HoFaceOutputTable`：行按顺序写进表、后写覆盖先写）
#   ⇒ 行序既是求值顺序也是依赖顺序，不能手工乱挪 —— 这份脚本搬完**逐行验依赖**，断了就报错、不写盘。
#
# 分组口径（2026-09-29 用户定）：
#   出口 90 行 → Ho/Drive 各区域成块（嘴 / 眼睑 / 注视 / 眉 / 颊 / 鼻）→ 区域门 → 切片
#   → **style 整区靠下**，区内按大类成块（每条形态的判定行 + 开关行 + 发布行 + 该形态自己的处理行），
#     最后是三类共享的仲裁行与形态门。
#   ⭐ **块序 = 归属序**：「谁主动产生这条规则，规则就归谁、就写在谁的块里」—— 好处是
#     **想整块删掉某个功能时，它的规则是挨在一起的**。所以 `Ho/Drive/Style/CatMouth` 有两行：
#     猫嘴块的纯转发（⑪）与 V嘴 块里"把它压成 0"的覆盖行（⑫）—— **同名不同归属**，
#     分组因此看整行（名字 + 表达式），不能只看名字。
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
PUCKER = ('Ho/Style/InvertedV', 'Ho/Drive/Style/InvertedV', 'Ho/Style/MouthWidth', 'Ho/Drive/Mouth/Pucker',
          # V嘴 的两条「关别人」规则（归属 V嘴 ⇒ 归到噘嘴块里）：
          #   ① `Ho/Style/MouthCoreGate` = 关 core 块（含猫嘴变体）  ② 压猫嘴开关的同名覆盖行（见 ⑪/⑫）
          'Ho/Style/MouthCoreGate')


def is_pucker(n):
    return n in PUCKER or n.startswith('Ho/Style/InvertedV/') or n.startswith('Ho/Style/MouthWidth/')


def is_cheek(r):
    """归属鼓嘴的**它自己那几行**（读数 / 门 / 判定 / 开关）。
    ⚠️ 2026-09-29 主从翻转之后它不再是"最霸道"的那块：
      · 它的「关别人」（关猫嘴 / 关嘴平移 X Y / 嘴形归 0）读的是**最终**权重 ⇒ 归 ⑭ 作用区；
      · 「关鼓嘴」那条是**噘嘴**的规则（归 ⑬）—— 它名字也叫 `Ho/Style/Cheek`，所以这里要按
        表达式把它排掉：**读 `Ho/Style/InvertedV` 的那条不是鼓嘴自己的行**。"""
    return (r['name'].startswith('Ho/Style/Cheek') and 'Ho/Style/InvertedV' not in r['expr']) \
        or r['name'] == 'Ho/Drive/Style/Cheek'


def is_cheek_closes(r):
    """鼓嘴的「关别人」：读 `Ho/Style/Cheek`（= 被噘嘴压过之后的鼓嘴权重）⇒ 住 ⑭ 作用区。"""
    return 'Ho/Style/Cheek' in r['expr'] and r['name'] in (
        'Ho/Drive/Style/CatMouth', 'Ho/Drive/Mouth/X', 'Ho/Drive/Mouth/Y', 'Ho/Drive/Mouth/Form')


def is_pucker_closes_cheek(r):
    """噘嘴的「关鼓嘴」：名字也是 `Ho/Style/Cheek`（同名链合法），但表达式读 `Ho/Style/InvertedV`。"""
    return r['name'] == 'Ho/Style/Cheek' and 'Ho/Style/InvertedV' in r['expr']


def is_effect(r):
    """**作用区**：读的是**最终的**形态权重 ⇒ 必须排在所有形态块**之后**（谁被压成 0，这里自动跟着灭）。
      · 固定那几个：core 块门、猫嘴宽度门、宽度读数与两行归中、宽度转发；
      · 2026-09-29 **主从翻转**（噘嘴搬到鼓嘴下面）之后又多一组：**鼓嘴的「关别人」** ——
        它们必须排在**噘嘴那条「关鼓嘴」下面**，才吃得到「被 V嘴 压过的鼓嘴」。"""
    if r['name'] in ('Ho/Style/MouthCoreGate', 'Ho/Style/MouthWidth/CatMouthGate',
                     'Ho/Style/MouthWidth/Read', 'Ho/Style/MouthWidth', 'Ho/Drive/Mouth/Pucker'):
        return True
    return is_cheek_closes(r)


def is_forward(r):
    """整条就是一个 `out("…")`（纯转发）⇒ 与"读自己再乘别的"的**同名覆盖行**区分开。"""
    return re.match(r'^\s*out\(\s*"[^"]+"\s*\)\s*$', r['expr']) is not None


GROUPS = [
    ('① 出口（ARKit 52 / VTS 20 / VB 5 / 姿态 12 / 信号 1）', lambda r: not r['name'].startswith('Ho/')),
    # ⚠️ ② 是"先匹配到的赢"，所以必须把**已经归到 style 区那几组**的同名链行排掉
    #    （鼓嘴关的那三根轴 / 宽度转发 …）：留在 ② 就会被排到 style 区**上面** ⇒ 前向引用。
    ('② Ho/Drive/Mouth',        lambda r: r['name'].startswith('Ho/Drive/Mouth/')
        and not is_pucker(r['name']) and not is_cheek(r) and not is_effect(r)),
    ('③ Ho/Drive/Lid',          lambda r: r['name'].startswith('Ho/Drive/Lid/')),
    ('④ Ho/Drive/Gaze',         lambda r: r['name'].startswith('Ho/Drive/Gaze/')),
    ('⑤ Ho/Drive/Brow',         lambda r: r['name'].startswith('Ho/Drive/Brow/')),
    ('⑥ Ho/Drive/Cheek',        lambda r: r['name'].startswith('Ho/Drive/Cheek/')),
    ('⑦ Ho/Drive/Nose',         lambda r: r['name'].startswith('Ho/Drive/Nose/')),
    # ⚠️ 两条**契约行**（形态门 `MouthStyle`、core 块门 `MouthCore`）是 style 区的口，不算区域门 ⇒ 排到 ⑬
    ('⑧ Ho/Drive/Gate（区域门）', lambda r: r['name'].startswith('Ho/Drive/Gate/')
        and r['name'] not in ('Ho/Drive/Gate/MouthStyle', 'Ho/Drive/Gate/MouthCore')),
    ('⑨ Ho/Drive/Slice',        lambda r: r['name'].startswith('Ho/Drive/Slice/')),
    # 猫嘴块：判定 / 开关 / 发布（发布 = 纯转发那一行；V嘴 压它的那条**同名覆盖行**归 ⑬）
    ('⑪ style · CatMouth',      lambda r: r['name'].startswith('Ho/Style/CatMouth')
        or (r['name'] == 'Ho/Drive/Style/CatMouth' and is_forward(r))),
    # ⭐ **2026-09-29 主从翻转**：噘嘴搬到鼓嘴**下面**（用户定：「V嘴关掉鼓嘴，鼓嘴不关V嘴」）——
    #    鼓嘴自己的行在前、噘嘴在后，「关鼓嘴」那条同名链住在噘嘴块末（后写覆盖先写才压得住）。
    ('⑫ style · Cheek（鼓嘴自己的行：读数 / 门 / 判定 / 开关）', is_cheek),
    ('⑬ style · 噘嘴（最霸道：自己的行 + 压猫嘴 + 关鼓嘴）',
     lambda r: (is_pucker(r['name']) and not is_effect(r) and not is_cheek(r))
        or (r['name'] == 'Ho/Drive/Style/CatMouth' and not is_forward(r) and not is_cheek(r)
            and not is_effect(r))
        or is_pucker_closes_cheek(r)),
    # ⭐ **作用区**：读的是**最终的**形态权重 ⇒ 排在所有形态块之后（谁被压成 0，这里自动跟着灭）
    ('⑭ style · 作用区（被形态权重驱动的行 + 宽度转发）', is_effect),
    ('⑮ style · 共享（块门契约行）',
     lambda r: r['name'] in ('Ho/Style/MouthGate', 'Ho/Drive/Gate/MouthStyle',
                             'Ho/Drive/Gate/MouthCore')),
]
FALLBACK = '⓪ 其它（没归进任何组，请补规则）'


def group_of(row):
    for i, (label, pred) in enumerate(GROUPS):
        if pred(row):
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

order = sorted(range(len(rows)), key=lambda k: (group_of(rows[k])[0], k))

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
    label = group_of(rows[k])[1]
    counts[label] = counts.get(label, 0) + 1
cur, shown = None, 0
for k in order:
    label = group_of(rows[k])[1]
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
