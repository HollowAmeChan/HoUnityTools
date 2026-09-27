# census-matrix.py -- compact channel matrix out of a residual-channel-census markdown.
#
# usage: python census-matrix.py <census.md> [group-substring ...]
# Each group section lists "| `channel` | min ~ max |"; we print the midpoint so groups can be
# compared side by side without reading hundreds of lines.
import io
import re
import sys

CHANNELS = [
    'mouthDimpleLeft', 'mouthDimpleRight', 'mouthFrownLeft', 'mouthFrownRight',
    'mouthRollLower', 'mouthRollUpper', 'mouthPressLeft', 'mouthPressRight',
    'mouthShrugLower', 'mouthShrugUpper', 'mouthSmileLeft', 'mouthSmileRight',
    'mouthStretchLeft', 'mouthStretchRight', 'mouthLowerDownLeft', 'mouthLowerDownRight',
    'mouthUpperUpLeft', 'mouthPucker', 'mouthFunnel', 'mouthClose', 'jawOpen',
    'cheekPuff', 'noseSneerLeft', 'mouthLeft', 'mouthRight',
]

path = sys.argv[1]
wanted = sys.argv[2:]
text = io.open(path, encoding='utf-8-sig').read()
groups = {}
order = []
cur = None
for line in text.split('\n'):
    m = re.match(r'^##\s+(.+?)\s*$', line)
    if m:
        cur = m.group(1)
        groups[cur] = {}
        order.append(cur)
        continue
    m = re.match(r'^\|\s*`([^`]+)`\s*\|\s*([-\d.]+)\s*~\s*([-\d.]+)\s*\|', line)
    if m and cur:
        groups[cur][m.group(1)] = (float(m.group(2)) + float(m.group(3))) / 2.0

sel = [g for g in order if (not wanted or any(w in g for w in wanted))]
short = {
    'mouthDimpleLeft': 'dimple', 'mouthDimpleRight': 'dimple',
    'mouthFrownLeft': 'frown', 'mouthFrownRight': 'frown',
    'mouthRollLower': 'rollLow', 'mouthRollUpper': 'rollUp',
    'mouthPressLeft': 'press', 'mouthPressRight': 'press',
    'mouthShrugLower': 'shrugLow', 'mouthShrugUpper': 'shrugUp',
    'mouthSmileLeft': 'smile', 'mouthSmileRight': 'smile',
    'mouthStretchLeft': 'stretch', 'mouthStretchRight': 'stretch',
    'mouthLowerDownLeft': 'lowerDn', 'mouthLowerDownRight': 'lowerDn',
    'mouthUpperUpLeft': 'upperUp', 'mouthPucker': 'pucker', 'mouthFunnel': 'funnel',
    'mouthClose': 'close', 'jawOpen': 'jawOpen', 'cheekPuff': 'cheekPuff',
    'noseSneerLeft': 'sneer', 'mouthLeft': 'mouthL', 'mouthRight': 'mouthR',
}
cols = []
for ch in CHANNELS:
    if short[ch] not in cols:
        cols.append(short[ch])
pairs = {}  # short name -> (left channel, right channel)
for ch in CHANNELS:
    pairs.setdefault(short[ch], []).append(ch)

lines = ['group'.ljust(12) + ''.join(c.rjust(9) for c in cols)]
for g in sel:
    row = []
    for c in cols:
        vals = [groups[g][ch] for ch in pairs[c] if ch in groups[g]]
        row.append('%.3f' % (sum(vals) / len(vals)) if vals else '-')
    # pad the (possibly wide) group name on its display width so the columns stay aligned
    pad = 12 - sum(2 if ord(ch) > 0x2000 else 1 for ch in g)
    lines.append(g + ' ' * max(1, pad) + ''.join(v.rjust(9) for v in row))

out = path + '.matrix.txt'
io.open(out, 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
print('matrix written: ' + out + '  (%d groups)' % len(sel))
