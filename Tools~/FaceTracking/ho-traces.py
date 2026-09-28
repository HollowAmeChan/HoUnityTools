# ho-traces.py -- 面捕录样（Logs/HoFaceTraces/*.jsonl）的**速查 / 速统 / 速对**小工具。
#
# 录样格式（每次一段文件）：第 1 行 = 头部（label / intervalSeconds / profileJson / settingsJson …），
# 中间 = 一行一个 sample（inputs[] = 配置输入行、outputs[] = 输出行、wires[] = 原始线），
# 最后 1 行 = 页脚（kind=end / reason / samples）。
# 命名空间：inputs 用裸名（`jawOpen`）、outputs 加 `out:`（`out:Ho/Drive/Mouth/Jaw`）、wires 加 `w:`。
#
# 用法（都能用「序号 / 文件名子串 / 组名」指定 take）：
#   python ho-traces.py ls                       # 速查：目录里有什么（标签/组/帧数/时长）
#   python ho-traces.py info 训练3                # 速查：一次的头部+页脚元数据
#   python ho-traces.py keys 1                   # 速查：采样行有哪些顶层字段、三个命名空间各多少
#   python ho-traces.py rows 1 --moved           # 速查：这次动过的通道（阈值可调）
#   python ho-traces.py stat 1 --channels jaw,mouth        # 速统：指定通道的 min/avg/max/波动
#   python ho-traces.py stat --group 静置                   # 速统：整组（各次统计后再汇总）
#   python ho-traces.py series 1 --channel jawOpen --every 4  # 速查：逐帧取值
#   python ho-traces.py check                    # 速对：全目录完整性（页脚/帧数/NaN/超范围）
#   python ho-traces.py diff 1 2 --channels jawOpen,mouthClose  # 速对：两份录样逐通道比
#   python ho-traces.py csv 1 --channels jawOpen,mouthClose,out:Ho/Drive/Mouth/Jaw > t.csv
import argparse
import glob
import io
import json
import os
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

DEFAULT_DIR = r'D:\Unity_Project\BREAK_URP\Logs\HoFaceTraces'
NS = {'inputs': '', 'outputs': 'out:', 'wires': 'w:'}


def take_files(d):
    return sorted(glob.glob(os.path.join(d, '*.jsonl')))


def load_manifest(d):
    """读 recording-labels-*.json ⇒ {文件名: (组, 标签, 第几次)}"""
    m = {}
    for p in sorted(glob.glob(os.path.join(d, 'recording-labels-*.json'))):
        try:
            o = json.loads(io.open(p, encoding='utf-8-sig').read())
        except Exception:
            continue
        for r in o.get('recordings') or []:
            fn = r.get('filename') or ''
            if fn:
                m[fn] = (r.get('group') or '', r.get('label') or '', r.get('repeat'))
            of = r.get('originalFilename')
            if of and of not in m:
                m[of] = (r.get('group') or '', r.get('label') or '', r.get('repeat'))
    return m


def read_take(path, want=None):
    """→ (meta, samples)；samples = [ {通道: 值} ]（三个命名空间合并，带前缀）"""
    meta, samples = None, []
    for line in io.open(path, encoding='utf-8-sig', errors='replace'):
        line = line.strip()
        if not line:
            continue
        try:
            o = json.loads(line)
        except Exception:
            continue
        if not isinstance(o, dict):
            continue
        if o.get('kind') != 'sample':
            if meta is None:
                meta = o
            else:
                meta.setdefault('_footers', []).append(o)
            continue
        f = {}
        for key, pre in NS.items():
            for it in o.get(key) or []:
                nm = pre + (it.get('name') or '')
                if want and nm not in want:
                    continue
                v = it.get('effective', it.get('published', it.get('value')))
                try:
                    f[nm] = float(v)
                except (TypeError, ValueError):
                    pass
        samples.append(f)
    return meta or {}, samples


def resolve(spec, d, man):
    """序号(1 起) / 文件名子串 / 组名 → 文件路径列表（组名可能命中多个）"""
    files = take_files(d)
    if not files:
        sys.exit('目录里没有 .jsonl：%s' % d)
    if spec.isdigit():
        i = int(spec)
        if not (1 <= i <= len(files)):
            sys.exit('序号超范围：%d（1..%d）' % (i, len(files)))
        return [files[i - 1]]
    hit = [f for f in files if spec in os.path.basename(f)]
    if hit:
        return hit
    grp = [f for f in files if man.get(os.path.basename(f), ('',))[0] and spec in man[os.path.basename(f)][0]]
    if grp:
        return grp
    sys.exit('找不到 take：%r（用 ls 看目录）' % spec)


def stats(v):
    v = [x for x in v if x == x]                     # 去 NaN
    if not v:
        return (float('nan'),) * 4
    return min(v), sum(v) / len(v), max(v), max(v) - min(v)


def match(channels, pats):
    if not pats:
        return channels
    out = []
    for c in channels:
        for p in pats:
            if p in c:
                out.append(c)
                break
    return out


def fmt_stats(s):
    return '%8.4f %8.4f %8.4f %8.4f' % s


# ─────────────────────────────────────────────────────────── 子命令
def cmd_ls(a):
    files = take_files(a.dir)
    man = load_manifest(a.dir)
    print('%-3s %-52s %-22s %6s %7s %8s %9s' % ('#', '文件', '标签/组', '帧', '间隔', '时长', '大小'))
    for i, p in enumerate(files, 1):
        meta, samples = read_take(p, want=set())
        g, lab, _ = man.get(os.path.basename(p), ('', '', None))
        dt = float(meta.get('intervalSeconds') or 0.05)
        print('%-3d %-52s %-22s %6d %7.3f %7.1fs %8.0fK' % (
            i, os.path.basename(p)[:52], (lab or '(未打标签)')[:22], len(samples), dt,
            len(samples) * dt, os.path.getsize(p) / 1024))
        if a.groups and g:
            print('      └ 组：%s' % g)


def cmd_info(a):
    for p in resolve(a.take, a.dir, load_manifest(a.dir)):
        meta, samples = read_take(p, want=set())
        print('=== %s' % os.path.basename(p))
        for k in sorted(meta):
            if k == 'profileJson':
                print('   %-18s <内嵌 profile，%d 字符>' % (k, len(str(meta[k]))))
            elif k in ('settingsJson',):
                print('   %-18s %s' % (k, str(meta[k])[:160]))
            else:
                print('   %-18s %s' % (k, str(meta[k])[:160]))
        dt = float(meta.get('intervalSeconds') or 0.05)
        print('   帧数              %d（%.1fs @ %.3fs）' % (len(samples), len(samples) * dt, dt))


def cmd_keys(a):
    for p in resolve(a.take, a.dir, load_manifest(a.dir)):
        raw = io.open(p, encoding='utf-8-sig', errors='replace')
        first = None
        sample = None
        for line in raw:
            line = line.strip()
            if not line:
                continue
            o = json.loads(line)
            if o.get('kind') == 'sample':
                sample = o
                break
            first = first or o
        print('=== %s' % os.path.basename(p))
        print('   头部字段：%s' % ', '.join(sorted(first or {})))
        print('   采样字段：%s' % ', '.join(sorted(sample or {})))
        for key in NS:
            print('   %-8s %d 条' % (key, len((sample or {}).get(key) or [])))
        it = ((sample or {}).get('inputs') or [{}])[0]
        print('   每条的样子：%s' % json.dumps(it, ensure_ascii=False)[:220])


def cmd_rows(a):
    p = resolve(a.take, a.dir, load_manifest(a.dir))[0]
    meta, samples = read_take(p)
    if not samples:
        sys.exit('没有采样')
    chans = sorted(samples[0])
    if not a.include_pose:
        chans = [c for c in chans if is_shape(c)]
    if a.moved:
        keep = []
        for c in chans:
            v = [f.get(c) for f in samples if c in f]
            if stats(v)[3] >= a.threshold:
                keep.append(c)
        chans = keep
    print('共 %d 个通道（%s）' % (len(chans), '动过的，阈值 %.4f' % a.threshold if a.moved else '全部'))
    for c in chans:
        v = [f.get(c) for f in samples if c in f]
        print('   %-40s %s' % (c, fmt_stats(stats(v))))


def cmd_stat(a):
    man = load_manifest(a.dir)
    if a.group:
        files = [f for f in take_files(a.dir) if man.get(os.path.basename(f), ('',))[0] == a.group]
        if not files:
            sys.exit('没有这个组：%r' % a.group)
    else:
        files = []
        for spec in a.take:
            files += resolve(spec, a.dir, man)
    per = {}
    for p in files:
        _, samples = read_take(p)
        if not samples:
            continue
        for c in sorted(samples[0]):
            if not match([c], a.channels):
                continue
            per.setdefault(c, []).append((os.path.basename(p), stats([f.get(c) for f in samples if c in f])))
    chan = sorted(per)
    if a.moved:
        chan = [c for c in chan if max(s[3] for _, s in per[c]) >= a.threshold]
    if not a.include_pose:
        chan = [c for c in chan if is_shape(c)]
    print('%-40s %8s %8s %8s %8s   take 数' % ('通道', 'min', 'avg', 'max', '波动'))
    for c in chan:
        rows = per[c]
        gmin = min(s[0] for _, s in rows)
        gavg = sum(s[1] for _, s in rows) / len(rows)
        gmax = max(s[2] for _, s in rows)
        print('%-40s %8.4f %8.4f %8.4f %8.4f   %d' % (c, gmin, gavg, gmax, gmax - gmin, len(rows)))


def cmd_series(a):
    p = resolve(a.take, a.dir, load_manifest(a.dir))[0]
    meta, samples = read_take(p)
    chans = match(sorted(samples[0]), a.channels)
    if not chans:
        sys.exit('没有匹配的通道（--channels）')
    dt = float(meta.get('intervalSeconds') or 0.05)
    print('=== %s  (%d 帧 @ %.3fs)' % (os.path.basename(p), len(samples), dt))
    print('%6s %7s  %s' % ('帧', '秒', '  '.join('%-20s' % c for c in chans)))
    for i in range(0, len(samples), a.every):
        f = samples[i]
        print('%6d %7.2f  %s' % (i, i * dt, '  '.join('%-20s' % ('%.4f' % f[c] if c in f else '-') for c in chans)))


# ⚠️ 不是形变量的通道：眼睛注视是角度、Rotation/Position 是位姿、还有帧号/时间戳/热键这些
#    ⇒ 「越界」与 `--moved` 两栏默认只查**形变类**通道，否则满屏假警报（第一版就是这么错的）。
#    要看全部就加 --include-pose。
NOT_SHAPE_BITS = ('Rotation', 'Rot', 'Position', 'Pos', 'Angle', 'EyeLeft', 'EyeRight', 'Eye/',
                  'Gaze', 'Head', 'Face', 'Body', 'Timestamp', 'Hotkey', 'clock', 'elapsed',
                  'unityFrame', 'revision', 'sample', 'deltaTime', 'lastReceivedAt',
                  'connected', 'Frame', 'Time')


def base_name(name):
    return name[4:] if name.startswith('out:') else (name[2:] if name.startswith('w:') else name)


def is_shape(name):
    b = base_name(name)
    return not any(bit in b for bit in NOT_SHAPE_BITS)


def cmd_check(a):
    man = load_manifest(a.dir)
    files = take_files(a.dir)
    if a.take:
        files = []
        for spec in a.take:
            files += resolve(spec, a.dir, man)
    bad = 0
    print('%-52s %7s %7s %5s %5s %6s  %s' % ('文件', '头部帧', '实际帧', '收尾', 'NaN', '越界', '越界的通道（最多 3 个）'))
    for p in files:
        meta, samples = read_take(p)
        foot = meta.get('_footers') or [{}]
        foot = foot[-1] if foot else {}
        declared = foot.get('samples')
        ok_end = (foot.get('kind') == 'end' and foot.get('reason') == 'completed')
        nan = 0
        weird = {}
        for f in samples:
            for k, v in f.items():
                if v != v or v in (float('inf'), float('-inf')):
                    nan += 1
                elif abs(v) > 1.5 and is_shape(k):
                    weird[k] = weird.get(k, 0) + 1
        nw = sum(weird.values())
        flag = '' if (ok_end and declared == len(samples) and not nan and not nw) else '   <<<'
        if flag:
            bad += 1
        top = ', '.join('%s×%d' % (k, v) for k, v in sorted(weird.items(), key=lambda x: -x[1])[:3])
        print('%-52s %7s %7d %5s %5d %6d  %s%s' % (
            os.path.basename(p)[:52], declared, len(samples), 'OK' if ok_end else 'BAD', nan, nw, top, flag))
    print('')
    print('合计 %d 个文件，可疑 %d 个（判据：页脚收尾 + 页脚帧数 == 实际帧数 + 无 NaN + 形变类通道不越界）' % (len(files), bad))


def cmd_diff(a):
    man = load_manifest(a.dir)
    pa = resolve(a.left, a.dir, man)[0]
    pb = resolve(a.right, a.dir, man)[0]
    _, sa = read_take(pa)
    _, sb = read_take(pb)
    chans = sorted(set().union(*[set(f) for f in sa[:1] + sb[:1]]))
    chans = match(chans, a.channels)
    if a.moved:
        chans = [c for c in chans
                 if stats([f.get(c) for f in sa if c in f])[3] >= a.threshold
                 or stats([f.get(c) for f in sb if c in f])[3] >= a.threshold]
    print('A = %s (%d 帧)' % (os.path.basename(pa), len(sa)))
    print('B = %s (%d 帧)' % (os.path.basename(pb), len(sb)))
    print('')
    print('%-40s %9s %9s %9s %9s' % ('通道', 'A avg', 'B avg', 'avg 差', 'max 差'))
    for c in chans:
        va = [f[c] for f in sa if c in f]
        vb = [f[c] for f in sb if c in f]
        if not va or not vb:
            continue
        ma, mb = sum(va) / len(va), sum(vb) / len(vb)
        mx = max(abs(x - y) for x, y in zip(va, vb)) if len(va) == len(vb) else float('nan')
        print('%-40s %9.4f %9.4f %+9.4f %9.4f' % (c, ma, mb, mb - ma, mx))


def cmd_csv(a):
    p = resolve(a.take, a.dir, load_manifest(a.dir))[0]
    meta, samples = read_take(p)
    chans = match(sorted(samples[0]), a.channels)
    print('frame,seconds,' + ','.join(chans))
    dt = float(meta.get('intervalSeconds') or 0.05)
    for i, f in enumerate(samples):
        print('%d,%.4f,' % (i, i * dt) + ','.join('%.6f' % f[c] if c in f else '' for c in chans))


ap = argparse.ArgumentParser(description='面捕录样速查/速统/速对')
ap.add_argument('--dir', default=DEFAULT_DIR)
sub = ap.add_subparsers(dest='cmd', required=True)

s = sub.add_parser('ls', help='速查：目录里有什么'); s.add_argument('--groups', action='store_true'); s.set_defaults(fn=cmd_ls)
s = sub.add_parser('info', help='速查：一次的头部/页脚'); s.add_argument('take'); s.set_defaults(fn=cmd_info)
s = sub.add_parser('keys', help='速查：采样行结构与命名空间'); s.add_argument('take'); s.set_defaults(fn=cmd_keys)
s = sub.add_parser('rows', help='速查：通道清单'); s.add_argument('take')
s.add_argument('--moved', action='store_true'); s.add_argument('--include-pose', action='store_true')
s.add_argument('--threshold', type=float, default=0.001); s.set_defaults(fn=cmd_rows)
s = sub.add_parser('stat', help='速统：min/avg/max/波动'); s.add_argument('take', nargs='*')
s.add_argument('--group'); s.add_argument('--channels', nargs='*'); s.add_argument('--moved', action='store_true')
s.add_argument('--include-pose', action='store_true', help='连注视/位姿/帧号那些非形变通道一起列')
s.add_argument('--threshold', type=float, default=0.001); s.set_defaults(fn=cmd_stat)
s = sub.add_parser('series', help='速查：逐帧取值'); s.add_argument('take')
s.add_argument('--channels', nargs='*', required=True); s.add_argument('--every', type=int, default=1); s.set_defaults(fn=cmd_series)
s = sub.add_parser('check', help='速对：全目录完整性'); s.add_argument('take', nargs='*'); s.set_defaults(fn=cmd_check)
s = sub.add_parser('diff', help='速对：两份录样逐通道比'); s.add_argument('left'); s.add_argument('right')
s.add_argument('--channels', nargs='*'); s.add_argument('--moved', action='store_true')
s.add_argument('--threshold', type=float, default=0.001); s.set_defaults(fn=cmd_diff)
s = sub.add_parser('csv', help='导出 CSV'); s.add_argument('take'); s.add_argument('--channels', nargs='*'); s.set_defaults(fn=cmd_csv)

a = ap.parse_args()
a.fn(a)
