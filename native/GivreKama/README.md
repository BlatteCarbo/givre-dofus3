# GivreKama.dll - hook natif MITM de GivreClone

DLL C++ (Microsoft **Detours**) injectee dans `Dofus.exe` par GivreClone (`DofusCaptureHost` via
`GivreCore.DllInjector`). Elle hooke `ws2_32!connect` : quand le client se connecte au serveur de jeu
(`::ffff:x.x.x.x:5555`), elle reecrit la destination vers **`127.0.0.1:5658`** (le proxy GivreClone) et pousse
un **handshake de 26 octets** (`Address[16] + Addrlen(int) + Port(ushort) + Pid(uint)`, `#pragma pack(1)`) en
tete de socket. Layout identique a `GivreCore.Handshake`.

**Propre a GivreClone** : c'est le pendant de `GivreHook.dll` (GivreProxy) mais un binaire distinct, avec un
**port distinct (5658 vs 5657)** -> GivreClone est independant et peut coexister avec GivreProxy.

## Build

Prerequis : VS 2022 (charge *Desktop C++*, toolset v143). Detours est **vendorise** dans
`third_party/Detours/` (include + lib.X64) -> aucun telechargement.

```bash
msbuild native\GivreKama\GivreKama.vcxproj /t:Build /p:Configuration=Release /p:Platform=x64
```

Sortie : `native/GivreKama/build/Release/GivreKama.dll`. `GivreClone.App.csproj` la copie a cote de l'exe.
VMProtect est facultatif (marqueurs no-op si le SDK est absent).
