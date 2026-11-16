import hashlib, json, pathlib, re, shutil, subprocess

ROOT = pathlib.Path(__file__).parent
SOURCE = pathlib.Path(r"%USERPROFILE%\AppData\Local\Mizan\app")
ILSPY = next((ROOT / "tools" / "ilspy" / ".store").rglob("ilspycmd.exe"))
(ROOT / "payload").mkdir(exist_ok=True)
(ROOT / "logs").mkdir(exist_ok=True)
manifest = []
for name in ["MizanBot.exe", "MizanKama.dll", "MizanMods.dll", "MizanMcp.exe",
             "WebAuth/MizanLauncher.WebAuth.dll"]:
    source = SOURCE / name
    alias = re.sub("mizan", "MZB", source.name, flags=re.I)
    output = ROOT / "payload" / alias
    shutil.copyfile(source, output)
    manifest.append({"source": str(source), "artifact": str(output.relative_to(ROOT)),
                     "bytes": output.stat().st_size,
                     "sha256": hashlib.sha256(output.read_bytes()).hexdigest()})
    if name.startswith("WebAuth"):
        continue
    result = subprocess.run([str(ILSPY), "-l", "c", str(output)],
                            capture_output=True, text=True, timeout=60)
    (ROOT / "logs" / (alias + ".ilspy.txt")).write_text(
        "exit_code=" + str(result.returncode) + "\n" + result.stdout + result.stderr, encoding="utf-8")
    print(alias, "ILSpy exit", result.returncode)
(ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
print("Preserved", len(manifest), "original binary copies under neutral filenames")
