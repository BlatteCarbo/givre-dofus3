"""Offline source-bound feature parity audit for reconstructed Givre 1.3.1.

Does not import application binaries, access account data, execute Lua, or open
network/process handles. A source-path-present result is not a gameplay pass.
"""

from __future__ import annotations

import hashlib
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DOC = Path("docs/portal/lua-api-doc.json")
NPC = Path("GivreClone.App/Services/GivreLuaHost.SnowNpc.cs")
ECON = Path("GivreClone.App/Services/GivreLuaHost.SnowEconomy.cs")
MAP = Path("GivreClone.App/Services/GivreLuaHost.SnowMap.cs")


@dataclass(frozen=True)
class Feature:
    api: str
    file: Path
    table: str
    member: str
    registration: str  # fill / assignment / foreach
    expected: str
    helper: str = ""
    sentinel: str = ""
    dynamic: str = ""


FEATURES = (
    Feature("quest:objectiveText", NPC, "q", "objectiveText", "fill", "FAIL_CONSTANT", sentinel='Str("")'),
    Feature("quest:path", NPC, "q", "path", "fill", "FAIL_STUB", sentinel="WarnSnow"),
    Feature("character:accountId", NPC, "ch", "accountId", "fill", "FAIL_CONSTANT", sentinel="DynValue.NewNumber(-1)"),
    Feature("character:getSpellMinPlayerLevel", NPC, "ch", "getSpellMinPlayerLevel", "fill", "FAIL_CONSTANT", sentinel="DynValue.NewNumber(-1)"),
    Feature("mount:toggleRiding", NPC, "mount", "toggleRiding", "foreach", "FAIL_STUB", helper="M", sentinel="DynValue.False"),
    Feature("mount:getEnergy", NPC, "mount", "getEnergy", "foreach", "FAIL_STUB", helper="M", sentinel="DynValue.NewNumber(-1)"),
    Feature("sale:buyToPnj", ECON, "sale", "buyToPnj", "assignment", "SOURCE_PATH_PRESENT", dynamic="_launch.NpcBuyAsync"),
    Feature("sale:sellToPnj", ECON, "sale", "sellToPnj", "assignment", "SOURCE_PATH_PRESENT", dynamic="_launch.NpcSellAsync"),
    Feature("merchant:buyItem", ECON, "m", "buyItem", "fill", "FAIL_STUB", helper="Stub", sentinel="DynValue.False"),
    Feature("fightSlave:castSpellOnCell", MAP, "fsl", "castSpellOnCell", "foreach", "FAIL_STUB", helper="Neutral", sentinel="DynValue.False"),
    Feature("quest:initialize", NPC, "q", "initialize", "assignment", "SOURCE_PATH_PRESENT", dynamic="_launch.QuestsLoaded"),
    Feature("npc:replyByIdAndChangeMap", NPC, "npc", "replyByIdAndChangeMap", "fill", "SOURCE_PATH_PRESENT", dynamic="_launch.NpcReplyById"),
    Feature("sale:AvailableSpace", ECON, "sale", "AvailableSpace", "assignment", "SOURCE_PATH_PRESENT", dynamic="_launch.HdvFreeSlots"),
    Feature("craft:isOpen", ECON, "craft", "isOpen", "fill", "SOURCE_PATH_PRESENT", dynamic="_launch.CraftOpen"),
)


def mask_comments(src: str) -> str:
    """Remove comments without treating // inside C# strings as comments.

    Preserve newline positions for report line numbers. This deliberately
    supports regular and verbatim C# strings as used in these sources.
    """
    out = list(src)
    i = 0
    mode = "code"
    while i < len(src):
        c = src[i]
        nxt = src[i + 1] if i + 1 < len(src) else ""
        if mode == "code":
            if c == '@' and nxt == '"':
                mode = "verbatim"
                i += 2
                continue
            if c == '"':
                mode = "string"
            elif c == "'":
                mode = "char"
            elif c == '/' and nxt in ('/', '*'):
                mode = "line" if nxt == '/' else "block"
                out[i] = out[i + 1] = ' '
                i += 2
                continue
        elif mode == "line":
            if c == '\n':
                mode = "code"
            else:
                out[i] = ' '
        elif mode == "block":
            if c == '*' and nxt == '/':
                out[i] = out[i + 1] = ' '
                i += 2
                mode = "code"
                continue
            if c != '\n':
                out[i] = ' '
        elif mode == "string":
            if c == '\\':
                i += 2
                continue
            if c == '"':
                mode = "code"
        elif mode == "verbatim":
            if c == '"' and nxt == '"':
                i += 2
                continue
            if c == '"':
                mode = "code"
        elif mode == "char":
            if c == '\\':
                i += 2
                continue
            if c == "'":
                mode = "code"
        i += 1
    return "".join(out)


def statement(src: str, begin: int) -> str:
    """Grab one balanced C# statement (lambdas can contain semicolons)."""
    braces = parens = brackets = 0
    in_string = False
    in_char = False
    escape = False
    for i in range(begin, len(src)):
        c = src[i]
        if escape:
            escape = False
            continue
        if in_string or in_char:
            if c == '\\':
                escape = True
            elif (in_string and c == '"') or (in_char and c == "'"):
                in_string = in_char = False
            continue
        if c == '"':
            in_string = True
        elif c == "'":
            in_char = True
        elif c == '{':
            braces += 1
        elif c == '}':
            braces -= 1
        elif c == '(':
            parens += 1
        elif c == ')':
            parens -= 1
        elif c == '[':
            brackets += 1
        elif c == ']':
            brackets -= 1
        elif c == ';' and braces == parens == brackets == 0:
            return src[begin:i + 1]
    return ""


def registration(source: str, feature: Feature) -> tuple[str, int]:
    src = mask_comments(source)
    t = re.escape(feature.table)
    n = re.escape(feature.member)
    if feature.registration == "fill":
        pattern = rf'\bFill\(\s*{t}\s*,\s*"{n}"\s*,'
    elif feature.registration == "assignment":
        pattern = rf'\b{t}\s*\[\s*"{n}"\s*\]\s*='
    else:
        # Only attribute a member to a foreach loop if its actual member list
        # contains the name and the subsequent Fill binds THAT table/helper.
        pattern = (rf'\bforeach\s*\(\s*var\s+n\s+in\s+new\[\]\s*'
                   rf'\{{(?P<names>[^{{}}]*)\}}\s*\)\s*'
                   rf'Fill\(\s*{t}\s*,\s*n\s*,\s*{re.escape(feature.helper)}\(\s*n\s*,')
    for match in re.finditer(pattern, src, flags=re.S):
        if feature.registration == "foreach":
            if not re.search(rf'"{n}"', match.group("names")):
                continue
        snippet = statement(src, match.start())
        if snippet:
            return snippet, source.count('\n', 0, match.start()) + 1
    return "", 0


def helper_implementation(source: str, name: str) -> str:
    masked = mask_comments(source)
    pat = rf'\bDynValue\s+{re.escape(name)}\s*\(\s*string\s+fn\s*,\s*DynValue\s+ret\s*\)\s*=>'
    match = re.search(pat, masked)
    if not match:
        return ""
    return statement(masked, match.start())


def classify(source: str, feature: Feature) -> tuple[str, int, str]:
    expr, line = registration(source, feature)
    if not expr:
        return "ABSENT", 0, "No binding registration found"
    if feature.helper:
        if not re.search(rf'\b{re.escape(feature.helper)}\s*\(', expr):
            return "REVIEW", line, "Bound helper changed"
        helper = helper_implementation(source, feature.helper)
        if not helper:
            return "REVIEW", line, "Helper definition unavailable"
        if (feature.sentinel in expr and
            re.search(r'WarnSnow(?:NotYet)?\s*\(', helper) and
            re.search(r'\breturn\s+ret\s*;', helper)):
            return "FAIL_STUB", line, "Registered helper returns neutral value and warns"
        return "REVIEW", line, "Helper no longer a confirmed neutral stub"
    if feature.dynamic and feature.dynamic in expr:
        return "SOURCE_PATH_PRESENT", line, "Callback references runtime state or service"
    if feature.sentinel in expr:
        if "WarnSnow" in expr:
            return "FAIL_STUB", line, "Callback returns neutral value with unsupported warning"
        return "FAIL_CONSTANT", line, "Callback returns a fixed sentinel or empty value"
    return "REVIEW", line, "Expression requires behavioral review"


def documented_names(root: dict) -> set[str]:
    return {
        str(sig.get("ns", "")).rstrip(":") + ":" + str(sig.get("name", ""))
        for cat in root.get("categories", [])
        for method in cat.get("methods", [])
        for sig in method.get("sig", [])
    }


def audit(root: Path = ROOT) -> dict:
    doc = json.loads((root / DOC).read_text(encoding="utf-8"))
    names = documented_names(doc)
    paths = sorted(set(f.file for f in FEATURES), key=str)
    sources = {p: (root / p).read_text(encoding="utf-8") for p in paths}
    rows = []
    for f in FEATURES:
        status, line, cause = classify(sources[f.file], f)
        rows.append({"api": f.api, "status": status, "expected_source_status": f.expected,
                     "doc_declared": f.api in names, "file": f.file.as_posix(),
                     "line": line, "cause": cause,
                     "behavior": "UNTESTED_OFFLINE", "v131_binary_parity": "UNTESTED"})
    sha = {p.as_posix(): hashlib.sha256(sources[p].encode("utf-8")).hexdigest() for p in paths}
    sha[DOC.as_posix()] = hashlib.sha256((root / DOC).read_bytes()).hexdigest()
    return {"scope": "reconstructed sources only; no original runtime/game ACK",
            "source_sha256": sha, "features": rows}


def main() -> int:
    result = audit()
    print(json.dumps(result, ensure_ascii=True, indent=2))
    mismatches = [r for r in result["features"] if
                  r["status"] != r["expected_source_status"] or not r["doc_declared"]]
    if mismatches:
        print("SOURCE_CONTRACT_REVIEW_REQUIRED:", ", ".join(x["api"] for x in mismatches), file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
