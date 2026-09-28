# clip-dump.py -- 速查一个 .anim 里到底写了什么：形变键（名字 + 值） / 人形肌肉曲线 / 其它绑定。
# 用途：判断作者画的某个姿势「烘焙了多少张嘴/闭眼」这类问题（例如下巴左右那两格是不是照张满画的）。
# 用法: python clip-dump.py <x.anim> [--top N] [--grep 子串]
import argparse
import io
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

ap = argparse.ArgumentParser()
ap.add_argument('clip')
ap.add_argument('--top', type=int, default=40)
ap.add_argument('--grep')
a = ap.parse_args()

text = io.open(a.clip, encoding='utf-8-sig', errors='replace').read()
name = re.search(r'(?m)^\s*m_Name:\s*(.+?)\s*$', text)
print('=== %s' % a.clip)
print('    m_Name: %s' % (name.group(1) if name else '?'))

# 形变曲线：m_BlendShapeName 或 老格式的 attribute 名；然后是 keyframe 的 value
pairs = []
for blk in re.split(r'(?m)^  - curve:', text)[1:]:
    nm = re.search(r'm_Attribute:\s*(.+?)\s*$', blk, re.M)
    bs = re.search(r'(?m)^\s*m_BlendShapeName:\s*(.+?)\s*$', blk)
    label = (bs.group(1) if bs else (nm.group(1) if nm else '?'))
    vals = [float(v) for v in re.findall(r'(?m)^\s*value:\s*([-\d.eE]+)', blk)]
    if vals:
        pairs.append((label, vals[0], max(vals)))

pairs.sort(key=lambda x: -abs(x[2]))
if a.grep:
    pairs = [p for p in pairs if a.grep in p[0]]
print('    共 %d 条曲线；按幅值排前 %d：' % (len(pairs), a.top))
for label, first, mx in pairs[:a.top]:
    bar = '#' * min(40, int(abs(mx) * 40))
    print('      %-58s %8.4f %8.4f  %s' % (label[:58], first, mx, bar))
