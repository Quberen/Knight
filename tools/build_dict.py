#!/usr/bin/env python3
"""Build app/src/main/assets/dict.txt from the rime-ice dictionaries.

Usage: python3 tools/build_dict.py <dir with 8105.dict.yaml and base.dict.yaml> [max_words]
Output lines: word<TAB>pinyin syllables (space separated)<TAB>weight
"""
import os, sys

src = sys.argv[1]
max_words = int(sys.argv[2]) if len(sys.argv) > 2 else 150000
out = os.path.join(os.path.dirname(__file__), '..', 'app', 'src', 'main', 'assets', 'dict.txt')

def read(name):
    rows = []
    for line in open(os.path.join(src, name), encoding='utf8'):
        if line.startswith('#'):
            continue
        p = line.rstrip('\n').split('\t')
        if len(p) == 3 and p[2].isdigit() and all(s.isalpha() and s.isascii() for s in p[1].split(' ')):
            rows.append((p[0], p[1], int(p[2])))
    return rows

chars = read('8105.dict.yaml')
words = [r for r in read('base.dict.yaml') if len(r[0]) > 1]
words.sort(key=lambda r: -r[2])
words = words[:max_words]

seen = set()
with open(out, 'w', encoding='utf8') as f:
    for w, py, wt in chars + words:
        if (w, py) in seen:
            continue
        seen.add((w, py))
        f.write(f'{w}\t{py}\t{wt}\n')
print(len(seen), 'entries ->', os.path.normpath(out))
