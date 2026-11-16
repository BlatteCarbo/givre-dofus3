"""Pinned static evidence checks for Launcher 2 v0.14.3 process creation.

This checks real packaged source, not runtime behavior, tickets, or network.
It does not read launcher profiles, repositories, or games; only 3 ASAR members.
"""

import hashlib
import json

from inspect_launcher2_source import member_bytes


EXPECTED = {
    "package.json": "076a07e0afc3dfc219bd108aa4494908845cf6e4cea1e7f02ed8eb9e0e8515ba",
    "electron-main.js": "8e099b65b380102cb8e56177cc8e586884b8260d1b9c6f107039bdadd25afc6f",
    "node_modules/protocol-generated-nodejs/ZaapService.js":
        "22225dfef610e7f820388037f38c34c6b07d104dff38583b1135cb297e7ec0a4",
}


def ensure(value: bool, label: str) -> None:
    if not value:
        raise SystemExit("MZP20 FAIL: " + label)
    print("MZP20 PASS", label)


members = {}
for name, expected in EXPECTED.items():
    raw = member_bytes(name)
    ensure(hashlib.sha256(raw).hexdigest() == expected, "source sha " + name)
    members[name] = raw.decode("utf-8", "strict")

metadata = json.loads(members["package.json"])
ensure(metadata.get("version") == "0.14.3" and
       metadata.get("main") == "./electron-main.js", "launcher version and entrypoint")

source = members["electron-main.js"]
checks = {
    "real node spawn import": 'import{spawn as pI}from"node:child_process"',
    "game settings compiled": "compileGameSettings(i,void 0,n)",
    "configuration file loaded": 'Cn(e,"zaap.yml")',
    "missing config has defaults": 'executable:"zaap-start",arguments:[],env:{}',
    "executable resolved from settings": 'h=d.executable==="zaap-start"',
    "game executable existence checked": "ft(Aa(i.installLocation,m))",
    "process arguments from configuration": 'let I=h?d.arguments.map',
    "child cwd, env, flags": 'pI(m,I,{cwd:i.installLocation,env:Object.assign({},process.env,n,d.env??{}),shell:h,detached:!0,stdio:"ignore",windowsHide:h&&!qa()})',
    "child tracked by launcher": "E.gameProcess=C",
    "game environment bound to instance": "ZAAP_INSTANCE_ID:o.id,ZAAP_HASH:String(o.id)",
    "game environment bound to launcher": "ZAAP_PORT:y.resolve(tn).getLocalPort(),ZAAP_GAME:r.gameUid,ZAAP_RELEASE:r.releaseName",
    "game environment bound to account": "ZAAP_CAN_AUTH:t!=null,ZAAP_LOGS_PATH:n,ACCOUNT_ID:o.accountId",
    "instance checked by connect RPC": "getGameInstance({gameInstanceId:t,gameInstanceKey:n,gameReleaseId:o})",
    "token RPC resolves owning account": "getGameInstance({gameInstanceKey:r})?.accountId",
    "token RPC calls launcher account auth": "return await y.resolve(le).createToken(e,n)",
    "ticket requested via authenticated account": '"json/Ankama/v5/Account/CreateToken"',
    "default localhost port and bounded candidates": "Ca=de===\"staging\"?36116:de===\"pre-production\"?16116:26116,aI=5e3,lI=4",
}
for label, fragment in checks.items():
    ensure(fragment in source, label)

service = members["node_modules/protocol-generated-nodejs/ZaapService.js"]
ensure("ZaapService_connect_args" in service, "compiled Thrift connect schema")
ensure("ZaapService_auth_getGameToken_args" in service,
       "compiled Thrift ticket schema")
print("MZP20 RESULT=STATIC_SPAWN_AND_ZAAP_CONTRACT_PINNED")
print("MZP20 EXECUTABLE_ARGV_ENV_VALUES=RUNTIME_CONFIGURATION_REQUIRED")
print("MZP20 TICKET=RUNTIME_ACCOUNT_BOUND_RPC_NO_STATIC_VALUE")
print("MZP20 GAME_RUNTIME_OR_AUTHORITY=NOT_TESTED")
