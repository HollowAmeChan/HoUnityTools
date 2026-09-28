# probe-arkit-keys.py -- 生成 ARKit 控制器前的数据核对（只读）
#   · 键 = ARKit 直通配置里的输出行，去掉 Head/*（头交给 LookAt/IK）
#   · 每个键要有成对的 potato_build__BS__On|Off__<键> 片段，且都带 .meta（能取到 GUID）
import io
import json
import os
import re

PROFILE = 'Editor/FaceTracking/Profiles/ho-iPhoneVTS.arkit.hoface.json'
ANIM = 'D:/Unity_Project/BREAK_URP/Assets/Hollow/土豆/FT/Animations'

data = json.loads(io.open(PROFILE, encoding='utf-8-sig').read())
keys = [r['parameter'] for r in data['outputs']
        if r.get('parameter') and not r['parameter'].startswith('Head/')]
keys = sorted(set(keys))

clip = {}
for f in os.listdir(ANIM):
    if not f.endswith('.anim'):
        continue
    m = re.match(r'^potato_build__BS__(On|Off)__(.+)\.anim$', f)
    if m:
        clip.setdefault(m.group(2), {})[m.group(1)] = f

paired, missing, head_key = [], [], []
for k in keys:
    d = clip.get(k)
    if d and 'On' in d and 'Off' in d:
        paired.append(k)
    else:
        missing.append(k + ('（只有 ' + '/'.join(sorted(d)) + '）' if d else '（没有片段）'))

print('候选键（配置里非 Head 的输出）：%d' % len(keys))
print('片段成对、可生成              ：%d' % len(paired))
print('缺片段                        ：%d %s' % (len(missing), ('→ ' + ', '.join(missing[:8]) + ('…' if len(missing) > 8 else '')) if missing else ''))
print()
print('可生成的键（%d）：' % len(paired))
for i in range(0, len(paired), 6):
    print('  ' + '  '.join(paired[i:i + 6]))
print()
print('片段库里但配置里没点名的键（供参考）：%s' % ', '.join(sorted(set(clip) - set(keys))[:10]))
