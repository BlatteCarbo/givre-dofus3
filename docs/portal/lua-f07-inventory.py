"""Inventaire statique F07, sans client : python docs/portal/lua-f07-inventory.py.

Déplie les foreach de noms/tuples littéraux, suit les tables publiées et les
helpers de liaison. Présence = liaison source, pas preuve d'exécution en jeu.
Les fonctions de Lua standard et les variantes de casse/snake_case sont exclues.
"""
import hashlib
import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = pathlib.Path(__file__).with_name("lua-f07-inventory.json")


def masked(text):
    # Même longueur, pour rechercher les délimiteurs sans compter chaînes/commentaires.
    return re.sub(r'//[^\n]*|/\*.*?\*/|@?"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'',
                  lambda m: " " * len(m[0]), text, flags=re.S)


def closing(text, start, op="{", cl="}"):
    view = masked(text)
    depth = 0
    for i in range(start, len(view)):
        if view[i] == op:
            depth += 1
        elif view[i] == cl:
            depth -= 1
            if depth == 0:
                return i
    raise ValueError(text[start:start + 100])


def expand(text):
    pattern = r'foreach\s*\(var\s+(\w+|\([\w, ]+\))\s+in\s+new\[\]\s*\{'
    while match := re.search(pattern, text):
        end_array = closing(text, match.end() - 1)
        end_head = text.index(")", end_array) + 1
        begin = end_head
        while text[begin].isspace():
            begin += 1
        if text[begin] == "{":
            end = closing(text, begin) + 1
            body = text[begin + 1:end - 1]
        else:
            # Délimiteur ; hors chaînes/commentaires et parenthèses.
            view = masked(text)
            depth = 0
            end = begin
            while end < len(text):
                c = view[end]
                if c in "({[": depth += 1
                if c in ")}]": depth -= 1
                if c == ";" and depth == 0: break
                end += 1
            end += 1
            body = text[begin:end]
        names = re.findall(r'\w+', match[1])
        literals = re.findall(r'"([^"\n]*)"|(-?\d+)', text[match.end():end_array])
        values = [int(number) if number else string for string, number in literals]
        if not values or len(values) % len(names):
            text = text[:match.start()] + "/* foreach non littéral */" + text[end_head:]
            continue
        bodies = []
        for n in range(0, len(values), len(names)):
            b = body
            for key, val in zip(names, values[n:n + len(names)]):
                b = re.sub(r'\b' + key + r'\b', lambda _: json.dumps(val), b)
            # "getCost" + "Vitality" -> "getCostVitality".
            while re.search(r'"([^"\n]*)"\s*\+\s*"([^"\n]*)"', b):
                b = re.sub(r'"([^"\n]*)"\s*\+\s*"([^"\n]*)"', lambda m: '"' + m[1] + m[2] + '"', b)
            bodies.append(b)
        text = text[:match.start()] + "\n".join(bodies) + text[end:]
    return text


rows = {}


def add(module, name, source, unavailable=False):
    if not module or name.startswith("__"):
        return
    key = name if module == "Globals" else module + ":" + name
    row = rows.setdefault(key, {"api": key, "sources": [], "unavailable": False})
    if source not in row["sources"]:
        row["sources"].append(source)
    row["unavailable"] |= unavailable


paths = sorted((ROOT / "GivreClone.App/Services").glob("GivreLuaHost*.cs"))
paths += sorted((ROOT / "GivreClone.App/Services/LotM").glob("LotMLuaModule*.cs"))
paths += sorted((ROOT / "GivreClone.App/Services").glob("LuaSnowCompat*.cs"))
for path in paths:
    source = path.relative_to(ROOT).as_posix()
    text = path.read_text(encoding="utf-8-sig")
    # Retirer les commentaires avant de déplier les helpers.
    text = re.sub(r'//[^\n]*|/\*.*?\*/', "", text, flags=re.S)
    text = expand(text)
    # Méthodes membres : une variable locale réutilisée dans une autre méthode n'est pas une même table.
    chunks = re.split(r'\n    (?:private|public|internal) (?:static )?[^\n=;{]+? (\w+)\([^{};]*?\)\s*(?:=>|\{)', text)
    for i in range(1, len(chunks), 2):
        method, body = chunks[i:i + 2]
        src = source + "#" + method
        mods = {"Globals": "Globals", "_bankModule": "bank"}
        for m in re.finditer(r'(\w+)\s*=\s*(?:s|script|_s)\.Globals\s*;', body): mods[m[1]] = "Globals"
        for m in re.finditer(r'(\w+)\s*=\s*GetOrCreateTable\(s,\s*"(\w+)"', body): mods[m[1]] = m[2]
        for m in re.finditer(r'(?:Globals|(?<!\w)[gG])\["(\w+)"\]\s*=\s*DynValue.NewTable\((\w+)\)', body): mods.setdefault(m[2], m[1])
        for m in re.finditer(r'(\w+)\s*=\s*(?:s\.Globals|[gG])\.Get\("(\w+)"\)', body):
            # variable V du DynValue, puis table dérivée du même nom sans V.
            mods[m[1].removesuffix("V").removesuffix("Dv")] = m[2]
        if method == "BindCombatC2": mods["combat"] = "combat"
        if method in ("BindMapLotB", "BindMapLotJ"): mods["map"] = "map"
        if method == "BindSnowDeveloper": mods["dev"] = "developer"
        if method == "BindSnowD2Data": mods["data"] = "data"
        if "LotMLuaModule" in source:
            name = method.removeprefix("Bind")
            mods["m"] = name[:1].lower() + name[1:]
            mods.update({x: x for x in ["job", "quest", "bank", "sale", "npc"]})
            if method == "BindCraftPlan": mods["m"] = "craft"
        if method == "BindLotF": mods["ch"] = "character"
        if method == "BindComfortStatus": mods["ch"] = "character"
        if method == "BindComfortChallenges": mods["fc"] = "fightChallenge"
        if method == "BindPlatform": mods.update(dev="developer", team="team")
        if method == "BindRoadCreator": mods.update(io="io", handle="io.open()")
        if method == "AccountProxy": mods["t"] = "accountProxy"
        if method == "BindSnowController": mods["t"] = "accountProxy"

        # Appels Fill/Add sur table, affectations directes. Les champs constants sont exclus.
        pattern = r'(?:Fill|Add)\((\w+),\s*"(\w+)"\s*,|(?:(\w+)\.)?(\w+)\["(\w+)"\]\s*='
        for m in re.finditer(pattern, body):
            var = m[1] or m[4]
            name = m[2] or m[5]
            module = "Globals" if var == "Globals" else mods.get(var)
            if not module:
                continue
            tail = body[m.end():]
            # Expression entière de la liaison jusqu'au ; (ou début de callback).
            head = tail[:tail.find(";") + 1] if ";" in tail else tail[:200]
            if re.match(r'\s*DynValue.NewTable\(', tail) or re.match(r'\s*DynValue\.(False|True|NewNumber|NewString|NewBoolean)\b', tail): continue
            # La métatable ne constitue pas une fonction publique.
            if var in ("mt", "r", "t2"): continue
            unavailable = any(x in head for x in ["UnsupportedLua(", "Stub(", "Neutral(", "M(", "Refused(", "Refuse("])
            add(module, name, src, unavailable)
        for m in re.finditer(r'GetOrCreateTable\(s, "(\w+)"\)\["(\w+)"\]\s*=', body): add(m[1], m[2], src)
        for m in re.finditer(r'Globals.Get\("(\w+)"\).Table\["(\w+)"\]\s*=', body): add(m[1], m[2], src)
        # Helpers locaux dont la cible est connue.
        target = "character" if method == "BindLotB" else "global" if method == "BindSnowGlobal" else None
        for m in re.finditer(r'\bAlias\("(\w+)"', body): add(target, m[1], src)
        if method == "BindLotF":
            for m in re.finditer(r'\bAdd\("(\w+)"', body): add("character", m[1], src)
        if method == "BindSnowCharacter":
            for m in re.finditer(r'\bBase\("(\w+)"', body): add("character", m[1], src)
        if method == "BindSnowConfig":
            for m in re.finditer(r'(?:IntPair|ListPair)\("(\w+)", "(\w+)"', body):
                add("config", m[1], src); add("config", m[2], src)
        for m in re.finditer(r'BindRamVerbs\(s, ct, (\w+),.*?((?:"\w+"\s*,\s*){4}"\w+")\)', body):
            for n in re.findall(r'"(\w+)"', m[2]): add(mods.get(m[1]), n, src)

# Alias globaux d'une API indisponible (affectations de callbacks capturés).
if "leaveTutorial" in rows:
    rows["leaveTutorial"]["unavailable"] = True

# Alias de tables (même instance) ; ne pas compter deux fois les variantes de casse.
for key, row in list(rows.items()):
    if key.startswith("data:"):
        add("d2data", key.split(":", 1)[1], "GivreLuaHost.Snow.cs#alias-data", row["unavailable"])

doc = json.loads((ROOT / "docs/portal/lua-api-doc.json").read_text(encoding="utf-8-sig"))
documented = set()
callbacks = set()
for cat in doc["categories"]:
    for method in cat.get("methods", []):
        for sig in method.get("sig", []):
            key = sig.get("ns", "") + sig["name"]
            if cat["id"] == "callbacks": callbacks.add(key)
            else: documented.add(key)


def norm(key):
    # Seulement les modules de compatibilité tolèrent les variantes de nom.
    return key.replace("_", "").lower() if ":" in key else key


bound = {norm(k) for k in rows}
docs = {norm(k) for k in documented}
for row in rows.values(): row["documented"] = norm(row["api"]) in docs
result = {
    "method": "Analyse statique des liaisons et helpers littéraux ; liaison != validation serveur. Voir F07_LUA_AUDIT_20261024.md.",
    "sourceHashes": {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in paths},
    "documentedCallbacks": sorted(callbacks),
    "documentedButAbsent": sorted(k for k in documented if norm(k) not in bound),
    "exposedButUndocumented": sorted(r["api"] for r in rows.values() if not r["documented"]),
    "apis": sorted(rows.values(), key=lambda r: r["api"].lower()),
}
OUT.write_bytes((json.dumps(result, ensure_ascii=False, indent=2) + "\n").replace("\n", "\r\n").encode("utf-8"))
print(f"Liaisons: {len(rows)} ; absentes: {len(result['documentedButAbsent'])} ; non documentées: {len(result['exposedButUndocumented'])}")
print("Absentes:", ", ".join(result["documentedButAbsent"]))
