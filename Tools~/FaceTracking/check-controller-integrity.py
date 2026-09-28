# check-controller-integrity.py -- 通用完整性检查（.controller 文本层面）
#   ① 每个 {fileID: N} 引用是否都有对应的块（或 0）；② 状态机 -> 状态 -> 根树 的接线对不对
import io
import re
import sys

path = sys.argv[1] if len(sys.argv) > 1 else 'D:/Unity_Project/BREAK_URP/Assets/Hollow/土豆/FT/PTP_CTR_Face_ARKit.controller'
t = io.open(path, encoding='utf-8-sig', errors='replace').read()

blocks = {}
for m in re.finditer(r'^--- !u!(\d+) &(\d+)', t, re.M):
    blocks[m.group(2)] = m.group(1)
print('块数: %d  (按类型: %s)' % (
    len(blocks), ', '.join('%sx%d' % (k, v) for k, v in
                            sorted({c: sum(1 for x in blocks.values() if x == c) for c in set(blocks.values())}.items()))))

refs = set(re.findall(r'\{fileID: (\d+)(?:,|\})', t))
dangling = sorted(r for r in refs if r != '0' and r not in blocks)
print('引用总数: %d · 悬空引用: %d %s' % (len(refs), len(dangling), ('→ ' + ', '.join(dangling[:8])) if dangling else ''))

# 找根树 / 状态 / 状态机
def name_of(bid):
    m = re.search(r'^--- !u!\d+ &' + bid + r'\n(.*?)(?=^--- !u!|\Z)', t, re.M | re.S)
    n = re.search(r'^  m_Name: (.*)$', m.group(1), re.M) if m else None
    return n.group(1).strip() if n else '?'

trees = {bid: name_of(bid) for bid, cls in blocks.items() if cls == '206'}
root = [bid for bid, nm in trees.items() if nm == 'Ho/00 Drive Tree']
print('根树: %s' % (root[0] + ' (' + trees[root[0]] + ')' if root else '没找到 Ho/00 Drive Tree'))

state = [bid for bid, cls in blocks.items() if cls == '1102']
for sid in state:
    m = re.search(r'^--- !u!1102 &' + sid + r'\n(.*?)(?=^--- !u!|\Z)', t, re.M | re.S).group(1)
    mo = re.search(r'^  m_Motion: \{fileID: (\d+)\}', m, re.M)
    print('状态 %s: 名字=%s · m_Motion=%s %s' % (
        sid, name_of(sid), mo.group(1) if mo else '无',
        ('-> ' + trees.get(mo.group(1), '(不是 BlendTree)')) if mo else ''))

sm = [bid for bid, cls in blocks.items() if cls == '1107']
for sid in sm:
    m = re.search(r'^--- !u!1107 &' + sid + r'\n(.*?)(?=^--- !u!|\Z)', t, re.M | re.S).group(1)
    df = re.search(r'^  m_DefaultState: \{fileID: (\d+)\}', m, re.M)
    kids = re.findall(r'^  - serializedVersion: \d+\n    m_State: \{fileID: (\d+)\}', m, re.M)
    print('状态机 %s: 默认状态=%s · 子状态=%s' % (sid, df.group(1) if df else '无', ','.join(kids) or '无'))
