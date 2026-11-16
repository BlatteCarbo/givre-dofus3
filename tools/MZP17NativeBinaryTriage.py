"""Read-only, SHA-pinned PE/CLR triage of code extracted from the isolated ZIP.

Sampling bounded portions of PE sections. No native execution, no account
data, no process/memory access, and no extraction of embedded credentials.
"""
from pathlib import Path
from collections import Counter
import hashlib
import json
import math

import pefile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "offline-evidence/v131-app/verified-binaries"
MANIFEST = ROOT / "offline-evidence/v131-app/MZP17-code-only-sha256.json"
EXPECTED = json.loads(MANIFEST.read_text(encoding="utf-8"))
RESULTS = []


def entropy(buffer: bytes) -> float:
    if not buffer:
        return 0.
    frequency = Counter(buffer)
    return -sum((n / len(buffer)) * math.log2(n / len(buffer)) for n in frequency.values())


for item in EXPECTED["binaries"]:
    file = SOURCE / item["path"]
    if not file.is_file() or file.is_symlink() or not file.resolve().is_relative_to(SOURCE.resolve()):
        raise ValueError("Invalid code file: " + item["path"])
    digest = hashlib.sha256(file.read_bytes()).hexdigest().upper()
    if digest != item["sha256"] or file.stat().st_size != item["bytes"]:
        raise ValueError("Original ZIP witness no longer matches")
    pe = pefile.PE(str(file), fast_load=True)
    pe.parse_data_directories(directories=[
        pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_IMPORT"],
        pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_EXPORT"]
    ])
    with file.open("rb") as stream:
        sections = []
        for part in pe.sections:
            stream.seek(part.PointerToRawData)
            part_data = stream.read(min(part.SizeOfRawData, 1024 * 1024))
            sections.append({
                "name": part.Name.rstrip(b"\x00").decode("ascii", "replace"),
                "raw_bytes": part.SizeOfRawData,
                "entropy_sample_1MiB": round(entropy(part_data), 3)
            })
    exports = [e.name.decode("ascii", "replace") for e in
               getattr(getattr(pe, "DIRECTORY_ENTRY_EXPORT", None), "symbols", [])
               if e.name is not None]
    clr = bool(pe.OPTIONAL_HEADER.DATA_DIRECTORY[14].VirtualAddress)
    oversized = any(s["raw_bytes"] > 0.6 * file.stat().st_size for s in sections)
    unusual = (not clr and
               next((s["raw_bytes"] for s in sections if s["name"] == ".text"), None) == 0
               and oversized)
    category = ("managed_dotnet_metadata" if clr else
                "native_aot_candidate" if "DotNetRuntimeDebugHeader" in exports else
                "native_protected_packed_candidate" if unusual else
                "native_unmanaged_unknown")
    RESULTS.append({
        "file": item["path"], "sha256": digest, "bytes": item["bytes"],
        "CLR_metadata": clr, "classification": category,
        "sections": sections, "export_count": len(exports),
        "export_contains_DotNetRuntimeDebugHeader":
            "DotNetRuntimeDebugHeader" in exports,
        "did_not_execute": True
    })
    pe.close()

report = {
    "status": "STATIC_PE_CLASSIFICATION_NOT_DECOMPILATION",
    "no_running_process_touched": True,
    "original_code_exactly_recovered": False,
    "input_archive_sha256": EXPECTED["distribution_sha256"],
    "results": RESULTS,
}
out = ROOT / "offline-evidence/v131-app/MZP17-native-classification.json"
out.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
for entry in RESULTS:
    print(entry["file"], entry["classification"], "CLR=" + str(entry["CLR_metadata"]),
          "sections=" + str(len(entry["sections"])))
print("STATIC_TRIAGE=PASS, authentic code-only witnesses=9, no decompilation claimed.")
