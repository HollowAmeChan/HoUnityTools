# profile-row-diff.py -- 把若干份 profile 里同一批行的表达式并排打出来
# 用法: python profile-row-diff.py <row 子串> <profile.json> [profile2.json ...]
import io
import json
import sys

# GBK 控制台打不出 ⇒ / ⭐ 之类 ⇒ 统一按 UTF-8 输出（错误字符替换，不让脚本死）
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

want = sys.argv[1]
files = sys.argv[2:]
for p in files:
    try:
        o = json.loads(io.open(p, encoding='utf-8-sig').read())
    except Exception as e:
        print('!! %s : %s' % (p, e))
        continue
    # 收集 profile 里所有「带 parameter 的列表」（输入行 / 输出行可能分开放）
    rows = []

    def walk(node, depth=0):
        if depth > 2:
            return
        if isinstance(node, list):
            for v in node:
                if isinstance(v, dict) and 'parameter' in v and 'expression' in v:
                    rows.append(v)
                else:
                    walk(v, depth + 1)
        elif isinstance(node, dict):
            for v in node.values():
                walk(v, depth + 1)

    walk(o)
    print('=' * 100)
    print('%s  (rows=%d)' % (p, len(rows)))
    for r in rows:
        if want in (r.get('parameter') or ''):
            print('  [%s]' % r.get('parameter'))
            print('      expr  : %s' % (r.get('expression') or r.get('formula')))
            mods = r.get('modifiers') or []
            print('      mods  : %s' % '; '.join('%s %s' % (m.get('kind'), {k: v for k, v in m.items() if k != 'kind'}) for m in mods))
            ck = ((r.get('curve') or {}).get('keys') or [])
            print('      curve : %s' % ' '.join('%g->%g' % (k.get('t'), k.get('v')) for k in ck))
            nt = r.get('notes') or ''
            print('      notes 长度 %d' % len(nt))
            print('      notes 尾  : ...%s' % nt[-320:].replace('\n', ' | '))
            print('      notes 尾 repr: %s' % repr(nt[-120:]))
