# tree-dump.py -- 从 .controller 里把某一棵 BlendTree 的结构打出来（子节点：片段名 / 坐标 / 阈值 / 权重参数）
# 用法: python tree-dump.py <x.controller> [树名子串]
import io
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

path = sys.argv[1]
want = sys.argv[2] if len(sys.argv) > 2 else None

text = io.open(path, encoding='utf-8-sig', errors='replace').read()
chunks = re.split(r'(?m)^--- !u!', text)
docs = {}
order = []
for c in chunks:
    m = re.match(r'^(-?\d+) &(-?\d+)', c)
    if m:
        docs[m.group(2)] = c
        order.append(m.group(2))


def field(t, name):
    m = re.search(r'(?m)^\s*%s:\s*(.*)$' % name, t)
    if not m:
        return None
    v = m.group(1).strip()
    if len(v) >= 2 and v.startswith('"') and v.endswith('"'):
        v = v[1:-1]
        v = re.sub(r'\\u([0-9A-Fa-f]{4})', lambda mm: chr(int(mm.group(1), 16)), v)
    return v


print('blocks=%d' % len(docs))
for fid in order:
    t = docs[fid]
    if 'BlendTree:' not in t:
        continue
    name = field(t, 'm_Name') or ''
    if want and want not in name:
        continue
    bt = re.search(r'(?m)^\s*m_BlendType:\s*(\d+)', t)
    kinds = {'0': 'Simple1D', '1': 'SimpleDirectional2D', '2': 'FreeformDirectional2D',
             '3': 'FreeformCartesian2D', '4': 'Direct'}
    print('')
    print('=== %s   [%s]  &%s' % (name, kinds.get(bt.group(1) if bt else '?', '?'), fid))
    for key in ('m_BlendParameter', 'm_BlendParameterY', 'm_UseAutomaticThresholds',
                'm_MinThreshold', 'm_MaxThreshold', 'm_DirectBlendParameter'):
        v = field(t, key)
        if v is not None:
            print('    %-26s %s' % (key, v))
    kids = re.findall(r'(?ms)^  - serializedVersion:.*?(?=^  - serializedVersion:|^  m_BlendParameter|\Z)', t)
    if not kids:
        kids = re.findall(r'(?ms)^  - serializedVersion:.*?(?=\n  \w|\Z)', t)
    for i, k in enumerate(kids):
        motion = re.search(r'm_Motion: \{fileID: (-?\d+)(?:, guid: ([0-9a-f]+))?', k)
        pos = re.search(r'm_Position: \{x: ([-\d.e]+), y: ([-\d.e]+)\}', k)
        thr = re.search(r'm_Threshold: ([-\d.e]+)', k)
        direct = field(k, 'm_DirectBlendParameter')
        clip = '<空 Motion>'
        if motion:
            mid, guid = motion.group(1), motion.group(2)
            if mid in docs:
                clip = field(docs[mid], 'm_Name') or ('&' + mid)
            elif guid:
                clip = 'guid:' + guid[:8]
            else:
                clip = '&' + mid
        bits = []
        if thr:
            bits.append('thr=%s' % thr.group(1))
        if pos:
            bits.append('pos=(%s, %s)' % (pos.group(1), pos.group(2)))
        if direct:
            bits.append('direct=%s' % direct)
        print('    [%2d] %-28s %s' % (i, clip, '  '.join(bits)))
