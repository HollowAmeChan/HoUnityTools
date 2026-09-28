# clip-progress.py -- 槽位片段进度探针：每个 .anim 到底有没有真曲线。
#
# WHY: 检查器只核"槽位摆好没有"（文件在、GUID 对），不核"里面有没有姿势"。验收前需要知道
#      每棵树/每张表画了几格。`.anim` 是 YAML：`m_Curves:` / `m_FloatCurves:` 后面若为空数组就是空片段。
#      另外数一下 m_EditorCurves / m_FloatCurves 里真正的 keyframe 行数（value 行）。
# 用法：python Tools~/FaceTracking/clip-progress.py [AnimationsDir] [--out report.txt]
import io
import os
import re
import sys

d = r'D:\Unity_Project\BREAK_URP\Assets\Hollow\土豆\FT\Animations'
if len(sys.argv) > 1 and not sys.argv[1].startswith('--'):
    d = sys.argv[1]
if '--out' in sys.argv:
    i = sys.argv.index('--out')
    sys.stdout = io.open(sys.argv[i + 1], 'w', encoding='utf-8')

groups = {}
for name in sorted(os.listdir(d)):
    if not name.endswith('.anim'):
        continue
    text = io.open(os.path.join(d, name), encoding='utf-8', errors='replace').read()
    keys = len(re.findall(r'^\s+value:\s', text, re.M))
    curves = 0
    for block in ('m_Curves', 'm_FloatCurves', 'm_PositionCurves', 'm_ScaleCurves', 'm_EulerCurves'):
        m = re.search(r'^  ' + block + r':\s*$', text, re.M)
        if m:
            rest = text[m.end():]
            nxt = re.search(r'^  \S', rest, re.M)
            body = rest[:nxt.start()] if nxt else rest
            if re.search(r'^\s+- curve:', body, re.M):
                curves += len(re.findall(r'^\s+- curve:', body, re.M))
    tree = name.split('__')[0]
    g = groups.setdefault(tree, {'total': 0, 'drawn': 0, 'keys': 0, 'slots': []})
    g['total'] += 1
    if keys > 0 or curves > 0:
        g['drawn'] += 1
        g['keys'] += keys
        g['slots'].append('%s(%d keys)' % (name[:-5], keys))

print('animations dir: %s' % d)
print('%-16s %-9s %s' % ('tree', 'drawn', 'slots with curves'))
tot = totd = 0
for tree in sorted(groups):
    g = groups[tree]
    tot += g['total']; totd += g['drawn']
    print('%-16s %2d/%-2d    %s' % (tree, g['drawn'], g['total'], ', '.join(g['slots']) if g['slots'] else '-'))
print('TOTAL %d/%d slots have curves' % (totd, tot))
