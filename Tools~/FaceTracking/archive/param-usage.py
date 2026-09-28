# param-usage.py -- 哪些控制器参数**真的被树消费**了（决定性清单）。
#
# WHY: 检查器只核"声明 == 期望"，不核"有没有树用"。嘴的 review 需要一张权威的
#      "轴 → 消费者" 表：哪些轴只是发布当出口（没有树消费）。做法：读活资产，数每个参数出现在
#      m_BlendParameter / m_BlendParameterY / m_DirectBlendParameter 里几次。
# 用法：python Tools~/FaceTracking/param-usage.py [controllerPath] [--out report.txt]
import io
import re
import sys

path = sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith('--') \
    else r'D:\Unity_Project\BREAK_URP\Assets\Hollow\土豆\FT\PTP_CTR_Face_VTS.controller'
if '--out' in sys.argv:
    i = sys.argv.index('--out')
    sys.stdout = io.open(sys.argv[i + 1], 'w', encoding='utf-8')

text = io.open(path, encoding='utf-8', errors='replace').read()
# 参数表条目（跟 check-controller.ps1 的写法一致）
declared = re.findall(r'(?m)^\s*- m_Name:\s*(\S+)\s*\n\s*m_Type:\s*\d+\s*\n\s*m_DefaultFloat:', text)
# 树里引用参数的地方：1D/2D 的轴 + Direct 子节点的权重（缩进不固定，别写死空格数）
used = {}
for m in re.finditer(r'(?m)^[ \t]*m_(?:BlendParameter|BlendParameterY|DirectBlendParameter):[ \t]*(\S+)', text):
    used[m.group(1)] = used.get(m.group(1), 0) + 1

print('asset: %s' % path)
print('declared params: %d   params referenced by trees: %d' % (len(declared), len(used)))
print('\n-- 有树消费（按次数）--')
for name in sorted(used, key=lambda n: (-used[n], n)):
    print('  %-38s %d' % (name, used[name]))
print('\n-- 声明了但没有任何树消费 --')
for name in sorted(set(declared) - set(used)):
    print('  %s' % name)
