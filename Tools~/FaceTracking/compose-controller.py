# compose-controller.py -- 照 `controller-parts.json` 的配方**一条命令重生成**分层控制器
#
# 这套控制器是一层层合成的：`DIAG_L<n>_<名字>`，**L 越大越靠上（合成得越多）**。
# L1 是从生产件直接剪出来的，往上逐层合成；**没有「L0 总控制器」** —— 生产件（`PTP_CTR_Face_VTS.controller`）
# 就是最高的那一层，最终合成结果由人手动复制进它，脚本永远不动生产件本身。
# 「谁合谁」只在 `controller-parts.json` 里写一次：这份脚本照它做，rig 的 Diagnostics/README.md 照它写给人看。
#
# 用法：
#   python compose-controller.py --list                 # 看配方（含哪些是手工件、不能重生成）
#   python compose-controller.py DIAG_L2_MouthGroup     # 重生成一份（名字可写子串）
#   python compose-controller.py --all                  # 重生成所有非手工件
#   python compose-controller.py DIAG_L2_MouthGroup --dry-run
#
# 规矩：
#   * 覆盖前把旧文件（含 .meta）备份到 %TEMP%\ho-controller-backups\<时间戳>\；
#   * **沿用旧 .meta 的 GUID** ⇒ Unity 里挂着的引用不断（重生成不是新资产）；
#   * 生成后跑 check-controller-integrity.py，再跑 check-controller.ps1 -Isolation 打分（缺/错要 0）。
import argparse
import datetime
import io
import json
import os
import re
import shutil
import subprocess
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
HERE = os.path.dirname(os.path.abspath(__file__))
RECIPE = os.path.join(HERE, 'controller-parts.json')
ISOLATE = os.path.join(HERE, 'isolate-trees.py')
INTEGRITY = os.path.join(HERE, 'check-controller-integrity.py')
CHECKER = os.path.join(HERE, 'check-controller.ps1')


def read_recipe():
    return json.loads(io.open(RECIPE, encoding='utf-8-sig').read())


def guid_of(meta):
    if not os.path.exists(meta):
        return None
    m = re.search(r'guid:\s*([0-9a-f]{32})', io.open(meta, encoding='utf-8-sig', errors='replace').read())
    return m.group(1) if m else None


def build(entry, rig, dry):
    target = os.path.join(rig, 'Diagnostics', entry['file'])
    src = os.path.join(rig, entry.get('from', read_recipe()['source']))
    cmd = [sys.executable, ISOLATE, src, target,
           '--root-children', ','.join(entry['rootChildren'])]
    base = entry.get('base')
    if base:
        cmd += ['--base', os.path.join(rig, 'Diagnostics', base)]
    for tree, kids in (entry.get('children') or {}).items():
        cmd += ['--children', '%s=%s' % (tree, ','.join(kids))]
    if entry.get('stateMotion'):
        cmd += ['--state-motion', entry['stateMotion']]
    old_guid = guid_of(target + '.meta')
    if old_guid:
        cmd += ['--guid', old_guid]

    print('  → %s' % entry['file'])
    print('      %s' % ' '.join('"%s"' % c if ' ' in c else c for c in cmd[2:]))
    if dry:
        return True
    if os.path.exists(target):
        stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
        backup = os.path.join(os.environ.get('TEMP', '/tmp'), 'ho-controller-backups', stamp)
        os.makedirs(backup, exist_ok=True)
        shutil.copy2(target, backup)
        if os.path.exists(target + '.meta'):
            shutil.copy2(target + '.meta', backup)
        print('      旧件已备份到 %s' % backup)
        os.remove(target)
        if os.path.exists(target + '.meta'):
            os.remove(target + '.meta')
    r = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='replace')
    for line in (r.stdout or '').strip().splitlines():
        print('      ' + line.strip())
    if r.returncode != 0:
        print('      ✗ isolate-trees.py 失败：%s' % (r.stderr or '').strip()[:300])
        return False
    r2 = subprocess.run([sys.executable, INTEGRITY, target], capture_output=True, text=True, encoding='utf-8', errors='replace')
    ok2 = r2.returncode == 0 and '悬空' in (r2.stdout or '')
    print('      完整性检查：%s' % ('通过' if ok2 else '见下'))
    if not ok2:
        print((r2.stdout or '') + (r2.stderr or ''))
    if os.name == 'nt' and os.path.exists(CHECKER):
        r3 = subprocess.run(['powershell', '-NoProfile', '-File', CHECKER, '-Path', target, '-Isolation'],
                            capture_output=True, text=True, encoding='utf-8', errors='replace')
        bad = [l for l in (r3.stdout or '').splitlines() if '[缺/错]' in l]
        print('      检查器（隔离模式）：缺/错 %d 条%s' % (len(bad), '' if not bad else ' ← 见下'))
        for l in bad[:6]:
            print('        ' + l.strip())
        return len(bad) == 0
    return True


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('name', nargs='?', help='要重生成的控制器（名字子串）')
    ap.add_argument('--all', action='store_true')
    ap.add_argument('--list', action='store_true')
    ap.add_argument('--rig', help='FT 目录（默认取配方里的 rigDir）')
    ap.add_argument('--dry-run', action='store_true')
    a = ap.parse_args()
    recipe = read_recipe()
    rig = a.rig or recipe['rigDir']
    entries = recipe['controllers']
    if a.list or (not a.name and not a.all):
        print('  分层命名：%s' % recipe['levels'])
        print('')
        print('  %-38s %-4s %-22s %s' % ('文件', '层', '来源', '备注'))
        for e in entries:
            if e.get('manual'):
                org = '手工件'
            elif e.get('base'):
                org = '生产件 +骨架 %s' % re.sub(r'^DIAG_(L\d)_', r'\1 ', e['base'].replace('.controller', ''))
            else:
                org = '生产件'
            print('  %-38s %-4s %-22s %s' % (e['file'], e['level'], org[:22], (e.get('notes') or '')[:46]))
        print('')
        print('  重生成：compose-controller.py <名字子串>   或  --all')
        return 0
    todo = [e for e in entries if (a.all and not e.get('manual')) or (a.name and a.name in e['file'])]
    if not todo:
        print('  没有匹配的配方条目（--list 看全部）')
        return 1
    ok = True
    for e in todo:
        if e.get('manual'):
            print('  ⏭ %s 是手工件，跳过（%s）' % (e['file'], (e.get('notes') or '')[:60]))
            continue
        ok = build(e, rig, a.dry_run) and ok
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
