# WebAuth recovered-source comparison — 2026-10-23

Scope: existing decompiled C# only. No new binary analysis or runtime execution. The Downloads recovery is labelled 1.3.4 by its surrounding investigation; this comparison does not independently establish its shipped version.

Compared `%USERPROFILE%/Downloads/GivreClone/src/GivreCloneLauncher.WebAuth/GivreCloneLauncher.WebAuth` with `offline-evidence/v131-app/decompiled-WebAuth-IL/GivreLauncher.WebAuth`.

## Observations

Seven C# files exist in each tree. After normalizing only the namespace prefix, Program, LoginFlow, LoginResult, BrowseSession and LocalSocksBridge are identical. BrowseForm differs only in two window-title literals (`GivreClone` versus `GivreCloneBot`). BrowserSetup differs only in local profile root branding (`GivreClone` versus `GivreCloneBot`) and legacy temp migration root (`GivreWebAuth` versus `GivreCloneWebAuth`). There is no missing recovered authentication, proxy, session or browser feature in this source comparison. These path changes affect profile storage identity; they do not justify importing or moving existing browser profiles.

`tools/MZP17RecoveredWebAuth` already compiles the seven recovered behavior files plus AssemblyInfo. `tools/MZP18IsolatedWebAuth` already reuses that source with isolated Program/BrowserSetup replacements. Production uses `GivreClone.App/Services/WebAuthRunner.cs` plus pinned companion validation; an isolated browser runner also exists. No duplicate project or authentication-policy change was introduced.

## Verification and limits

`python -B tools/MZP17WebAuthContract.py`: PASS, 18 offline contract checks. No browser, game, credential profile or network contacted. This is source-level contract verification; it does not establish a successful live authentication or full binary equivalence. The protected native main application is outside this comparison. No source behavior modification was warranted by this evidence.

## File provenance

```json
[
  {
    "file": "BrowseForm.cs",
    "v131_sha256": "83908c0180c538e06d2292e39a062bb3ec754704b1bf6ae3ce620e98bfd44157",
    "recovered_134_sha256": "715c350636d608838e6a14298a79717bfa9f3bca1ec7ca0c851baa2c6b35b60e",
    "equal_after_namespace_normalization": false
  },
  {
    "file": "BrowserSetup.cs",
    "v131_sha256": "75a74667874fe9eab740e7c696c2f87a3359400776fe0c9d114681408319035e",
    "recovered_134_sha256": "d7218f0812bce9e5062f887e1e3477b04f0e5ce51bc0b3f423110c3aa225f172",
    "equal_after_namespace_normalization": false
  },
  {
    "file": "BrowseSession.cs",
    "v131_sha256": "78f25ac249b342a07cfae8beeb0cb22143a72054d6ae6cfe619a61cd5460b636",
    "recovered_134_sha256": "72a0abae39ac7ce92dfb1e12de39246c0f2eb343c1f47197e69efb50dab13828",
    "equal_after_namespace_normalization": true
  },
  {
    "file": "LocalSocksBridge.cs",
    "v131_sha256": "034eec582d96c42cbfc52a27152b318362dbab80e20303a2e0a5e68b9ee5277d",
    "recovered_134_sha256": "2a81e295e7d55e46934bd74f7a6831b0efd208e52773c88362b242a21da66ba0",
    "equal_after_namespace_normalization": true
  },
  {
    "file": "LoginFlow.cs",
    "v131_sha256": "0c532d875f504a2a4d5459170ade4c73e3167bde3a018e64dc978fe23c9eddc9",
    "recovered_134_sha256": "a52f8dfdbcefc59bd07d5b3d69597f33f01532d4f1c0d496292bfbc9ea098170",
    "equal_after_namespace_normalization": true
  },
  {
    "file": "LoginResult.cs",
    "v131_sha256": "732999c5ceb3fba6ee221543fe9fc5d4ce8b490b849a0cb2ba25b77ae661f8b3",
    "recovered_134_sha256": "1d1aeced5db05b882e47990a0da3fc767e03a485d7c58577610aaf5e06ae6186",
    "equal_after_namespace_normalization": true
  },
  {
    "file": "Program.cs",
    "v131_sha256": "a6d1d349430f9350fe8b4e3a4a850c942c0918bff7abef10ce0460d1cc6a6552",
    "recovered_134_sha256": "915486a6b44cd963fcb747d2cadab3d903616fcbd8e4c8d7200f356549291c8e",
    "equal_after_namespace_normalization": true
  }
]
```
