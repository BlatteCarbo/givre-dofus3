import json, re
R = '%USERPROFILE%/Videos/GIVRE/GivreWork-Integration-20261020/'
f = json.load(open(R + 'data/fields.json', encoding='utf-8'))
m = json.load(open(R + 'data/matching_current.json', encoding='utf-8'))
code_of = {}
for k, v in m.items():
    if v.startswith('MessageType.'):
        code_of[v.split('.', 1)[1]] = k.split('/')[-1]
words = {'flag','fail','from','gold','game','goal','gift','fire','full','gain','gate','gear','grid','free','fall','fame','fast','feed','file','find','fish','fist','flow','fold','food','foot','form','fuse','gaps','gems','give','glow','good','grab','grow'}
obf = re.compile(r'^[fg][a-z]{3}$')
def unnamed(n):
    return [x for x in f.get(n, {}) if obf.match(x) and x not in words]

cats = [
 ('Compte & personnage (création, suppression, pseudo, secret)', r'CharacterDeletion|CharacterCreation|CharacterName|Remodel|CharacterReplay|Nickname|Secret|Shield|PrepareCharacter'),
 ('Boutique, ogrines, abonnement, Bourse', r'Shop|Store|Ogrine|Bak|Haapi|Purchase|Premium|Subscription|Bourse|Money'),
 ('Échange & commerce entre joueurs / PNJ', r'Exchange|Trade'),
 ('HDV', r'Bid|Market'),
 ('Dialogue PNJ', r'Npc|Dialog'),
 ('Chasse au trésor', r'Treasure|Hunt|Clue'),
 ('Métiers, ateliers, craft', r'Craft|Job|Recipe|Skill|Workshop'),
 ('Groupe, amis, contacts', r'Party|Friend|Acquaint|Ignored|Spouse|Contact'),
 ('Guilde', r'Guild'),
 ('Alliance & prismes', r'Alliance|Prism'),
 ('Montures & enclos', r'Mount|Paddock'),
 ('Havre-sac, maisons, coffres', r'HavenBag|House|Lockable|Storage'),
 ('Quêtes', r'Quest'),
 ('Succès', r'Achievement'),
 ('Almanax, anomalies, bonus', r'Almanach|Anomal|Bonus'),
 ('Kolizéum, arène, lobby', r'Kolizeum|Arena|Lobby'),
 ('Chat & messages', r'Chat|Mail|Message'),
]
out = ['# Inventaire du mapping — 21/10/2026', '',
       f'Source : `data/fields.json` ({len(f)} messages décrits) et `data/matching_current.json` ({len(code_of)} messages avec code réseau).',
       'Un champ « non nommé » est un champ dont le nom est obfusqué (4 lettres commençant par f ou g : `fzju`, `gccm`…) : son numéro est connu, pas son sens.',
       'Le décompte est approximatif (heuristique sur le motif du nom).', '']
seen = set()
for title, rx in cats:
    names = sorted(n for n in code_of if re.search(rx, n) and n not in seen)
    seen.update(names)
    tot_un = sum(len(unnamed(n)) for n in names)
    out += [f'## {title}', '', f'{len(names)} message(s), {tot_un} champ(s) non nommé(s).', '',
            '| Message | Code | Champs nommés | Champs non nommés |', '|---|---|---|---|']
    for n in names:
        fl = f.get(n, {})
        un = unnamed(n)
        named = [k for k in fl if k not in un]
        out.append(f'| {n} | {code_of[n]} | {", ".join(f"{k}#{fl[k]}" for k in named) or "—"} | {", ".join(f"{k}#{fl[k]}" for k in un) or "—"} |')
    out.append('')
rest = sorted(n for n in code_of if n not in seen)
out += ['## Autres messages (hors catégories ci-dessus)', '', f'{len(rest)} message(s) : ' + ', '.join(rest[:400]) + (' …' if len(rest) > 400 else ''), '']
open(R + 'docs/MAPPING_INVENTORY_20261021.md', 'w', encoding='utf-8').write('\n'.join(out))
print(len(out), 'lignes')
