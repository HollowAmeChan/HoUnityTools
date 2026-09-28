# isolate-trees.py -- 从一份控制器**剪出任意树集合**的隔离版（联合测试用：区域 Direct 的孩子按名字保留/砍掉）。
#
# 与归档的 `isolate-regions.py` 的分工：那份按"一棵子树"剪（一份一个区），这份按**你点名的孩子**剪
#   ⇒ 可以做**联合**测试（例：噘嘴族 + 鼻子一张控制器）。
# 不碰源资产：只读源、写新文件（新 GUID 的 .meta）；被砍掉的子树整块不写进新文件（不留孤儿）。
#
# 用法：
#   python isolate-trees.py <源.controller> <输出.controller> \
#          --root-children MouthRegion,NoseRegion \
#          --children MouthRegion=MouthCore,MouthShift,MouthWidth,InvertedV \
#          --children NoseRegion=NoseUp
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

text = ('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
        + ''.join('--- !u!%d &%s\n%s' % (v['kind'], k, v['body']) for k, v in blocks.items() if k in keep))
a.output.parent.mkdir(parents=True, exist_ok=True)
if a.output.exists():
    raise SystemExit('拒绝覆盖已存在的文件：%s' % a.output)
io.open(a.output, 'w', encoding='utf-8', newline='\n').write(text)      # 与源一样：无 BOM
io.open(str(a.output) + '.meta', 'w', encoding='ascii', newline='\n').write(
    'fileFormatVersion: 2\nguid: %s\n' % uuid.uuid4().hex)
print('  写出 %s（%d 块，sha1 %s）' % (a.output.name, len(keep),
                                  hashlib.sha1(text.encode()).hexdigest()[:12]))
