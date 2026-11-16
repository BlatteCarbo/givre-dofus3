"""Offline compatibility proof for authentic decompiled WebAuth and rebuilt App.

Checks source signatures, never reconstructs/prints HMAC secrets, does not run
the helper or read credentials, profiles, or network traffic.
"""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
ORIGINAL = ROOT / (
    "offline-evidence/v131-app/decompiled-WebAuth-IL/"
    "GivreLauncher.WebAuth/Program.cs"
)
LAUNCHER = ROOT / "GivreClone.App/Services/WebAuthRunner.cs"
FLOW = ROOT / (
    "offline-evidence/v131-app/decompiled-WebAuth-IL/"
    "GivreLauncher.WebAuth/LoginFlow.cs"
)
SIDE_CAR = ROOT / "vendor/GivreWebAuth131/GivreLauncher.WebAuth.dll"
assert all(f.is_file() and not f.is_symlink()
           for f in (ORIGINAL, LAUNCHER, FLOW, SIDE_CAR))

original = ORIGINAL.read_text(encoding="utf-8")
launcher = LAUNCHER.read_text(encoding="utf-8")
flow = FLOW.read_text(encoding="utf-8")
checks = 0


def check(expression, label):
    global checks
    if not expression:
        raise AssertionError(label)
    checks += 1


original_bytes = re.search(
    r"private static readonly byte\[\] HsEnc = new byte\[49\]\s*\{([^}]+)\}",
    original,
)
launcher_bytes = re.search(
    r"private static readonly byte\[\] _hsEnc\s*=\s*\{([^}]+)\}",
    launcher,
)
check(original_bytes is not None and launcher_bytes is not None,
      "original and replica both use the same handshake byte-array contract")
actual = [int(piece) for piece in re.findall(r"\b\d+\b", original_bytes.group(1))]
rebuilt = [int(piece, 16) for piece in re.findall(r"0x[0-9a-fA-F]{2}", launcher_bytes.group(1))]
check(len(actual) == len(rebuilt) == 49,
      "handshake key size matches, no partial secret")
check(actual == rebuilt, "original/replica handshake encrypted bytes coincide")
check("new HMACSHA256" in original and "HMACSHA256" in launcher,
      "SHA256 HMAC primitive matches")
check("ToUnixTimeSeconds() / 30" in original and "ToUnixTimeSeconds() / 30" in launcher,
      "thirty-second handshake window matches")
check("MZ_WEBAUTH_HS" in original and "MZ_WEBAUTH_HS" in launcher,
      "same process admission environment key")
check('GetArg(args, "--proxy")' in original and 'psi.ArgumentList.Add("--proxy")' in launcher,
      "proxy argument consistent")
check('GetArg(args, "--lang")' in original and 'psi.ArgumentList.Add("--lang")' in launcher,
      "language argument consistent")
for key in ("login", "password", "proxy_user", "proxy_pass"):
    check(f'"{key}"' in original and f'"{key}"' in launcher,
          "stdin JSON field names agree: " + key)
check('status = ((loginFlow.ReachedRedirect' in original,
      "original helper distinguishes failed/cancelled")
check('status, "failed"' in launcher and 'status, "cancelled"' in launcher,
      "rebuilt runner handles failed/cancelled")
check('"access_token"' in launcher and '"refresh_token"' in launcher,
      "output JSON token field names accepted")
check('"access_token"' in flow and '"refresh_token"' in flow,
      "original helper emits the same token keys")
check('psi.Environment.Remove("MZ_WEBAUTH_FAILOVER")' in launcher,
      "optional inherited diagnostic beacon is disabled in rebuilt runner")
check("MZP17WebAuthIntegrity.TryAcquire" in launcher,
      "runner gates execution on pinned, verified companion files")
print(f"MZP17 genuine WebAuth IL/runner contract: PASS ({checks} offline checks). "
      "HMAC bytes not emitted. No token, browser, process, or game contacted.")
