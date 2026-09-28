# fix-raw-newlines.py -- escape raw line breaks that sit INSIDE json strings.
#
# WHY: notes values are single-line json strings whose newlines must be the two characters
# backslash + n. Several earlier payload edits put a REAL newline in instead (the payload file
# itself held a newline), so today's profile only parses with a lenient reader (the middleware's
# hand-written one). `json.load` / jq reject it: "Invalid control character at: line 2776".
# This walks the text with a string-state machine and rewrites every raw CR/LF/TAB inside a
# string as \n / \t, dropping the indentation that followed the break.
#
# Read-only unless --apply. ASCII source on purpose.
import io
import sys


def fix(text):
    out = []
    i = 0
    n = len(text)
    inside = False
    hits = 0
    while i < n:
        c = text[i]
        if not inside:
            if c == '"':
                inside = True
            out.append(c)
            i += 1
            continue
        if c == '\\':
            out.append(text[i:i + 2])
            i += 2
            continue
        if c == '"':
            inside = False
            out.append(c)
            i += 1
            continue
        if c in '\r\n\t':
            # one break (CRLF counts once), then swallow the next line's indentation
            if c == '\r' and i + 1 < n and text[i + 1] == '\n':
                i += 1
            out.append('\\t' if c == '\t' else '\\n')
            i += 1
            while i < n and text[i] in ' \t':
                i += 1
            hits += 1
            continue
        out.append(c)
        i += 1
    return ''.join(out), hits


def main():
    path = sys.argv[1]
    apply = '--apply' in sys.argv
    raw = io.open(path, encoding='utf-8-sig', newline='').read()
    fixed, hits = fix(raw)
    print('raw breaks inside strings: %d   chars %d -> %d' % (hits, len(raw), len(fixed)))
    # show the escaped shape of every break we are about to touch
    i = 0
    shown = 0
    while i < len(raw) and shown < 12:
        c = raw[i]
        if c in '\r\n' and i > 0:
            ctx = raw[max(0, i - 24):i].replace('\r', '').replace('\n', '|')
            if not ctx.rstrip().endswith(('{', '[', ',', ':')):
                print('   near ...' + ctx.encode('ascii', 'replace').decode('ascii'))
                shown += 1
        i += 1
    if not apply:
        print('dry run -- pass --apply to write')
        return
    with io.open(path, 'w', encoding='utf-8-sig', newline='') as f:
        f.write(fixed)
    print('written: ' + path)


main()
