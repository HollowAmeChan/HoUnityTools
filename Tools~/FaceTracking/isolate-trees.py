# isolate-trees.py -- 从一份控制器**剪出任意树集合**的隔离版（联合测试用：区域 Direct 的孩子按名字保留/砍掉）。
#
# 与归档的 `isolate-regions.py` 的分工：那份按"一棵子树"剪（一份一个区），这份按**你点名的孩子**剪
#   ⇒ 可以做**联合**测试（例：噘嘴族 + 鼻子一张控制器）。
# 不碰源资产：只读源、写新文件（新 GUID 的 .meta）；被砍掉的子树整块不写进新文件（不留孤儿）。
#
# 两种剪法：
#   A) 单源剪：从一份控制器里剪出点名的孩子集合（下面 -A 用法）。
#   B) 拼权威件（--base）：骨架用**已人工验过的隔离版**，只把源里缺的那几棵并进来
#      ⇒ 联合控制器里"验过的那部分"与权威件**逐字节相同**，不会被源里的旧值顶掉；
#         同 ID 但内容不同 = 按权威件为准并打警告（这就是"别用旧的点位"的机器化说法）。
#      拼完通常要 --state-motion <根树> 把状态机从单区树改指到根树。
#
# 用法：
#   A) python isolate-trees.py <源.controller> <输出.controller> \
#          --root-children MouthRegion,NoseRegion \
#          --children MouthRegion=MouthShift,MouthWidth,InvertedV \
#          --children NoseRegion=NoseUp
#   B) python isolate-trees.py <源.controller> <输出.controller> \
#          --base <权威隔离.controller> --state-motion "Ho/00 Drive Tree" \
#          --root-children MouthRegion,NoseRegion \
#          --children MouthRegion=MouthShift,MouthWidth,InvertedV \
#          --children NoseRegion=NoseUp
#      （--base 只提供骨架与权威值；白名单/剪枝仍作用在源上。--guid 可沿用旧资产 GUID。）
import argparse
import hashlib
import io
import json
import re
import sys
import uuid
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

ap = argparse.ArgumentParser()
ap.add_argument('source', type=Path)
ap.add_argument('output', type=Path)
ap.add_argument('--root-children', required=True, help='根 Direct 树保留哪些孩子（逗号分隔的树名）')
ap.add_argument('--children', action='append', default=[],
                help='某个 Direct 树的孩子白名单：<树名>=<孩子名>,<孩子名>（可重复）')
ap.add_argument('--base', type=Path,
                help='权威隔离版：拿它当骨架拼（同 ID 冲突以它为准）；不传＝单源剪')
ap.add_argument('--state-motion',
                help='把 AnimatorState 的 m_Motion 指到这棵树名（拼完把状态机从单区树改到根树）')
ap.add_argument('--guid', help='输出的 .meta GUID（32 位十六进制；默认随机。沿用旧值可保住 Unity 里的引用）')
a = ap.parse_args()


def parse(text):
    """整份 .controller → {id: {kind, name, body}}（body 含 '%YAML' 之外的那一段）"""
    b = {}
    for m in re.finditer(r'(?ms)^--- !u!(\d+) &(-?\d+)\n(.*?)(?=^--- !u!|\Z)', text):
        nm = re.search(r'(?m)^  m_Name: (.*)$', m.group(3))
        v = nm.group(1) if nm else ''
        if v.startswith('"'):
            v = json.loads(v)
        b[m.group(2)] = {'kind': int(m.group(1)), 'name': v, 'body': m.group(3)}
    return b


def children_of(body):
    """把 m_Childs 段切成 [(整段文本, motion fileID 字符串)]，其余原样保留"""
    m = re.search(r'(?ms)^(  m_Childs:\n)(.*?)(?=^  m_[A-Za-z]|\Z)', body)
    if not m:
        return None
    entries = re.findall(r'(?ms)^  - serializedVersion:.*?(?=^  - serializedVersion:|\Z)', m.group(2))
    out = []
    for e in entries:
        mid = re.search(r'm_Motion: \{fileID: (-?\d+)', e)
        out.append((e, mid.group(1) if mid else None))
    return m, out


blocks = parse(io.open(a.source, encoding='utf-8-sig', errors='replace').read())
byname = {v['name']: k for k, v in blocks.items() if v['kind'] == 206}
assert byname, '源文件里没解析到 BlendTree'

wanted_children = {}
for spec in a.children:
    tree, kids = spec.split('=', 1)
    wanted_children[tree] = [x.strip() for x in kids.split(',') if x.strip()]

# ── 1) 按白名单过滤 Direct 树的孩子（只动 Direct；2D 表的格子不动）
for tree, kids in wanted_children.items():
    assert tree in byname, '找不到树：%s' % tree
    tid = byname[tree]
    m, entries = children_of(blocks[tid]['body'])
    assert m, '树 %s 里找不到 m_Childs' % tree
    keep_ids = {byname[k] for k in kids if k in byname}
    missing = [k for k in kids if k not in byname]
    assert not missing, '树 %s 指定的孩子不存在：%s' % (tree, missing)
    kept = [e for e, mid in entries if mid in keep_ids]
    dropped = [blocks[mid]['name'] for e, mid in entries if mid not in keep_ids]
    new_seg = '  m_Childs:\n' + ''.join(kept)
    blocks[tid]['body'] = blocks[tid]['body'][:m.start()] + new_seg + blocks[tid]['body'][m.end():]
    print('  剪 %-14s 留 %-46s 砍 %s' % (tree, ','.join(kids), ','.join(dropped) or '（无）'))

# ── 2) 根的孩子白名单
root = 'Ho/00 Drive Tree'
assert root in byname, '找不到根树 %s' % root
m, entries = children_of(blocks[byname[root]]['body'])
keep_ids = {byname[k] for k in a.root_children.split(',') if k.strip() and k.strip() in byname}
kept = [e for e, mid in entries if mid in keep_ids]
dropped = [blocks[mid]['name'] for e, mid in entries if mid not in keep_ids]
blocks[byname[root]]['body'] = (blocks[byname[root]]['body'][:m.start()] + '  m_Childs:\n' + ''.join(kept)
                                + blocks[byname[root]]['body'][m.end():])
print('  剪 %-14s 留 %-46s 砍 %s' % (root, a.root_children, ','.join(dropped) or '（无）'))

# ── 3) keep = 所有非 BlendTree 块（状态机/状态/片段）+ 从根可达的 BlendTree
keep = {k for k, v in blocks.items() if v['kind'] != 206}
queue = [byname[root]]
keep.add(byname[root])
while queue:
    cur = queue.pop()
    m, entries = children_of(blocks[cur]['body']) or (None, [])
    for e, mid in entries:
        if mid and mid in blocks and blocks[mid]['kind'] == 206 and mid not in keep:
            keep.add(mid)
            queue.append(mid)
out_trees = [blocks[k]['name'] for k in keep if blocks[k]['kind'] == 206]
print('  保留 BlendTree %d 棵：%s' % (len(out_trees), ', '.join(sorted(out_trees))))

# ── 4) 可选：与权威隔离版拼（骨架＝权威件；同 ID 不同内容时权威件赢，并打警告）
if a.base:
    base = parse(io.open(a.base, encoding='utf-8-sig', errors='replace').read())
    merged = dict(base)
    same, conflict, added = [], [], []
    for k in keep:
        if k not in merged:
            merged[k] = blocks[k]
            if blocks[k]['kind'] == 206:
                added.append(blocks[k]['name'])
        elif merged[k]['body'] == blocks[k]['body']:
            same.append(blocks[k]['name'])
        else:
            conflict.append((blocks[k]['name'], k))
    print('  拼骨架 %s：权威件 %d 块；源新增 %d 棵：%s'
          % (a.base.name, len(base), len(added), ', '.join(sorted(added)) or '（无）'))
    if same:
        print('  两边都有且逐字节相同（留权威件）：%s' % ', '.join(sorted(same)))
    if conflict:
        print('  ⚠ 同 ID 内容不同，已按权威件为准（源里那份是旧的）：%s'
              % ', '.join('%s &%s' % c for c in sorted(conflict)))
    blocks = merged
    if a.state_motion:
        tgt = {v['name']: k for k, v in blocks.items() if v['kind'] == 206}.get(a.state_motion)
        assert tgt, '拼完找不到树：%s' % a.state_motion
        n = 0
        for v in blocks.values():
            if v['kind'] != 1102:          # 1102 = AnimatorState
                continue
            v['body'], c = re.subn(r'(m_Motion: \{fileID: )-?\d+',
                                   lambda m: m.group(1) + tgt, v['body'])
            n += c
        assert n, '--state-motion 一处也没改到，检查状态块'
        print('  状态机 m_Motion → %s &%s（%d 个状态）' % (a.state_motion, tgt, n))
    keep = set(blocks)
    final_trees = sorted(v['name'] for v in blocks.values() if v['kind'] == 206)
    print('  拼完 BlendTree %d 棵：%s' % (len(final_trees), ', '.join(final_trees)))

order = sorted(keep, key=lambda x: int(x))       # Unity 自己也是按 ID 升序写块

# ── 5) 控制器块的名字对齐到文件名（复制品别顶着源资产或骨架的名字）
for v in blocks.values():
    if v['kind'] == 91:                          # 91 = AnimatorController
        v['body'], c = re.subn(r'(?m)^  m_Name: .*$', '  m_Name: %s' % a.output.stem,
                               v['body'], count=1)
        assert c == 1, '控制器块里没找到 m_Name'
        print('  控制器名 → %s（与文件名一致）' % a.output.stem)
        break

text = ('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
        + ''.join('--- !u!%d &%s\n%s' % (blocks[k]['kind'], k, blocks[k]['body']) for k in order))
a.output.parent.mkdir(parents=True, exist_ok=True)
if a.output.exists():
    raise SystemExit('拒绝覆盖已存在的文件：%s' % a.output)
guid = a.guid or uuid.uuid4().hex
assert re.fullmatch(r'[0-9a-f]{32}', guid), '--guid 要是 32 位小写十六进制'
io.open(a.output, 'w', encoding='utf-8', newline='\n').write(text)      # 与源一样：无 BOM
io.open(str(a.output) + '.meta', 'w', encoding='ascii', newline='\n').write(
    'fileFormatVersion: 2\nguid: %s\n' % guid)
print('  写出 %s（%d 块，sha1 %s）' % (a.output.name, len(order),
                                  hashlib.sha1(text.encode()).hexdigest()[:12]))
