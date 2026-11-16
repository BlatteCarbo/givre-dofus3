"""MZP23 : écart entre l'API Lua documentée par le portail Givre (texte copié par l'utilisateur) et les fonctions enregistrées par le
moteur Lua du clone (GivreClone.App/Services/GivreLuaHost*.cs).

Usage : python -I tools/MZP23_LuaApiGap.py "<fichier texte du portail>"
Sortie : artifacts/MZP23REAEvidence/lua-api-gap.json + résumé.

Méthode (approximative, dite telle) :
 - documenté : lignes de signature `module:fonction(args)` ou `fonction(args)` sans guillemets/affectation (les lignes d'exemple en
   contiennent presque toujours) ; une signature est comptée une fois par (module, nom) ;
 - implémenté : noms `Globals["x"]`, `<var>["x"] =` et `AddFn("x"...)`-like dans GivreLuaHost*.cs ; le module est déduit du nom de
   variable (map, combat, bank, _bankModule -> bank, ...). Comparaison insensible à la casse et aux '_' (le moteur est tolérant).
 - une fonction globale (sans module) documentée est aussi acceptée si elle existe sous n'importe quel module (le moteur expose des alias).
Limite : « présent » = enregistré, pas « correct ». Aucun comportement n'est vérifié ici.
"""
import json
import pathlib
import re
import sys

root = pathlib.Path(__file__).resolve().parent.parent
doc = pathlib.Path(sys.argv[1]).read_text(encoding='utf8', errors='replace')

MODULES = ['map', 'combat', 'fightAction', 'fightChallenge', 'fightCharacter', 'fightBasic', 'fightDebug', 'fightSlave', 'team', 'inventory',
           'bank', 'exchange', 'trade', 'craft', 'merchant', 'zaap', 'zaapi', 'job', 'sale', 'npc', 'chat', 'character', 'mount', 'stats', 'quest',
           'achievement', 'document', 'teleport', 'dungeon', 'treasure', 'data', 'memory', 'sharedMemory', 'http', 'developer', 'global', 'config',
           'givreController', 'supervisor', 'bots', 'accounts', 'proxies', 'anomaly']
modset = {m.lower() for m in MODULES}


def norm(s):
    return s.replace('_', '').lower()


# ---------------- documenté ----------------
sig = re.compile(r'^(?:(?P<mod>[A-Za-z]\w*)[:.])?(?P<fn>[A-Za-z]\w*)\((?P<args>[^()"\'`=]*)\)(?:\s*→.*)?$')
documented = {}
for line in doc.splitlines():
    line = line.strip()
    if not line or len(line) > 160:
        continue
    m = sig.match(line)
    if not m:
        continue
    mod = (m['mod'] or '').lower()
    fn = m['fn']
    if m['mod'] and mod not in modset:
        continue                      # objet.méthode() d'un exemple (b:xxx, bot.xxx) : pas un module de l'API
    if fn in ('if', 'for', 'while', 'function', 'return', 'print') and not mod:
        continue
    documented.setdefault((mod, norm(fn)), fn)

# ---------------- implémenté ----------------
impl_by_mod = {}
all_impl = set()
varmod = re.compile(r'(\w+)\["(\w+)"\]\s*=')
glob = re.compile(r'Globals\["(\w+)"\]\s*=')
SUP_MODULES = {'bots': 'bots', 'accounts': 'accounts', 'proxies': 'proxies', 'controller': 'givrecontroller'}
sup = root / 'GivreClone.App/Services/Supervisor'
for p in sorted(sup.glob('SupervisorLuaHost.cs')) + sorted(sup.glob('BotSupervisorModule.cs')):
    t = p.read_text(encoding='utf8', errors='replace')
    if p.name == 'BotSupervisorModule.cs':
        for m in re.finditer(r'Verb\("(\w+)"', t):
            impl_by_mod.setdefault('supervisor', set()).add(norm(m[1])); all_impl.add(norm(m[1]))
        continue
    parts = re.split(r'private void Bind(\w+)\(', t)
    for i in range(1, len(parts), 2):
        mod = SUP_MODULES.get(parts[i].lower())
        for m in re.finditer(r'Verb\(m, "(\w+)"', parts[i + 1]):
            if mod: impl_by_mod.setdefault(mod, set()).add(norm(m[1]))
            all_impl.add(norm(m[1]))
lotm = root / 'GivreClone.App/Services/LotM/LotMLuaModule.cs'
if lotm.exists():   # modules lot M : Add(<table>, "nom", ...) ; <table> = m (module de la section BindXxx) ou le nom d'un module passé en paramètre
    parts = re.split(r'void Bind(\w+)\(', lotm.read_text(encoding='utf8', errors='replace'))
    for i in range(1, len(parts), 2):
        for m in re.finditer(r'Add\((\w+), "(\w+)"', parts[i + 1]):
            mod = parts[i].lower() if m[1] == 'm' else m[1].lower()
            impl_by_mod.setdefault(mod, set()).add(norm(m[2])); all_impl.add(norm(m[2]))
for p in sorted((root / 'GivreClone.App/Services').glob('GivreLuaHost*.cs')):
    t = p.read_text(encoding='utf8', errors='replace')
    for m in glob.finditer(t):
        impl_by_mod.setdefault('', set()).add(norm(m[1]))
        all_impl.add(norm(m[1]))
    for m in varmod.finditer(t):
        var = m[1].lstrip('_')
        var = re.sub(r'Module$', '', var).lower()
        if var in ('globals',):
            continue
        impl_by_mod.setdefault(var, set()).add(norm(m[2]))
        all_impl.add(norm(m[2]))

# Borne haute : n'importe quel littéral "nom" dans le moteur Lua (enregistrement par assistant, alias, tables construites autrement).
literals = set()
if lotm.exists(): literals |= {norm(x) for x in re.findall(r'"([A-Za-z]\w{2,})"', lotm.read_text(encoding='utf8', errors='replace'))}
for p in sorted((root / 'GivreClone.App/Services').glob('GivreLuaHost*.cs')) + sorted((root / 'GivreClone.App/Services/Supervisor').glob('*.cs')):
    literals |= {norm(x) for x in re.findall(r'"([A-Za-z]\w{2,})"', p.read_text(encoding='utf8', errors='replace'))}

rows, missing = [], {}
for (mod, fn), orig in sorted(documented.items()):
    present = bool(fn in impl_by_mod.get(mod, set()) or (not mod and fn in all_impl) or (mod and fn in impl_by_mod.get('', set())))
    rows.append({'module': mod, 'name': orig, 'present': present, 'present_anywhere': fn in literals})
    if not present and fn not in literals:
        missing.setdefault(mod or '(global)', []).append(orig)

out = root / 'artifacts/MZP23REAEvidence/lua-api-gap.json'
out.write_text(json.dumps({'documented': len(rows), 'present_low': sum(r['present'] for r in rows), 'present_high': sum(r['present'] or r['present_anywhere'] for r in rows), 'missing_by_module': missing}, indent=1, ensure_ascii=False), encoding='utf8')
lo = sum(r['present'] for r in rows); hi = sum(r['present'] or r['present_anywhere'] for r in rows)
print(f"documentées {len(rows)} ; présentes entre {lo} (borne basse, module attribué) et {hi} (borne haute, nom trouvé n'importe où) ; absentes sûres {len(rows) - hi}")
for m, v in sorted(missing.items(), key=lambda kv: -len(kv[1])):
    print(f'  {m:18} absentes {len(v):3} / {sum(1 for r in rows if (r["module"] or "(global)") == m):3}  ex: {", ".join(v[:5])}')
