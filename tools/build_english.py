#!/usr/bin/env python3
"""Build app/src/main/assets/english.txt: common English words with frequencies.

Frequencies come from wordfreq (data CC-BY-SA 4.0); capitalisation of names
like "Monday" or "iPhone" from rime-ice's en.dict.yaml.

Usage: python3 tools/build_english.py <large_en.msgpack.gz> <en.dict.yaml> [min_zipf]
Output lines: word<TAB>zipf, most common first.
"""
import gzip, os, re, sys
import msgpack

freq_file, rime_file = sys.argv[1], sys.argv[2]
min_zipf = float(sys.argv[3]) if len(sys.argv) > 3 else 2.3
out = os.path.join(os.path.dirname(__file__), '..', 'app', 'src', 'main', 'assets', 'english.txt')

# Common misspellings and apostrophe-less contractions: keep them out, so they get corrected.
NOT_WORDS = set('''
alot teh recieve definately seperate untill wich becuase thier occured accomodate beleive goverment
tommorow tomorow wierd freind truely adress arguement begining calender comming completly concious
embarass existance finaly foriegn gaurd happend harrass immediatly independant knowlege lenght liason
millenium neccessary noticable occassion persue posession prefered publically realy recomend refered
relevent rember resistence sence sieze succesful supercede suprise tendancy therefor threshhold tounge
truley unforseen wether whereever writting acheive agressive apparantly basicly beggining bizzare
buisness catagory cemetary changable collegue commited concensus definate dissapoint ecstacy enviroment
excercise familar fourty gratefull guage hieght humourous ignorence intrest jewelery judgement libary
maintainance mispell neice nieghbor occurence peice persistant posible potatos preceeding presense
questionaire reccomend religous remeber rythm sargent shedule similiar sincerly speach strenght
suceed tatoo threshhold tounge twelth tyrany vaccuum vehical visable wellcome whitch yeild
dont im ive youre theyre didnt doesnt isnt wasnt arent couldnt shouldnt wouldnt havent hasnt hadnt
werent thats whats theres heres wheres whos hows youve weve theyve youll theyll itll shes wont cant
aint yall
'''.split())

data = msgpack.load(gzip.open(freq_file), raw=False)
ok = re.compile(r"^[a-z]+('[a-z]+)?$")

# Names that rime-ice only lists capitalised.
lower_seen, cased = set(), {}
for line in open(rime_file, encoding='utf8'):
    if line.startswith('#') or '\t' not in line:
        continue
    w = line.split('\t')[0].strip()
    if w.lower() == w:
        lower_seen.add(w)
    elif re.match(r"^[A-Za-z]+$", w):
        cased.setdefault(w.lower(), w)

rows = []
for i, bucket in enumerate(data[1:]):
    zipf = 9 - i / 100
    if zipf < min_zipf:
        break
    for w in bucket:
        if not ok.match(w) or w in NOT_WORDS:
            continue
        shown = w
        if w == 'i':
            shown = 'I'
        elif w in cased and w not in lower_seen:
            shown = cased[w]
        rows.append((shown, zipf))

with open(out, 'w', encoding='utf8') as f:
    for w, z in rows:
        f.write(f'{w}\t{z:.2f}\n')
print(len(rows), 'words ->', os.path.normpath(out))
