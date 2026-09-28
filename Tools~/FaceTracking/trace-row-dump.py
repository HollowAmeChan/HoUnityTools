# trace-row-dump.py -- 从录样里把「某一行的表达式原文」取出来（录样里带着运行时真正用的表达式）
# 用法: python trace-row-dump.py <jsonl> [行名子串...]
import io
import json
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

p = sys.argv[1]
want = sys.argv[2:] or ['Mouth/Y', 'Mouth/Jaw', 'Mouth/Form']
seen = {}
n = 0
for line in io.open(p, encoding='utf-8-sig', errors='replace'):
    line = line.strip()
    if not line:
        continue
    try:
        o = json.loads(line)
    except Exception:
        continue
    if not (isinstance(o, dict) and o.get('kind') == 'sample'):
        continue
    n += 1
    for key in ('inputs', 'outputs', 'wires'):
        for it in o.get(key) or []:
            nm = it.get('name', '')
            if any(w in nm for w in want) and nm not in seen:
                seen[nm] = (key, it)
    if n >= 400:
        break

print('file   :', p)
print('samples:', n)
print('keys of first sample:', sorted(json.loads(io.open(p, encoding='utf-8-sig').readline()).keys()))
for nm in sorted(seen):
    key, it = seen[nm]
    print('')
    print('--- [%s] %s' % (key, nm))
    for k in sorted(it):
        v = it[k]
        if isinstance(v, (dict, list)):
            v = json.dumps(v, ensure_ascii=False)[:400]
        print('    %-14s %s' % (k, v))
