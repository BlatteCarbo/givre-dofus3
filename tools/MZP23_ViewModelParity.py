"""MZP23 : compare les ViewModels/membres de l'original (metadonnees de bindings Avalonia lues dans un dump memoire
d'une copie isolee de GivreClone.exe) avec les sources du clone.

Entree : artifacts/MZP23REAEvidence/dump-strings/ascii.txt (chaines ASCII du dump).
Sortie : artifacts/MZP23REAEvidence/viewmodel-parity.json + resume sur stdout.

Regle de correspondance d'un membre M : present si M, m, _m existent comme identifiant dans GivreClone.App/**/*.cs ;
FooCommand est genere par [RelayCommand] depuis Foo()/FooAsync() ; une propriete Foo est generee par un champ _foo/foo.
Limite : correspondance par identifiant global (pas par type) -> peut sous-estimer les manques.
"""
import json
import pathlib
import re

root = pathlib.Path(__file__).resolve().parent.parent
text = (root / 'artifacts/MZP23REAEvidence/dump-strings/ascii.txt').read_text(encoding='utf8', errors='replace')
pat = re.compile(r'GivreClone\.App\.([A-Za-z]+)\.([A-Za-z0-9_]+),GivreClone\.([A-Za-z0-9_]+)!(Property|Getter|Setter|Field)')
orig = {}
for m in pat.finditer(text):
    ns, typ, mem, _kind = m.groups()
    orig.setdefault(f'{ns}.{typ}', set()).add(mem)

code = '\n'.join(p.read_text(encoding='utf8', errors='replace') for p in (root / 'GivreClone.App').rglob('*.cs'))
idents = set(re.findall(r'[A-Za-z_][A-Za-z0-9_]*', code))


def has(m):
    cands = {m, m[0].lower() + m[1:], '_' + m[0].lower() + m[1:]}
    if m.endswith('Command'):
        b = m[:-7]
        cands |= {b, b + 'Async'}
    return bool(cands & idents)


rep = {}
for t, mems in sorted(orig.items()):
    typ = t.split('.')[-1]
    exists = typ in idents
    missing = sorted(m for m in mems if not has(m)) if exists else sorted(mems)
    rep[t] = {'orig_members': len(mems), 'clone_type_exists': exists, 'missing_members': missing}

(root / 'artifacts/MZP23REAEvidence/viewmodel-parity.json').write_text(
    json.dumps(rep, indent=1, ensure_ascii=False), encoding='utf8')
nt = len(rep)
te = sum(v['clone_type_exists'] for v in rep.values())
nm = sum(v['orig_members'] for v in rep.values())
mm = sum(len(v['missing_members']) for v in rep.values())
print(f'types originaux {nt}, presents dans le clone {te}; membres {nm}, absents du clone {mm}')
for t, v in rep.items():
    if not v['clone_type_exists'] or v['missing_members']:
        print(' ', t, 'TYPE ABSENT' if not v['clone_type_exists'] else '', v['missing_members'][:8])
