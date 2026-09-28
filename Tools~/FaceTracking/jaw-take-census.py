# jaw-take-census.py -- 定膝/定宽用：基量取 inputs[] 的规范名（wires 里是 VTS 线名，拼写不同）
import io
import json
import os
import sys

# GBK 控制台打不出 ⇒ / ⭐ 之类 ⇒ 统一按 UTF-8 输出（错误字符替换，不让脚本死）
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

LABELS = '.research/mouth-isolation-2026-09-29/recording-labels-20260929.json'
TRACE = 'D:/Unity_Project/BREAK_URP/Logs/HoFaceTraces'

lab = json.loads(io.open(LABELS, encoding='utf-8-sig').read())
groups = {}
for r in lab['recordings']:
    p = os.path.join(TRACE, r['filename'])
    if not os.path.isfile(p):
        p = os.path.join(TRACE, r['originalFilename'])
    groups.setdefault(r['group'], []).append(p)


def rows(p):
    out = []
    for line in io.open(p, encoding='utf-8-sig', errors='replace'):
        line = line.strip()
        if not line:
            continue
        try:
            o = json.loads(line)
        except Exception:
            continue
        if isinstance(o, dict) and o.get('kind') == 'sample':
            d = {}
            for i in o.get('inputs') or []:
                d[i['name']] = float(i.get('effective') or 0.0)       # 规范名 = profile 输入行的参数名
            for r in o.get('outputs') or []:
                d['out:' + r['name']] = float(r.get('published') or 0.0)
            if d:
                out.append(d)
    return out


data = {g: [rows(p) for p in ps] for g, ps in groups.items()}
names = sorted(data)
S = {g: 'G%d' % (i + 1) for i, g in enumerate(names)}
for g in names:
    print('%s = %s (%d takes, %d samples)' % (S[g], g[:28], len(data[g]), sum(len(s) for s in data[g])))


def ser(g, fn):
    return [fn(o) for s in data[g] for o in s]


def pct(v, q):
    v = sorted(v)
    return v[min(len(v) - 1, int(q * len(v)))] if v else float('nan')


def rep(tag, fn):
    print('-- %s' % tag)
    for g in names:
        v = ser(g, fn)
        if v:
            print('   %s  min=%6.3f p50=%6.3f p90=%6.3f p99=%6.3f max=%6.3f  maxjump=%5.3f' % (
                S[g], min(v), pct(v, .5), pct(v, .9), pct(v, .99), max(v),
                max((abs(v[i + 1] - v[i]) for i in range(len(v) - 1)), default=0.0)))


print()
rep('jawOpen (输入行 effective)', lambda o: o.get('jawOpen', 0))
rep('mouthClose', lambda o: o.get('mouthClose', 0))
rep('min(jawOpen,mouthClose)   <- 咀嚼候选', lambda o: min(o.get('jawOpen', 0), o.get('mouthClose', 0)))
rep('jawOpen*mouthClose        <- 老判据的核', lambda o: o.get('jawOpen', 0) * o.get('mouthClose', 0))
rep('stretch 平均', lambda o: (o.get('mouthStretchLeft', 0) + o.get('mouthStretchRight', 0)) / 2)
rep('out:Mouth/Form', lambda o: o.get('out:Ho/Drive/Mouth/Form', 0))
rep('out:Mouth/Y', lambda o: o.get('out:Ho/Drive/Mouth/Y', 0))
rep('out:Mouth/Jaw', lambda o: o.get('out:Ho/Drive/Mouth/Jaw', 0))

print()
print('=== 拟合 ===')
a, b = names[2], names[3]
for tag, fn in (('OPEN: jawOpen', lambda o: o.get('jawOpen', 0)),
                ('CHEW: min(jaw,mc)', lambda o: min(o.get('jawOpen', 0), o.get('mouthClose', 0)))):
    if tag.startswith('OPEN'):
        x, y = names[0], names[1]
    else:
        x, y = a, b
    vx, vy = ser(x, fn), ser(y, fn)
    print('  %s | 未触发 %s: p99=%.3f max=%.3f | 触发 %s: p90=%.3f p99=%.3f max=%.3f' % (
        tag, S[x], pct(vx, .99), max(vx), S[y], pct(vy, .9), pct(vy, .99), max(vy)))

print()
print('=== stretch 归因（拟合补偿系数）===')
for g in names:
    rr = [o for s in data[g] for o in s if 'jawOpen' in o]
    if not rr:
        continue
    st = sum((o.get('mouthStretchLeft', 0) + o.get('mouthStretchRight', 0)) / 2 for o in rr) / len(rr)
    jo = sum(o.get('jawOpen', 0) for o in rr) / len(rr)
    mc = sum(o.get('mouthClose', 0) for o in rr) / len(rr)
    cj = sum(o.get('jawOpen', 0) * o.get('mouthClose', 0) for o in rr) / len(rr)
    print('   %s avg stretch=%5.3f  jawOpen=%5.3f  mouthClose=%5.3f  j*m=%5.3f' % (S[g], st, jo, mc, cj))


# ---------------------------------------------------------------- 表达式 A/B
# 候选的「闭嘴咀嚼」证据：min(jawOpen, mouthClose)（两者同时高 = 嘴唇闭着、下颌在动）。
# 真开唇时 mouthClose 不抬 ⇒ 该量恒低；纯咀嚼时两者同高 ⇒ 该量高。
def mn(o):
    return min(o.get('jawOpen', 0), o.get('mouthClose', 0))


def d(o):
    return o.get('jawOpen', 0) - o.get('mouthClose', 0)


KNEE, WIDE = 0.16, 0.20          # 0.16..0.36 宽斜坡（真开唇上界 0.112 / 轻咀嚼上界 0.225 / 强咀嚼 0.62）


def gate(o, knee=KNEE, wide=WIDE):
    return max(0.0, min(1.0, (mn(o) - knee) / wide))


def jaw_old(o):
    dd = d(o)
    return dd if dd < 0 else o.get('jawOpen', 0)


def jaw_new(o):
    return o.get('jawOpen', 0) - max(-d(o), 0.0)      # 只扣「嘴唇压得比下颌更紧」的那部分 ⇒ 连续


def ydown_old(o):
    g = max(0.0, min(1.0, (o.get('mouthClose', 0) - o.get('jawOpen', 0) + 0.1) / 0.1))
    return max(0.0, min(1.0, (o.get('jawOpen', 0) * g - 0.35) / 0.1))


def ydown_new(o):
    return max(0.0, min(1.0, (jaw_new(o) - 0.35) / 0.1))


def terms(o):
    s = (o.get('mouthSmileLeft', 0) + o.get('mouthSmileRight', 0)
         + (o.get('mouthDimpleLeft', 0) + o.get('mouthDimpleRight', 0)) / 2)
    f = (o.get('mouthFrownLeft', 0) + o.get('mouthFrownRight', 0)) / 2
    tr = (o.get('mouthStretchLeft', 0) + o.get('mouthStretchRight', 0)) / 2
    t = max(0.0, min(1.0, (tr - (0.42 * o.get('jawOpen', 0) + 0.05)) * 1.5))
    return s, f, t, tr


def form_old(o):
    s, f, t, _ = terms(o)
    return (s - 2 * max(f, t)) / 2


def form_new(o):
    s, f, t, _ = terms(o)
    return (s - 2 * max(f * (1 - gate(o)), t)) / 2


def jump(fn, g):
    worst = 0.0
    for s in data[g]:
        v = [fn(o) for o in s]
        worst = max([worst] + [abs(v[i + 1] - v[i]) for i in range(len(v) - 1)])
    return worst


print()
print('=== 咀嚼被读成苦：归因（G4 = 强咀嚼）===')
print('   %-4s %8s %8s %8s %8s %8s %8s' % ('grp', 'frown p90', 'stretch', '基线', 'T(残差)', 'max(F,T)', 'Form'))
for g in names:
    rr = [o for s in data[g] for o in s if 'jawOpen' in o]
    if not rr:
        continue
    f = [(o.get('mouthFrownLeft', 0) + o.get('mouthFrownRight', 0)) / 2 for o in rr]
    tt = [terms(o)[2] for o in rr]
    tr = [terms(o)[3] for o in rr]
    base = [0.42 * o.get('jawOpen', 0) + 0.05 for o in rr]
    mx = [max(a, b) for a, b in zip(f, tt)]
    fo = [form_old(o) for o in rr]
    print('   %-4s %8.3f %8.3f %8.3f %8.3f %8.3f %8.3f' % (
        S[g], pct(f, .9), sum(tr) / len(tr), sum(base) / len(base), pct(tt, .9), pct(mx, .9), min(fo)))

print()
print('=== 表达式 A/B（每格 = 最小 / 相邻帧最大跳变）===')
print('   说明：Jaw 旧/新都比的是「值」；Y下移 比的是「下移量」（0..1，越大越下移）')
print('   %-4s %14s %14s %14s %14s' % ('grp', 'Jaw 旧', 'Jaw 新', 'Y下移 旧', 'Y下移 新'))
for g in names:
    rr = [o for s in data[g] for o in s if 'jawOpen' in o]
    if not rr:
        continue
    jn = [jaw_new(o) for o in rr]
    print('   %-4s %6.3f/%5.3f %6.3f/%5.3f %6.3f/%5.3f %6.3f/%5.3f' % (
        S[g], min(jaw_old(o) for o in rr), jump(jaw_old, g), min(jn), jump(jaw_new, g),
        max(ydown_old(o) for o in rr), jump(ydown_old, g), max(ydown_new(o) for o in rr), jump(ydown_new, g)))

print()
print('=== 新 Form（咀嚼门乘在苦证据上）===')
for g in names:
    rr = [o for s in data[g] for o in s if 'jawOpen' in o]
    if not rr:
        continue
    fo = [form_old(o) for o in rr]
    fn = [form_new(o) for o in rr]
    gt = [gate(o) for o in rr]
    print('   %s 旧 min=%6.3f p50=%6.3f | 新 min=%6.3f p50=%6.3f | 门 p50=%.3f p90=%.3f max=%.3f' % (
        S[g], min(fo), pct(fo, .5), min(fn), pct(fn, .5), pct(gt, .5), pct(gt, .9), max(gt)))

print()
print('=== 苦判据在最坏帧上是谁（Form < -0.15 的帧全列）===')
for g in names:
    rr = [o for s in data[g] for o in s if 'jawOpen' in o]
    bad = 0
    for i, o in enumerate(rr):
        s, f, t, tr = terms(o)
        fo = (s - 2 * max(f, t)) / 2
        if fo < -0.15:
            bad += 1
            if bad <= 6:
                print('   %s #%3d  S=%5.3f F=%5.3f T=%5.3f stretch=%5.3f  jaw=%5.3f mc=%5.3f min=%5.3f d=%+5.3f gate=%.2f' % (
                    S[g], i, s, f, t, tr, o.get('jawOpen', 0), o.get('mouthClose', 0), mn(o), d(o), gate(o)))
    if bad:
        print('   %s 共 %d 帧 Form < -0.15（占 %.1f%%）' % (S[g], bad, 100.0 * bad / len(rr)))

print()
print('=== 阶梯归因：跳变里有多少是「输入自己跳的」===')
for tag, fn in (('Jaw 旧', jaw_old), ('Jaw 新', jaw_new)):
    for g in names:
        rr = [o for s in data[g] for o in s if 'jawOpen' in o]
        if not rr:
            continue
        worst_excess = 0.0
        n_excess = 0
        for i in range(len(rr) - 1):
            do = abs(fn(rr[i + 1]) - fn(rr[i]))
            di = max(abs(rr[i + 1].get('jawOpen', 0) - rr[i].get('jawOpen', 0)),
                     abs(rr[i + 1].get('mouthClose', 0) - rr[i].get('mouthClose', 0)))
            if do - di > worst_excess:
                worst_excess = do - di
            if do > 0.2 and do > di + 0.1:
                n_excess += 1
        print('   %-6s %s  输入没解释的那部分最大 = %5.3f   无理由大跳帧数 = %d' % (tag, S[g], worst_excess, n_excess))


# ------------------------------------------------- 咀嚼门（锁存）+ 苦判据压制
def interval(p):
    for line in io.open(p, encoding='utf-8-sig', errors='replace'):
        line = line.strip()
        if not line:
            continue
        try:
            o = json.loads(line)
        except Exception:
            continue
        if isinstance(o, dict) and o.get('kind') != 'sample':
            return float(o.get('intervalSeconds') or 0.05)
    return 0.05


def simulate_steps(vals, dt, trigger, threshold, hold):
    """照 HoFaceAnimationSession.Step 的语义：过 trigger 点亮、掉到 trigger-|threshold| 以下才灭、点亮后至少 hold 秒。"""
    on = False
    until = 0.0
    out = []
    t = 0.0
    for v in vals:
        if not on and v >= trigger:
            on = True
            until = t + hold
        elif on and t >= until and v < trigger - abs(threshold):
            on = False
        out.append(1.0 if on else 0.0)
        t += dt
    return out


TRIG, THR, HOLD = 0.5, 0.45, 0.5
print()
print('=== 咀嚼门（膝 0.16/0.20 的 min 证据 + 维持 trigger %.2f / 释放 %.2f / hold %.2fs）===' % (TRIG, TRIG - THR, HOLD))
for g in names:
    for p in groups[g]:
        rr = rows(p)
        if not rr:
            continue
        dt = interval(p)
        ev = [gate(o) for o in rr]
        gt = simulate_steps(ev, dt, TRIG, THR, HOLD)
        n_on = int(sum(gt))
        #  chewing period: 亮着的连续段长度（秒）
        runs = []
        run = 0
        for b in gt:
            if b > 0:
                run += 1
            elif run:
                runs.append(run * dt)
                run = 0
        if run:
            runs.append(run * dt)
        # 门开时的苦证据 vs 门关时
        s_on = [max(terms(o)[1], terms(o)[2]) for o, b in zip(rr, gt) if b > 0]
        s_off = [max(terms(o)[1], terms(o)[2]) for o, b in zip(rr, gt) if b == 0]
        print('   %s %-34s dt=%.3f 亮 %.0f%%  段数 %d 最长 %.1fs | 苦证据 门开 max=%.3f  门关 max=%.3f' % (
            S[g], os.path.basename(p)[:34], dt, 100.0 * n_on / len(rr), len(runs),
            max(runs) if runs else 0.0,
            max(s_on) if s_on else 0.0, max(s_off) if s_off else 0.0))

print()
print('=== 新 Form（苦证据 x (1 - 锁存咀嚼门)）===')
for g in names:
    rr_all, gt_all = [], []
    for p in groups[g]:
        rr = rows(p)
        if not rr:
            continue
        dt = interval(p)
        gt = simulate_steps([gate(o) for o in rr], dt, TRIG, THR, HOLD)
        rr_all += rr
        gt_all += gt
    if not rr_all:
        continue
    old = [form_old(o) for o in rr_all]
    new = []
    for o, b in zip(rr_all, gt_all):
        s, f, t, _ = terms(o)
        new.append((s - 2 * max(f, t) * (1 - b)) / 2)
    print('   %s 旧 min=%6.3f p50=%6.3f 负帧 %2d | 新 min=%6.3f p50=%6.3f 负帧 %2d (%2d/%d)' % (
        S[g], min(old), pct(old, .5), sum(1 for v in old if v < 0),
        min(new), pct(new, .5), sum(1 for v in new if v < 0),
        sum(1 for b in gt_all if b > 0), len(gt_all)))
