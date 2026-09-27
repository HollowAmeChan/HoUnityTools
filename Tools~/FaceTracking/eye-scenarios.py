# eye-scenarios.py -- 把「静止 / 眯眼 / 眯眼笑」实测均值写成 chain-probe 的 wire dump，
# 并用**表达式原文的直译**先算出期望值（再去和真实链的输出对答案）。
# 另加两个**闭眼假设**场景，用来看去污系数在闭眼端的敏感度（闭眼还没录 ⇒ 这是唯一没实测的一格）。
import io

# 静止 / 眯眼 / 眯眼笑 = 从 takes-eyes.raw.txt 的 6 组均值取的对称化（左右各自保留差异）
SCEN = {
    'rest': dict(blinkL=0.146, blinkR=0.146, squintL=0.021, squintR=0.021,
                 smileL=0.082, smileR=0.120, dimpleL=0.139, dimpleR=0.139,
                 stretchL=0.078, stretchR=0.075, jawOpen=0.020),
    'squint': dict(blinkL=0.348, blinkR=0.348, squintL=0.078, squintR=0.078,
                   smileL=0.192, smileR=0.265, dimpleL=0.087, dimpleR=0.087,
                   stretchL=0.091, stretchR=0.087, jawOpen=0.022),
    'smile': dict(blinkL=0.477, blinkR=0.478, squintL=0.103, squintR=0.103,
                  smileL=0.670, smileR=0.692, dimpleL=0.214, dimpleR=0.214,
                  stretchL=0.107, stretchR=0.104, jawOpen=0.022),
    # 假设：苦脸（嘴的判据给负 ⇒ 眼睑 Form 应该跟着负）
    'sad': dict(blinkL=0.16, blinkR=0.16, squintL=0.025, squintR=0.025,
                smileL=0.0, smileR=0.0, dimpleL=0.0, dimpleR=0.0,
                stretchL=0.20, stretchR=0.20, jawOpen=0.03, frownL=0.30, frownR=0.30),
    # 假设：怒（压眉）。browDown 用实测带：静止 0.10~0.25、压眉样 0.90~0.95
    'anger': dict(blinkL=0.16, blinkR=0.16, squintL=0.03, squintR=0.03,
                  smileL=0.10, smileR=0.12, dimpleL=0.14, dimpleR=0.14,
                  stretchL=0.08, stretchR=0.08, jawOpen=0.02, browDownL=0.93, browDownR=0.93),
    # 不可达组合探针：笑 + 睁大（喜证据应被"不许睁大"的门掐成 0）
    'wide_happy': dict(blinkL=0.15, blinkR=0.15, squintL=0.10, squintR=0.10,
                       smileL=0.67, smileR=0.69, dimpleL=0.21, dimpleR=0.21,
                       stretchL=0.10, stretchR=0.10, jawOpen=0.02, wideL=0.6, wideR=0.6),
    # 假设：硬闭眼。squint 抬多少**没实测** ⇒ 两个极端都算出来看敏感度。
    'blink_flat': dict(blinkL=1.0, blinkR=1.0, squintL=0.05, squintR=0.05,
                       smileL=0.10, smileR=0.12, dimpleL=0.14, dimpleR=0.14,
                       stretchL=0.078, stretchR=0.075, jawOpen=0.020),
    'blink_squinty': dict(blinkL=1.0, blinkR=1.0, squintL=0.30, squintR=0.30,
                          smileL=0.10, smileR=0.12, dimpleL=0.14, dimpleR=0.14,
                          stretchL=0.078, stretchR=0.075, jawOpen=0.020),
}

K = 3.9
OFF = 0.06


def clamp(x, a, b):
    return max(a, min(b, x))


def mouth_form(s):
    smile = (s['smileL'] + s['smileR']) / 2
    dimple = (s['dimpleL'] + s['dimpleR']) / 2
    stretch = (s['stretchL'] + s['stretchR']) / 2
    sad = max((s.get('frownL', 0.0) + s.get('frownR', 0.0)) / 2,
              clamp((stretch - (0.42 * s['jawOpen'] + 0.05)) * 1.5, 0, 1))
    return ((s['smileR'] + s['smileL'] + (s['dimpleL'] + s['dimpleR']) / 2) - 2 * sad) / 2


def blink_wide(s, side):
    l = 'L' if side == 'Left' else 'R'
    return clamp(clamp(s['blink' + l] - K * s['squint' + l] - OFF, 0, 1) - 0.0, -1, 1)


def form(s, side):
    l = 'L' if side == 'Left' else 'R'
    x = blink_wide(s, side)
    return clamp(clamp((s['squint' + l] - 0.05) / 0.05, 0, 1) * clamp((0.50 - x) / 0.25, 0, 1)
                 - clamp(-mouth_form(s), 0, 1), -1, 1)


for name, s in SCEN.items():
    lines = ['FaceFound = 1', 'Ho/Drive/Gate/Mouth = 1', 'Ho/Drive/Gate/Eye = 1',
             'Ho/Drive/Gate/Brow = 1', 'Ho/Drive/Gate/Nose = 1']
    for k, v in (('eyeBlinkLeft', s['blinkL']), ('eyeBlinkRight', s['blinkR']),
                 ('eyeSquintLeft', s['squintL']), ('eyeSquintRight', s['squintR']),
                 ('eyeWideLeft', s.get('wideL', 0.0)), ('eyeWideRight', s.get('wideR', 0.0)),
                 ('browDownLeft', s.get('browDownL', 0.05)), ('browDownRight', s.get('browDownR', 0.05)),
                 ('mouthSmileLeft', s['smileL']), ('mouthSmileRight', s['smileR']),
                 ('mouthDimpleLeft', s['dimpleL']), ('mouthDimpleRight', s['dimpleR']),
                 ('mouthStretchLeft', s['stretchL']), ('mouthStretchRight', s['stretchR']),
                 ('mouthFrownLeft', s.get('frownL', 0.0)), ('mouthFrownRight', s.get('frownR', 0.0)),
                 ('jawOpen', s['jawOpen']), ('cheekSquintLeft', 0.05),
                 ('cheekSquintRight', 0.05), ('browInnerUp', 0.05),
                 ('HoExternalEyeSync', 0.0)):
        lines.append('%s = %s' % (k, v))
        # 同一个通道的 PascalCase 拼法也写一份：profile 里输入行的线名两种拼法都有
        # （chain-probe 的改名记录 `BrowInnerUp -> browInnerUp` 就是证据），ReadDump 按字面存键，
        # 链查哪个拼法就读到哪个。
        lines.append('%s%s = %s' % (k[0].upper(), k[1:], v))
    path = '.research/eye-scen-%s.txt' % name
    io.open(path, 'w', encoding='ascii', newline='\r\n').write('\n'.join(lines) + '\n')
    mf = mouth_form(s)
    print('%-14s Mouth/Form %+0.3f   X(左) %+0.3f  X(右) %+0.3f   Form(左) %+0.3f  Form(右) %+0.3f'
          % (name, mf, blink_wide(s, 'Left'), blink_wide(s, 'Right'), form(s, 'Left'), form(s, 'Right')))
    print('               -> %s' % path)
