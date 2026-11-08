#!/usr/bin/env python3
"""Build the emoji assets.

  emoji.txt        group<TAB>emoji<TAB>Chinese name, in Unicode order (emojibase-data, MIT)
  emoji_words.txt  word<TAB>emoji emoji ... for showing emoji after Chinese words (rime-ice, GPL-3.0)

Usage: python3 tools/build_emoji.py <emojibase zh/compact.json> <rime-ice opencc/emoji.txt>
"""
import json, os, sys

compact, words_file = sys.argv[1], sys.argv[2]
assets = os.path.join(os.path.dirname(__file__), '..', 'app', 'src', 'main', 'assets')

# 2 is skin tones and hair parts, which are not emoji on their own.
GROUPS = {0: 'smileys', 1: 'people', 3: 'nature', 4: 'food', 5: 'travel', 6: 'activities', 7: 'objects', 8: 'symbols', 9: 'flags'}

data = json.load(open(compact, encoding='utf8'))
rows = [e for e in data if e.get('group') in GROUPS]
rows.sort(key=lambda e: (e['group'], e.get('order', 0)))
known = set()
with open(os.path.join(assets, 'emoji.txt'), 'w', encoding='utf8') as f:
    for e in rows:
        known.add(e['unicode'])
        f.write(f"{e['group']}\t{e['unicode']}\t{e.get('label', '')}\n")

count = 0
with open(os.path.join(assets, 'emoji_words.txt'), 'w', encoding='utf8') as f:
    for line in open(words_file, encoding='utf8'):
        parts = line.rstrip('\n').split('\t')
        if len(parts) != 2:
            continue
        word, rest = parts
        emoji = [x for x in rest.split(' ')[1:] if x in known][:3]
        if emoji and word not in known:
            f.write(f"{word}\t{' '.join(emoji)}\n")
            count += 1
print(len(rows), 'emoji,', count, 'words')
