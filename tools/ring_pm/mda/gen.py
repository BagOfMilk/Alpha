# Збирає сторінку «Карта механік Рингу» (механіки → динаміки → естетики, готовність, схвалення власника).
#
#   python -I tools/ring_pm/mda/gen.py
#
# Вхід (поруч зі скриптом):
#   raw.json      — дослідження 15.11.2026: розділи match / career / show (mechanics, dynamics, aesthetics, gaps) і ready;
#   fixes.json    — виправлення після незалежної звірки з кодом (findings.json — її повний звіт);
#   template.html — сторінка з міткою /*__DATA__*/null.
# Вихід: docs/ring/mda/ring_mda.html — вміст сторінки без каркаса документа (так його публікує Artifact).
# Опубліковано: https://claude.ai/artifact/7D3fJaG68qF4e5FJ6FnLDD ; рішення власника лежать у базі сторінки,
# колекція «approvals», документ = id пункту (m.* механіка, d.* динаміка, a.* естетика): verdict yes / no / later, note.
import io
import json
import os
import re
import sys

sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
OUT = os.path.join(ROOT, 'docs', 'ring', 'mda', 'ring_mda.html')


def load(name):
    return json.load(io.open(os.path.join(HERE, name), encoding='utf-8'))


r = load('raw.json')

# --- виправлення після звірки ---
index = {}
for k in ('match', 'career', 'show'):
    for m in r[k]['mechanics'] + r[k]['dynamics']:
        index[m['id']] = m
systems = {s['system']: s for s in r['ready']['systems']}


def sub(text, f):
    who = f.get('id') or f.get('sys') or f.get('path')
    assert isinstance(text, str) and text.count(f['find']) == 1, ('find', who, f['find'][:50])
    return text.replace(f['find'], f['replace'])


applied = 0
for f in load('fixes.json'):
    if 'id' in f:
        it = index[f['id']]
        it[f['field']] = f['value'] if 'value' in f else sub(it[f['field']], f)
    elif 'sys' in f and 'list' in f:
        lst = systems[f['sys']][f['list']]
        hit = [i for i, x in enumerate(lst) if f['find'] in x]
        assert len(hit) == 1, ('list', f['sys'], f['find'][:50], len(hit))
        lst[hit[0]] = sub(lst[hit[0]], f)
    elif 'sys' in f:
        s = systems[f['sys']]
        s[f['field']] = f['value'] if 'value' in f else sub(s[f['field']], f)
    else:
        node = r
        for p in f['path'][:-1]:
            node = node[p]
        last = f['path'][-1]
        node[last] = f['value'] if 'value' in f else sub(node[last], f)
    applied += 1

# --- чиста кімната: назв чужих ігор і торгових марок на сторінці немає ---
BRANDS = ['WWE', 'AEW', 'Fire Pro', 'XCOM', 'Titantron', 'Smackdown', 'SmackDown', 'Mixamo', 'Wasteland',
          'Rogue Trader', 'BG3', 'Baldur', 'GURPS', 'PbtA', 'Apocalypse World', 'D&D']
txt = json.dumps(r, ensure_ascii=False)
hits = [b for b in BRANDS if b in txt]
txt = re.sub(r'\b2K\b', '[гра-референс]', txt)
for b in hits:
    txt = txt.replace(b, 'екран виходу' if b == 'Titantron' else '[гра-референс]')
data = json.loads(txt)

# --- посилання на id, якого немає серед пунктів, з карти прибираємо ---
ids = set()
for k in ('match', 'career', 'show'):
    ids.update(m['id'] for m in data[k]['mechanics'])
    ids.update(d['id'] for d in data[k]['dynamics'])
dropped = []
for k in ('match', 'career', 'show'):
    for d in data[k]['dynamics']:
        dropped += [f for f in d['from'] if f not in ids]
        d['from'] = [f for f in d['from'] if f in ids]
    data[k]['aesthetics'] = [a for a in data[k]['aesthetics'] if a['target'] in ids]

blob = json.dumps(data, ensure_ascii=False).replace('</', '<\\/')
frag = io.open(os.path.join(HERE, 'template.html'), encoding='utf-8').read().replace('/*__DATA__*/null', blob)
# Каркас документа додає публікація: лишаємо <title>, <link>, <style> і тіло.
for tag in ('<!doctype html>\n', '<html lang="uk">\n', '<head>\n', '<meta charset="utf-8">\n',
            '<meta name="viewport" content="width=device-width,initial-scale=1">\n', '</head>\n', '<body>\n',
            '</body>\n', '</html>\n'):
    assert frag.count(tag) == 1, tag
    frag = frag.replace(tag, '')
os.makedirs(os.path.dirname(OUT), exist_ok=True)
io.open(OUT, 'w', encoding='utf-8', newline='\n').write(frag)

n = {k: (len(data[k]['mechanics']), len(data[k]['dynamics']), len(data[k]['aesthetics'])) for k in ('match', 'career', 'show')}
print('виправлень: %d; замінено назв: %s; прибрано посилань: %s' % (applied, hits, dropped))
print('механік / динамік / зв\'язків: %s' % n)
print('записано %s (%d байт)' % (os.path.relpath(OUT, ROOT), len(frag.encode('utf-8'))))
