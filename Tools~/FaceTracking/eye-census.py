# eye-census.py -- 眼睛那批的通道普查（静止 / 眯眼 / 眯眼笑 各 3 段）。
# 段没有标签 ⇒ 按**出现顺序**分组（每 3 段一组，G1=静止 G2=眯眼 G3=眯眼笑）。
# 输出 ASCII 组名，直接打印（避免中文在 PS 控制台里糊掉）。用法：python Tools~/FaceTracking/eye-census.py <takes>
import io
import re
import sys

CH = ['eyeSquintLeft', 'eyeSquintRight', 'eyeBlinkLeft', 'eyeBlinkRight',
      'eyeWideLeft', 'eyeWideRight', 'cheekSquintLeft', 'cheekSquintRight',
      'mouthSmileLeft', 'mouthSmileRight', 'browInnerUp', 'noseSneerLeft',
      'mouthDimpleLeft', 'mouthDimpleRight', 'mouthStretchLeft', 'mouthStretchRight',
      'mouthFrownLeft', 'mouthFrownRight', 'jawOpen', 'mouthPucker', 'mouthRollLower']

path = sys.argv[1]
per = 3
takes = []
cur = None
for raw in io.open(path, encoding='utf-8', errors='replace'):
    line = raw.strip()
    if line.startswith('[Ho 面捕统计]'):
        cur = {}
        takes.append(cur)
        continue
    if cur is None or not line or line.startswith('参数名'):
        continue
    parts = line.split()
    if len(parts) >= 3 and parts[0] in CH:
        try:
            cur[parts[0]] = float(parts[2])
        except ValueError:
            pass

print('takes=%d' % len(takes))
groups = {}
for i, t in enumerate(takes):
    groups.setdefault('G%d' % (i // per + 1), []).append(t)
print('groups=%s' % ', '.join('%s(%d)' % (g, len(v)) for g, v in groups.items()))
print()
for ch in CH:
    line = '%-16s' % ch
    for g in sorted(groups):
        vals = [t[ch] for t in groups[g] if ch in t]
        line += '  %s %s' % (g, ('%.3f~%.3f' % (min(vals), max(vals))) if vals else '-')
    print(line)
print()


def mean(g, ch):
    vals = [t[ch] for t in groups.get(g, []) if ch in t]
    return sum(vals) / len(vals) if vals else None


for g in ('G2', 'G3'):
    for side in ('Left', 'Right'):
        keys = ['eyeSquint' + side, 'eyeBlink' + side, 'eyeWide' + side, 'mouthSmile' + side]
        vals = {k: (mean(g, k), mean('G1', k)) for k in keys}
        if any(v[0] is None or v[1] is None for v in vals.values()):
            continue
        dsq = vals['eyeSquint' + side][0] - vals['eyeSquint' + side][1]
        dbl = vals['eyeBlink' + side][0] - vals['eyeBlink' + side][1]
        dwi = vals['eyeWide' + side][0] - vals['eyeWide' + side][1]
        dsm = vals['mouthSmile' + side][0] - vals['mouthSmile' + side][1]
        k = (dbl / dsq) if abs(dsq) > 1e-6 else float('nan')
        print('%s %-5s: squint %+0.3f  blink %+0.3f  wide %+0.3f  mouthsSmile %+0.3f   => k=dBlink/dSquint %+0.3f'
              % (g, side, dsq, dbl, dwi, dsm, k))
