"""Génère GivreClone.App/Data/resource_map.json depuis l'API publique DofusDB (/skills et /recoltables2).

Idée et règles de tri reprises de dofus-miniatures (tools/generate-resource-map.py, avec l'accord de son auteur) : métiers de récolte
Bûcheron (2), Mineur (24), Alchimiste (26), Paysan (28), Pêcheur (36) et l'eau (métier 1, objet 311) ; chaque ressource garde son niveau de récolte minimal.
Aucun texte ni image de l'API n'est copié au-delà du nom français de la ressource, de son id, de son icône et de sa position (carte, coordonnées, quantité).

Utilisation : python tools/gen_resource_map.py [--out GivreClone.App/Data/resource_map.json]
"""
import argparse
import collections
import json
import sys
import time
import urllib.parse
import urllib.request

API = "https://api.dofusdb.fr"
JOBS = {1, 2, 24, 26, 28, 36}


def get(path, **params):
    url = API + path + "?" + urllib.parse.urlencode(params, safe="$")
    req = urllib.request.Request(url, headers={"User-Agent": "GivreWork-datagen/1.0"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.loads(r.read().decode("utf-8"))


def pages(path, **params):
    out, skip = [], 0
    while True:
        j = get(path, **params, **{"$limit": 50, "$skip": skip})
        out += j["data"]
        skip += len(j["data"])
        if not j["data"] or skip >= j["total"]:
            return out, j["total"]
        time.sleep(0.15)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="GivreClone.App/Data/resource_map.json")
    args = ap.parse_args()
    skills, total = pages("/skills", lang="fr")
    assert len(skills) == total, "skills incomplets"
    catalog = {}
    for s in skills:
        job, item_id = s["parentJobId"], s["gatheredRessourceItem"]
        if job not in JOBS or (job == 1 and item_id != 311):
            continue
        item = s.get("item")
        if not item or not item.get("iconId"):
            continue   # compétence sans objet récolté exploitable
        entry = [item_id, job, s["levelMin"], item["iconId"], item["name"]["fr"]]
        if item_id not in catalog or entry[2] < catalog[item_id][2]:
            catalog[item_id] = entry
    maps, total = pages("/recoltables2", **{"$sort[id]": 1})
    assert len(maps) == total, "recoltables2 incomplet"
    nodes, seen = [], set()
    for row in maps:
        mid, pos = row["id"], row["pos"]
        assert mid > 0 and mid not in seen
        seen.add(mid)
        counts = collections.Counter()
        for p in row["quantities"]:
            counts[p["item"]] += p["quantity"]
        for item_id, n in counts.items():
            if item_id in catalog:
                nodes.append([mid, pos["worldMap"], pos["posX"], pos["posY"], item_id, n, 0 if pos["outdoor"] else 1])
    nodes.sort(key=lambda n: (n[4], -n[5], n[0]))
    res = sorted(catalog.values(), key=lambda r: (r[1], r[2], r[0]))
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        json.dump({"source": "DofusDB /skills + /recoltables2", "resources": res, "nodes": nodes}, f, ensure_ascii=False, separators=(",", ":"))
    print(f"{len(res)} ressources, {len(nodes)} couples carte/ressource ({len(maps)} cartes)", file=sys.stderr)


if __name__ == "__main__":
    main()
