# mouth-audit.py -- 嘴系统的机械审计（只读）：规则/耦合/顺序/计数。
#
# 报四类：
#   ① 修饰符顺序（steps 应在 smooth 之前 —— "先过阈值、再平滑"，反过来语义不同）
#   ② 同名输出行（"写自己 / 关其他"是**故意**的：靠行序后写覆盖；这里把每一组列出来核对顺序）
#   ③ 各行引用的行是否存在、是否排在它之前（out() 只能读上面最近写过的那一行）
#   ④ 数值健全性：曲线 key 必须按 t 升序、v 无 NaN；steps 的 threshold 必须 < trigger（否则释放点 > 触发点）；
#      gate 行的 defaultValue 应为 1、普通轴应为 0
# 用法：python Tools~/FaceTracking/mouth-audit.py [profile.json]
import io
import json
import sys

# PowerShell 5.1 的 `>` 会把 stdout 写成 UTF-16（读不了）⇒ 需要落盘时用 --out，自己写 UTF-8。
if '--out' in sys.argv:
    _i = sys.argv.index('--out')
    sys.stdout = io.open(sys.argv[_i + 1], 'w', encoding='utf-8')
    del sys.argv[_i:_i + 2]

path = sys.argv[1] if len(sys.argv) > 1 else 'Editor/FaceTracking/Profiles/ho-iPhoneVTS.hoface.json'
d = json.loads(io.open(path, encoding='utf-8-sig').read())
outs = d['outputs']
inputs = {r['parameter'] for r in d.get('inputs', [])}


def kind(row):
    mods = [m.get('kind') for m in (row.get('modifiers') or [])]
    return mods


print('rows=%d  inputs=%d' % (len(outs), len(inputs)))

# ① 修饰符顺序
bad_order = []
for i, r in enumerate(outs):
    k = kind(r)
    if 'steps' in k and 'smooth' in k and k.index('steps') > k.index('smooth'):
        bad_order.append((i, r['parameter'], k))
print('\n[1] 修饰符顺序 steps 在 smooth 之后（应为 steps -> smooth）: %d' % len(bad_order))
for b in bad_order:
    print('    row %d  %s  %s' % b)

# ② 同名行（后写覆盖）
from collections import OrderedDict
groups = OrderedDict()
for i, r in enumerate(outs):
    groups.setdefault(r['parameter'], []).append(i)
dups = {k: v for k, v in groups.items() if len(v) > 1}
print('\n[2] 同名输出行组数 = %d（"写自己/关其他"的机制；顺序 = 数组顺序）' % len(dups))
for k, idx in dups.items():
    exprs = []
    for i in idx:
        e = (outs[i].get('expression') or '').replace('out("' + k + '")', 'out(自己)')
        exprs.append('#%d %s' % (i, e[:70]))
    print('    %-28s %s' % (k, ' | '.join(exprs)))

# ③ out() 引用顺序
missing = []
later = []
last_write = {}
for i, r in enumerate(outs):
    e = r.get('expression') or ''
    j = 0
    while True:
        p = e.find('out("', j)
        if p < 0:
            break
        q = e.find('")', p)
        name = e[p + 5:q]
        if name not in groups:
            missing.append((i, r['parameter'], name))
        elif last_write.get(name, -1) < 0:
            later.append((i, r['parameter'], name))
        j = q + 2
    last_write[r['parameter']] = i
print('\n[3] out() 引用了不存在的行: %d   out() 引用了一行尚未出现过的行: %d' % (len(missing), len(later)))
for m in missing + later:
    print('    row %d  %s -> out("%s")' % m)

# ④ 数值健全性
curve_bad = []
step_bad = []
gate_default_bad = []
for i, r in enumerate(outs):
    keys = ((r.get('curve') or {}).get('keys') or [])
    ts = [k.get('t') for k in keys]
    if ts != sorted(ts):
        curve_bad.append((i, r['parameter'], 't 未升序 ' + str(ts)))
    for k in keys:
        for f in ('t', 'v', 'inT', 'outT'):
            v = k.get(f)
            if isinstance(v, float) and (v != v):
                curve_bad.append((i, r['parameter'], f + ' = NaN'))
    for m in (r.get('modifiers') or []):
        for s in (m.get('steps') or []):
            if abs(s.get('threshold', 0)) >= s.get('trigger', 0):
                step_bad.append((i, r['parameter'], 'trigger %s / threshold %s' % (s.get('trigger'), s.get('threshold'))))
    if r['parameter'].startswith('Ho/Drive/Gate/') and r.get('defaultValue') != 1:
        gate_default_bad.append((i, r['parameter'], r.get('defaultValue')))
print('\n[4] 曲线异常: %d  steps 释放点 >= 触发点: %d  Gate 行 defaultValue != 1: %d'
      % (len(curve_bad), len(step_bad), len(gate_default_bad)))
for c in curve_bad + step_bad + gate_default_bad:
    print('    row %d  %s  %s' % c)

# ⑤ 嘴的账（只看 Ho/Drive/Mouth/* 与 Cheek/Style 里嘴那几根）
mouth = [r for r in outs if r.get('parameter', '').startswith('Ho/Drive/Mouth/')]
print('\n[5] Ho/Drive/Mouth/* 轴行 %d 根:' % len(mouth))
for r in mouth:
    print('    %-30s smooth=%s' % (r['parameter'], [m.get('seconds') for m in (r.get('modifiers') or []) if m.get('kind') == 'smooth']))
