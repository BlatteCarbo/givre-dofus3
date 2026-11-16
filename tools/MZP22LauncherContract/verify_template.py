"""Read-only, SHA-pinned Dofus Launcher 2 argument contract.

The generated values are TEMPLATE TOKENS only. This never accesses account
settings, process environments, browser profiles, tickets, ports, or sockets.
"""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import sys


BASE = Path("zaap.yml")
WINDOWS = Path("zaap-windows.yml")
PINS = {
    BASE.name: "d15ac9891ebf629fc9d9140648eeb357341c0fc8db14669cbce52e4628775789",
    WINDOWS.name: "ca7794bdee1a6bd44ff89c0e6a2924f4aa73d6dff3c350f57a6af32d52158d87",
}
EXPECTED_PAIRS = [
    ("--port", "{{ env.ZAAP_PORT }}"),
    ("--gameName", "{{ env.ZAAP_GAME }}"),
    ("--gameRelease", "{{ env.ZAAP_RELEASE }}"),
    ("--instanceId", "{{ env.ZAAP_INSTANCE_ID }}"),
    ("--hash", "{{ env.ZAAP_HASH }}"),
    ("--canLogin", "{{ env.ZAAP_CAN_AUTH }}"),
    ("--langCode", "{{ game.settings.language }}"),
    ("--autoConnectType", "{{ game.settings.autoConnectType }}"),
    ("--connectionPort", "{{ game.settings.connectionPort }}"),
]


def default_root() -> Path:
    roaming = os.environ.get("APPDATA")
    if not roaming:
        raise ValueError("APPDATA is unavailable")
    return Path(roaming) / "zaap2" / "repositories" / "production" / "dofus" / "dofus3" / "windows" / "configuration"


def _read_pinned(root: Path, filename: str) -> str:
    candidate = root / filename
    if candidate.is_symlink() or root.is_symlink():
        raise ValueError("Symlinked launcher template rejected")
    if not candidate.is_file() or candidate.stat().st_size > 8192:
        raise ValueError("Missing or oversized launcher template")
    data = candidate.read_bytes()
    if hashlib.sha256(data).hexdigest() != PINS[filename]:
        raise ValueError("Launcher template digest differs from verified release")
    return data.decode("utf-8-sig")


def _tokens(text: str) -> list[dict[str, str]]:
    block = re.search(r"\{% block arguments %\}(.*?)\{% endblock arguments %\}", text, re.S)
    if block is None:
        raise ValueError("Launcher argument block absent")
    condition = "always"
    result: list[dict[str, str]] = []
    for raw in block.group(1).splitlines():
        line = raw.strip()
        if line in ("{% if game.settings.hdReady %}", "{% if game.settings.4kReady %}"):
            if condition != "always":
                raise ValueError("Unexpected nested condition")
            condition = "hdReady" if "hdReady" in line else "4kReady"
        elif line == "{% endif %}":
            if condition == "always":
                raise ValueError("Unexpected endif")
            condition = "always"
        elif line:
            match = re.fullmatch(r'- "([^"\n]+)"', line)
            if not match:
                raise ValueError("Unrecognized argument template syntax")
            result.append({"token": match.group(1), "when": condition})
    if condition != "always":
        raise ValueError("Unclosed argument condition")
    return result


def verify(root: Path) -> dict:
    primary = _read_pinned(root, BASE.name)
    platform = _read_pinned(root, WINDOWS.name)
    if '{% extends "zaap-" + os.platform + ".yml" %}' not in primary:
        raise ValueError("Platform template inheritance changed")
    if "maxInstances: true" not in platform:
        raise ValueError("Platform instance option changed")
    args = _tokens(primary)
    required = [entry["token"] for entry in args if entry["when"] == "always"]
    expected = [value for pair in EXPECTED_PAIRS for value in pair]
    if required != expected:
        raise ValueError("Required Dofus launch arguments changed")
    optional = [(entry["token"], entry["when"]) for entry in args if entry["when"] != "always"]
    if optional != [("--hdReady", "hdReady"), ("--4kReady", "4kReady")]:
        raise ValueError("Optional Dofus launch arguments changed")
    trailing = re.search(r'\{% endblock arguments %\}\s*- "(.*?)"\s*fragments:', platform, re.S)
    if not trailing or trailing.group(1) != "{{ game.settings.directxVersion }}":
        raise ValueError("DirectX setting template changed")
    args.append({"token": trailing.group(1), "when": "always"})
    return {
        "source": "installed Launcher 2 static, 2026-10-16",
        "source_sha256": PINS.copy(),
        "argument_template": args,
        "executable": "DYNAMIC_FROM_RELEASE_CONFIG_OR_LAUNCHER_FALLBACK",
        "cwd": "gameRelease.installLocation (dynamic)",
        "zaap_environment": "Launcher-owned dynamic session (not provided)",
        "gameplay_or_ticket_certified": False,
        "status": "STATIC_TEMPLATE_VERIFIED_SESSION_REQUIRED",
    }


if __name__ == "__main__":
    try:
        result = verify(default_root())
        print(json.dumps(result, indent=2, ensure_ascii=False))
    except (OSError, UnicodeError, ValueError) as exc:
        print(f"MZP22 Launcher template refused: {exc}", file=sys.stderr)
        sys.exit(1)
