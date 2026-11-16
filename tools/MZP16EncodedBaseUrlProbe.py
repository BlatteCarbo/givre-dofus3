"""Bounded offline search for the updater's documented XOR-encoded BaseUrl.

This is only evidence about a detached setup, never evidence of an authenticated
licensing session or a license bypass. Does not execute the installer or connect
to any service. Output excludes query strings and fragments.
"""
from pathlib import Path
from urllib.parse import urlsplit
import hashlib
import json
import pefile

ROOT = Path(__file__).resolve().parents[1] / "offline-evidence" / "setup-1.3.1-4D4D6458"
FILE = ROOT / "GivreClone-Setup-1.3.1.exe"
SHA = "4D4D6458A0B808173749EB98D9FC3B199748B707CEFD8C7319809769C1DF6E83"
K16 = bytes.fromhex("3AC8914FD20B77A61E5CE98430BD62F1")


def decode(held: bytes) -> bytes:
    return bytes(c ^ K16[i % len(K16)] ^ ((i * 27 + 165) & 255)
                 for i, c in enumerate(held))


def run():
    if not FILE.is_file() or FILE.is_symlink():
        raise ValueError("Detached setup absent or replaced")
    content = FILE.read_bytes()
    if hashlib.sha256(content).hexdigest().upper() != SHA or len(content) > 1048576:
        raise ValueError("Unrecognized setup copy")
    pe = pefile.PE(data=content, fast_load=True)
    hits = []
    for section in pe.sections:
        if section.Name.rstrip(b"\0") not in (b".rdata", b".data"):
            continue
        offset = section.PointerToRawData
        length = section.SizeOfRawData
        data = content[offset: offset + length]
        for i in range(max(0, len(data) - 8)):
            first = decode(data[i:i + 8])
            if not (first.startswith(b"https://") or first.startswith(b"http://")):
                continue
            for size in range(14, min(256, len(data) - i) + 1):
                candidate = decode(data[i:i + size]).rstrip(b"\0")
                if not all(32 <= c <= 126 for c in candidate):
                    break
                try:
                    url = candidate.decode("ascii")
                    parsed = urlsplit(url)
                    if parsed.scheme in ("http", "https") and parsed.hostname and "." in parsed.hostname:
                        safe = parsed.scheme + "://" + parsed.netloc + parsed.path
                        if not hits or hits[-1].get("offset") != offset + i:
                            hits.append({"offset": offset + i, "candidate_base_url": safe})
                except ValueError:
                    continue
    report = {"status": "OFFLINE_XOR_STRING_PROBE",
              "sha256": SHA, "candidates": hits[:8],
              "documented_key_literal_occurrences": content.count(K16),
              "candidate_count": len(hits), "installer_executed": False,
              "licensing_bypassed": False, "download_attempted": False}
    print(json.dumps(report, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    run()
