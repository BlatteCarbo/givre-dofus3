import hashlib, json, pathlib, re, struct, zlib

OUT = pathlib.Path(__file__).parent
SOURCES = [
    pathlib.Path(r"%USERPROFILE%\AppData\Local\Mizan\app"),
]
MARK = bytes.fromhex("8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae")

def clean(s):
    return re.sub("mizan", "MZB", s, flags=re.I)

def run(p):
    b = p.read_bytes()
    info = {"source": str(p), "sha256": hashlib.sha256(b).hexdigest(), "size": len(b)}
    pe = struct.unpack_from("<I", b, 0x3c)[0]
    magic = struct.unpack_from("<H", b, pe + 24)[0]
    dd = pe + 24 + (112 if magic == 0x20b else 96)
    info["clr_directory"] = struct.unpack_from("<II", b, dd + 14 * 8)
    section_count = struct.unpack_from("<H", b, pe + 6)[0]
    optional_size = struct.unpack_from("<H", b, pe + 20)[0]
    sections = []
    for i in range(section_count):
        so = pe + 24 + optional_size + i * 40
        name = b[so:so+8].rstrip(b"\0").decode("ascii", "replace")
        vs, va, rs, ro = struct.unpack_from("<IIII", b, so+8)
        sections.append(dict(name=name, virtual_size=vs, virtual_address=va, raw_size=rs, raw_offset=ro))
    info["sections"] = sections
    pos = b.find(MARK)
    info["bundle_marker"] = pos
    strings = re.findall(rb"[\x20-\x7e]{6,}", b)
    interesting = [s.decode("ascii") for s in strings if re.search(rb"(?i)(mizan|version|reactor|confuser|themida|vmprotect|combat|auction|script|treasure|kama|snow|bank|breed|dungeon|bot)", s)]
    (OUT / "strings").mkdir(exist_ok=True)
    (OUT / "strings" / (clean(p.name) + ".txt")).write_text("\n".join(interesting), encoding="utf-8")
    wide = [s.decode("utf-16le") for s in re.findall(rb"(?:[\x20-\x7e]\x00){5,}", b)]
    (OUT / "strings" / (clean(p.name) + ".wide.txt")).write_text("\n".join(wide), encoding="utf-8")
    (OUT / "strings" / (clean(p.name) + ".all.txt")).write_text("\n".join(s.decode("ascii") for s in strings), encoding="utf-8")
    if pos >= 8:
        off = struct.unpack_from("<q", b, pos - 8)[0]
        info["bundle_offset"] = off
        cursor = off
        def take(fmt):
            nonlocal cursor
            val = struct.unpack_from(fmt, b, cursor)
            cursor += struct.calcsize(fmt)
            return val[0] if len(val) == 1 else val
        def string():
            nonlocal cursor
            n = shift = 0
            while True:
                c = b[cursor]
                cursor += 1
                n |= (c & 127) << shift
                if c < 128:
                    break
                shift += 7
            s = b[cursor:cursor+n].decode("utf-8")
            cursor += n
            return s
        try:
            major, minor, count = take("<III")
            info.update(bundle_version=[major, minor], bundle_count=count, bundle_id=string())
            if major >= 2:
                info["bundle_extra"] = take("<qqqqQ")
            entries = []
            for _ in range(count):
                offset, size = take("<qq")
                compressed = take("<q") if major >= 6 else 0
                kind = take("<B")
                name = string()
                entries.append(dict(name=clean(name), offset=offset, size=size, compressed=compressed, kind=kind))
                if re.search(r"(?i)(mizan|\.deps\.json$|\.runtimeconfig\.json$)", name):
                    data = b[offset:offset+(compressed or size)]
                    if compressed:
                        data = zlib.decompress(data, -15)
                    target = OUT / "payload" / clean(p.stem) / clean(name)
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(data)
            info["entries"] = entries
        except Exception as e:
            info["bundle_error"] = repr(e)
    return info

report = []
for source in SOURCES:
    for p in list(source.glob("Mizan*.*")) + list((source / "WebAuth").glob("Mizan*.dll")):
        try:
            report.append(run(p))
        except Exception as e:
            report.append({"source": str(p), "error": repr(e)})
(OUT / "inventory.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps([{k:v for k,v in r.items() if k != "entries"} for r in report], indent=2))
