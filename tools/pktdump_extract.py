#!/usr/bin/env python3
"""Extrait les paquets relevés par la « Capture de découverte » (PacketDump) d'un transport-*.log.

Usage :
  python tools/pktdump_extract.py <transport.log> [--name CharacterCreationRequest] [--dir C2S|S2C] [--json out.json]

Pour chaque paquet « [pkt-dbg] C->S|S->C Nom [code] N champ(s) <taille>o hex=... » : nom, sens, code, octets bruts (trames
sortantes <= 200 o) et arbre de champs décodé depuis l'hex (varint / len / sous-message / texte), numéros de champ BRUTS.
À lire avec data/fields.json : le numéro #N d'un champ est son clé dans le message ; les noms 4 lettres (fzju, gccm...) sont des
champs non nommés par le mapping. Ne devine jamais : compare avec DofusReverseIndexer/…/dofus-<build>.proto et les tables du jeu.
"""
import argparse, json, re, sys

LINE = re.compile(r'\[pkt-dbg\] (C->S|S->C) (\S+) \[(\w+)\]\s+(\d+) champ\(s\)(?:\s+(\d+)o)?(?: hex=([0-9A-Fa-f]+))?')


def varint(b, i):
    x = s = 0
    while True:
        c = b[i]; i += 1
        x |= (c & 0x7F) << s; s += 7
        if c < 0x80:
            return x, i


def parse(b, depth=0):
    out, i = [], 0
    try:
        while i < len(b):
            key, i = varint(b, i)
            f, w = key >> 3, key & 7
            if f == 0:
                raise ValueError
            if w == 0:
                v, i = varint(b, i); out.append({'f': f, 'varint': v})
            elif w == 2:
                n, i = varint(b, i)
                if i + n > len(b):
                    raise ValueError
                body = b[i:i + n]; i += n
                node = {'f': f, 'len': n}
                sub = parse(body, depth + 1) if depth < 6 and n else None
                try:
                    txt = body.decode('utf-8')
                    printable = txt.isprintable() and n > 0
                except UnicodeDecodeError:
                    printable = False
                if printable and (sub is None or len(txt) > 3 and not sub):
                    node['text'] = txt
                elif sub:
                    node['msg'] = sub
                else:
                    node['hex'] = body.hex().upper()
                    # varints packés ?
                    try:
                        j, vals = 0, []
                        while j < len(body):
                            v, j = varint(body, j); vals.append(v)
                        node['packed'] = vals
                    except IndexError:
                        pass
                out.append(node)
            elif w == 1:
                out.append({'f': f, 'fixed64': b[i:i + 8].hex().upper()}); i += 8
            elif w == 5:
                out.append({'f': f, 'fixed32': b[i:i + 4].hex().upper()}); i += 4
            else:
                raise ValueError
    except (ValueError, IndexError):
        return None
    return out


def frame_payload(raw):
    """Enveloppe sortante telle que journalisée : #4 { uid #1 ; #2 { #1 'type.ankama.com/<code>' ; #2 <charge utile> } }."""
    return parse(raw)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('log'); ap.add_argument('--name'); ap.add_argument('--dir', choices=['C2S', 'S2C'])
    ap.add_argument('--json')
    a = ap.parse_args()
    res = []
    for ln in open(a.log, encoding='utf-8', errors='replace'):
        m = LINE.search(ln)
        if not m:
            continue
        d, name, code, nf, size, hx = m.groups()
        if a.name and name != a.name:
            continue
        if a.dir and (d == 'C->S') != (a.dir == 'C2S'):
            continue
        item = {'dir': d, 'name': name, 'code': code, 'fields': int(nf), 'size': int(size) if size else None}
        if hx:
            raw = bytes.fromhex(hx)
            item['hex'] = hx.upper()
            item['tree'] = frame_payload(raw)
        res.append(item)
    if a.json:
        json.dump(res, open(a.json, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    for r in res:
        print(f"{r['dir']} {r['name']} [{r['code']}] {r['fields']} champ(s) {r['size'] or ''}")
        if r.get('tree'):
            print(json.dumps(r['tree'], ensure_ascii=False))
    print(f'-- {len(res)} paquet(s)', file=sys.stderr)


if __name__ == '__main__':
    main()
