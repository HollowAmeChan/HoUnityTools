# census-table.py -- 解析面板导出的「配置输入行」统计文本（=== 组名 / [Ho 面捕统计] 每次 / 通道 min avg max 波动），
# 按组打成一张通道表。用途：翻老语料回答「某个动作在那根线上到底有多少信号」。
# 用法: python census-table.py <census.txt> [通道名，逗号分隔]
import io
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

path = sys.argv[1]
CH = (sys.argv[2].split(',') if len(sys.argv) > 2 else
      ['jawLeft', 'jawRight', 'mouthLeft', 'mouthRight', 'jawOpen', 'mouthClose',
       'mouthPressLeft', 'mouthPressRight', 'mouthDimpleLeft', 'mouthDimpleRight',
       'mouthSmileLeft', 'mouthSmileRight', 'mouthFrownLeft', 'mouthFrownRight',
       'mouthRollLower', 'mouthStretchLeft', 'mouthPucker'])

ROW = re.compile(r'^(\w+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)(?:\s+(-?[\d.]+))?\s*$')

groups = []          # [(label, [ {ch: (min,avg,max,wave)} , ... ] )]
cur = None
take = None
for line in io.open(path, encoding='utf-8-sig', errors='replace'):
    line = line.rstrip('\r\n')
    if line.startswith('=== '):
        cur = (line[4:].strip(), [])
        groups.append(cur)
        take = None
        continue
    if '[Ho 面捕统计]' in line:
        take = {}
        if cur is not None:
            cur[1].append(take)
        continue
    m = ROW.match(line.strip())
    if m and take is not None:
        ch = m.group(1)
        take[ch] = tuple(float(x) if x else 0.0 for x in m.groups()[1:])


def agg(g, ch, which):
    """组内所有 take 的那个统计量的最大/均值：which 0=min 1=avg 2=max 3=波动"""
    vals = [t[ch][which] for _, takes in [g] for t in takes if ch in t]
    return (max(vals), sum(vals) / len(vals)) if vals else (float('nan'), float('nan'))


print('组（take 数）  ' + '  '.join('%-11s' % c[:11] for c in CH))
for label, takes in groups:
    cells = []
    for ch in CH:
        mx, av = agg((label, takes), ch, 2)
        cells.append('%5.3f/%5.3f' % (av, mx))
    print('%-14s(%2d) %s' % (label[:14], len(takes), '  '.join('%-11s' % c for c in cells)))
print()
print('每格 = 组内平均 / 组内最大（对每个 take 先取它自己的 avg 与 max，再在组内平均/取最大）')

print()
print('=== 下颌左右：逐 take 的 jawLeft/jawRight 与状态 ===')
for label, takes in groups:
    for i, t in enumerate(takes):
        if 'jawRight' not in t and 'jawLeft' not in t:
            continue
        jr = t.get('jawRight', (0, 0, 0, 0))
        jl = t.get('jawLeft', (0, 0, 0, 0))
        print('%-14s #%d  jawRight avg/max %5.3f/%5.3f   jawLeft avg/max %5.3f/%5.3f   差(max) %+5.3f | jawOpen avg %5.3f  mouthClose avg %5.3f' % (
            label[:14], i + 1, jr[1], jr[2], jl[1], jl[2], jr[2] - jl[2],
            t.get('jawOpen', (0, 0, 0, 0))[1], t.get('mouthClose', (0, 0, 0, 0))[1]))
