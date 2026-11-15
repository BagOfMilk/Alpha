# Зводить рішення власника зі сторінки «Карта механік Рингу» в текст.
#
# Спершу зберегти базу сторінки інструментом ArtifactData (action: list, collection: approvals, out_dir: <тека>),
# потім:  python -I tools/ring_pm/mda/approvals_report.py <тека>\approvals
#
# Рішення — дані власника: yes — в ітерацію 1, no — переробити, later — пізніше (ітерація 2).
import io
import json
import os
import sys

sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
src = sys.argv[1]
raw = json.load(io.open(os.path.join(HERE, 'raw.json'), encoding='utf-8'))
SYS = {'match': 'Матч', 'career': "Кар'єра", 'show': 'Шоу й показ'}
items = []
for k in ('match', 'career', 'show'):
    items += [(m['id'], m['name'], 'механіка', k) for m in raw[k]['mechanics']]
    items += [(d['id'], d['name'], 'динаміка', k) for d in raw[k]['dynamics']]
AES = ['sensation', 'challenge', 'narrative', 'fantasy', 'expression', 'discovery', 'submission', 'fellowship']
items += [('a.' + a, a, 'естетика', '') for a in AES]

got = {}
for f in os.listdir(src):
    if f.endswith('.json'):
        d = json.load(io.open(os.path.join(src, f), encoding='utf-8'))
        d = d.get('data', d)
        got[f[:-5]] = d
LAB = {'yes': 'в ітерацію 1', 'no': 'переробити', 'later': 'пізніше (ітерація 2)'}
cnt = {'yes': 0, 'no': 0, 'later': 0, None: 0}
rows = []
for i, name, kind, k in items:
    v = (got.get(i) or {}).get('verdict')
    cnt[v if v in LAB else None] += 1
    rows.append((i, name, kind, k, v, (got.get(i) or {}).get('note', '')))
print('Усього пунктів %d: в ітерацію 1 — %d, переробити — %d, пізніше — %d, без рішення — %d'
      % (len(items), cnt['yes'], cnt['no'], cnt['later'], cnt[None]))
for v in ('later', 'no'):
    print('\n== %s' % LAB[v])
    for i, name, kind, k, vv, note in rows:
        if vv == v:
            print('- %s (%s, %s) [%s]%s' % (name, kind, SYS.get(k, '—'), i, (' — ' + note) if note else ''))
print('\n== в ітерацію 1 з коментарем')
for i, name, kind, k, vv, note in rows:
    if vv == 'yes' and note:
        print('- %s [%s] — %s' % (name, i, note))
print('\n== без рішення: %d' % cnt[None])
for kind in ('механіка', 'динаміка', 'естетика'):
    g = [r for r in rows if r[4] not in LAB and r[2] == kind]
    print('  %s: %d' % (kind, len(g)))
