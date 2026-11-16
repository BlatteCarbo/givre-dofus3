"""Source-provenance and binary packages; original GivreClone not modified."""
from pathlib import Path
import hashlib
import json
import stat
import sys
import zipfile

TOOLS=Path(__file__).resolve().parents[1]
ROOT=TOOLS.parent
sys.path.insert(0,str(TOOLS))
from MZP8Release import package as binary
from MZP14Release.package import SOURCE_FILES as PRIOR

binary.ARCHIVE=binary.ARTIFACTS/"MZP15-Historical-GivreCloneApp-WinX64-OFFLINE-ONLY.zip"
binary.MANIFEST=binary.ARTIFACTS/"MZP15-Historical-App-Manifest.json"
binary.README=(
    "MZP15 historical GivreClone.App Windows x64, compiled, NOT game-certified.\n"
    "Source only reconstructs scoped map, character and combat state readers.\n"
    "No authenticated Dofus 3.7.11.11 login, server ACK or gameplay proof.\n"
    "No detached original GivreClone v1.3.1 code/binary was certified.\n"
    "Original installed GivreClone and active bots remain untouched.\n"
)
SOURCE_FILES=tuple(dict.fromkeys(PRIOR+(
    "GivreClone.Engine/MZP15VerifiedFightReader.cs",
    "tools/MZP15FightHarness/MZP15FightHarness.csproj",
    "tools/MZP15FightHarness/Program.cs",
    "tools/MZP15Release/package.py",
    "docs/MZP15_PRIME_HANDOFF_20261016.md",
)))

def source_package():
    destination=binary.ARTIFACTS/"MZP15-Source-Delta-OFFLINE-ONLY.zip"
    contents=[]
    with zipfile.ZipFile(destination,"w",compression=zipfile.ZIP_DEFLATED,compresslevel=9) as writer:
        for relative in SOURCE_FILES:
            path=ROOT/relative
            if not path.is_file() or path.is_symlink() or path.stat().st_size>2_000_000:
                raise ValueError("Unsafe source "+relative)
            if not path.resolve().is_relative_to(ROOT.resolve()):
                raise ValueError("Outside source root")
            raw=path.read_bytes()
            contents.append({"path":relative,"sha256":hashlib.sha256(raw).hexdigest(),"bytes":len(raw)})
            info=zipfile.ZipInfo(relative,binary.STAMP)
            info.compress_type=zipfile.ZIP_DEFLATED
            info.external_attr=(stat.S_IFREG|0o644)<<16
            writer.writestr(info,raw,compress_type=zipfile.ZIP_DEFLATED,compresslevel=9)
        manifest=(json.dumps({"auth_gameplay_verified":False,"real_v131_decompiled":False,
                              "files":contents},indent=2,sort_keys=True)+"\n").encode()
        info=zipfile.ZipInfo("SOURCE_SHA256_MANIFEST.json",binary.STAMP)
        info.compress_type=zipfile.ZIP_DEFLATED
        writer.writestr(info,manifest,compress_type=zipfile.ZIP_DEFLATED,compresslevel=9)
    with zipfile.ZipFile(destination) as reader:
        if reader.testzip() is not None: raise ValueError("Invalid source ZIP")
    print("SOURCE_ARCHIVE="+str(destination))
    print("SOURCE_SHA256="+hashlib.sha256(destination.read_bytes()).hexdigest())
    print("SOURCE_BYTES="+str(destination.stat().st_size))

if __name__=="__main__":
    binary.main()
    source_package()
