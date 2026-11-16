#!/usr/bin/env python3
"""Static offline consistency checks for the detached GivreClone setup."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from MZP16InstallerAudit import inspect
from MZP16EncodedBaseUrlProbe import decode

tests = 0
def require(condition, label):
    global tests
    if not condition:
        raise AssertionError(label)
    tests += 1

data = inspect()
require(data["status"] == "STATIC_SETUP_PE_ONLY", "offline classification")
require(data["sha256"] == "4D4D6458A0B808173749EB98D9FC3B199748B707CEFD8C7319809769C1DF6E83", "SHA pin")
require(data["size_bytes"] == 226304, "exact size")
require(data["machine_hex"] == "0x8664", "x64 machine")
require(not data["clr_directory_present"], "not managed IL")
require(data["overlay_bytes"] == 0, "no appended ZIP")
require("WINHTTP.dll" in data["imported_modules_and_functions"], "WinHTTP")
require("WinHttpSendRequest" in data["imported_modules_and_functions"]["WINHTTP.dll"], "HTTP request")
require("BCryptHashData" in data["imported_modules_and_functions"]["bcrypt.dll"], "SHA")
require("GIVRE_BASE_URL" in data["keyword_hints_bounded"], "URL selector")
require("download_url" in data["keyword_hints_bounded"], "remote ZIP")
require("current_version" in data["keyword_hints_bounded"], "remote version metadata")
require(not data["installer_executed"] and not data["app_payload_extracted"], "no execution")
source = (ROOT / "native" / "GivreUpdaterExe" / "main.cpp").read_text(encoding="utf-8")
require('url += "/version?v="' in source, "source route")
require('JsonString(payload, "download_url", dl)' in source, "source download URL")
require("if (!Sha256File(zipPath, actual)" in source, "source hash validation")
require("ApplyZip(zipPath, a.target, a.mode == 1" in source, "source target replacement")
require("RemovePath(appDir)" in source, "original app at risk if launched")
require(decode(b"\x00") == b"\x9f", "placeholder not URL")
print(f"MZP16 setup static contract PASS ({tests} checks). No execution, network or installed Givre access.")
