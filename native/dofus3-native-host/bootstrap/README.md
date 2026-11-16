# Dofus Native Bootstrap

Bootstrap Windows x64 pour Dofus 3.
Le même binaire est distribué sous deux noms :

- `version.dll`, chargé par `UnityPlayer.dll` et chargé de transmettre l'API
  Windows Version vers `System32/version.dll` ;
- `DofusNativeBootstrap.dll`, cible stable de l'API explicite `DNB_*`.

Dans `Dofus.exe`, un worker attend `GameAssembly.dll`, un domaine IL2CPP valide
et la fenêtre `UnityWndClass`. Il installe ensuite un hook `WH_GETMESSAGE` limité
au thread de cette fenêtre. L'initialisation et les ticks du host s'exécutent
ainsi sur le thread Unity sans patcher le code du jeu.

Le bootstrap refuse de charger le host en dehors du couple
`Dofus.exe` + `Dofus_Data`. Les erreurs sont écrites dans
`NativeMods/native-bootstrap.log`.

## Native unload acknowledgment

`DNB_Shutdown` keeps its void ABI. A foreign-thread call queues one attempt on
the existing Unity message hook; it never frees the host on the calling thread.
`DNB_GetState() == 3` acknowledges completion. State `4` means stopping/retained:
cleanup was busy or failed and requires another explicit `DNB_Shutdown` call.
No automatic retry or wait runs on the Unity thread. Bootstrap stays mapped for
its worker, message hook and forwarded exports.

The host must export `DNH_TryShutdown() -> int32_t`; only zero permits releasing
its loader reference. Older hosts without this export are rejected and retained.
The legacy void `DNH_Shutdown` delegates to the same gate but cannot acknowledge
success. Direct host callers must serialize host entry and only unload after a
successful `DNH_TryShutdown` returns; bootstrap cannot protect bypassing callers.

Modules may export `DNM_PrepareUnload() -> int32_t`. Zero permits teardown; busy
(`1`), errors and unknown nonzero statuses retain the DLL and its record. The
gate also runs on failed load, duplicate rejection and query-only inspection,
so it must tolerate uninitialized state. Rejected retained libraries are retried
on explicit host shutdown. Active modules whose unload vetoes remain listed but
stop ticking; explicit UNLOAD/RELOAD or shutdown retries. Reload/install stop on
veto; a blocked install keeps its target and backup without unsafe rollback.

Lifecycle calls run on the initialization/Unity thread. Modules must not unwind
or reenter lifecycle exports. Before returning zero they must prove no callback
is in flight AND exclude future foreign or reentrant callback entry until unload.
Same-thread hooks must actually be non-reentrant at this boundary; an active
counter alone cannot close the entry-before-counter race. The handshake neither
establishes that barrier nor proves Unity affinity. Legacy modules retain their
existing quiescence obligation.

The host requires successful owned-hook disable/removal before module cleanup
and checks remaining hooks again before `FreeLibrary`. Failed hooks remain
registered. Hooks outside the host API and callbacks into other DLLs are module
responsibilities. Concurrent external loaders, process termination and forced
external `FreeLibrary` are outside this contract.
