# controller-block-diff.py -- Unity .controller 的「语义」对照：把 fileID 归一化后按块比多重集
# 为什么：两个文件可能只是**序列化块的顺序**不同（Unity 存盘会重排）⇒ 文本 diff 上千行，
# 但树/状态/参数其实一模一样。要看的是「有没有哪一块的**内容**不同」。
# 用法: python controller-block-diff.py <a.controller> <b.controller> [类名过滤，如 BlendTree]
import io
import re
import sys
import collections

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

CLASS = {
    '206': 'BlendTree', '1102': 'AnimatorState', '1107': 'AnimatorStateMachine',
    '1101': 'AnimatorController', '114': 'MonoBehaviour', '91': 'AnimatorController',
}
HEAD = re.compile(r'^--- !u!(\d+) &(\d+)')
FID = re.compile(r'\{fileID: (-?\d+)\}')


def blocks(path):
    out = []
    cur = None
    for line in io.open(path, encoding='utf-8-sig', errors='replace'):
        line = line.rstrip('\n').rstrip('\r')
        m = HEAD.match(line)
        if m:
            if cur:
                out.append(cur)
            cur = [m.group(1), []]
            continue
        if cur is not None:
            cur[1].append(line)
    if cur:
        out.append(cur)
    return out


def norm(cls, lines, idmap):
    txt = '\n'.join(lines)
    txt = FID.sub(lambda m: '{fileID: %s}' % idmap.setdefault(m.group(1), 'K%d' % len(idmap)), txt)
    # 掉对象头里那些每份文件都不同、语义无关的字段
    txt = re.sub(r'm_ObjectHideFlags: \d+', 'm_ObjectHideFlags: *', txt)
    return '%s\n%s' % (CLASS.get(cls, cls), txt)


a, b = sys.argv[1], sys.argv[2]
want = sys.argv[3] if len(sys.argv) > 3 else None
A = blocks(a)
B = blocks(b)
ca = collections.Counter(); cb = collections.Counter()
la = {}; lb = {}
for cls, lines in A:
    k = norm(cls, lines, la)
    if want and CLASS.get(cls, cls) != want:
        continue
    ca[k] += 1
for cls, lines in B:
    k = norm(cls, lines, lb)
    if want and CLASS.get(cls, cls) != want:
        continue
    cb[k] += 1

print('A = %s  blocks=%d' % (a, len(A)))
print('B = %s  blocks=%d' % (b, len(B)))
for cls in sorted(set([c for c, _ in A] + [c for c, _ in B])):
    na = sum(1 for c, _ in A if c == cls)
    nb = sum(1 for c, _ in B if c == cls)
    flag = '   <<< 数量不同' if na != nb else ''
    print('   %-12s A=%-4d B=%-4d%s' % (CLASS.get(cls, cls), na, nb, flag))

onlyA = ca - cb
onlyB = cb - ca
print()
print('只在 A（rig 现有）里的块: %d 种' % len(onlyA))
for k, n in list(onlyA.items())[:6]:
    print('   x%d %s' % (n, k[:400].replace('\n', ' | ')))
print('只在 B（生成版）里的块: %d 种' % len(onlyB))
for k, n in list(onlyB.items())[:6]:
    print('   x%d %s' % (n, k[:400].replace('\n', ' | ')))
