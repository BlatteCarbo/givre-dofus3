# Runtime de mods natifs reconstruit depuis la source (2026-10-16)

Source : https://github.com/AlpaGit/dofus3-native-host (MIT), commit `010eaac390c94b9e812e616b8c3ce9cd0525d13d` ("Fix native tactical loading and map transitions"), copie sans .git ni target dans `native/dofus3-native-host/`.
Chaîne : rustc 1.98.0 (88d9e12ae 2026-08-18) ; MSVC 14.44.35207 (Build Tools 2022) ; Windows SDK 10.0.22621 ; `build.ps1` du dépôt (cargo build --workspace --release --offline puis cl /std:c++20 pour le bootstrap).
Tests : `cargo test --workspace --release --offline` : 10 réussis, 0 échoué (la plupart des crates n'ont pas de tests).
NON VÉRIFIÉ : chargement réel dans Dofus.exe 3.7.11.11 (bootstrap, hôte, mods).

| Fichier | SHA-256 |
|---|---|
| DofusNativeBootstrap.dll | `cfdf212ab4503a09cd4feb3753ba46a4427273e28f2f3541b73398b9f49346bf` |
| DofusNativeHost.dll | `90ea11609bc03df3c36b820d65c0ee18285ee7e96a1f1c0d9922b8cc75fb3c1a` |
| NativeMods/DofusCombatAnimationSkipper.dll | `c8f2a344bdf88d933269298cbf6cc9cc39fb3eadcb9e732128c4d82e47ee1eab` |
| NativeMods/DofusInterfaceAnalyzer.dll | `4681d0ad761edc4c996e206297890ee122a39aa3ab11024bed15570de655b7be` |
| NativeMods/DofusNativeExample.dll | `7271d9c40204265982be48e793d86b9a65fb03fcf1ba35c4b67b4f21e1f6d0de` |
| NativeMods/DofusNativeTactical.dll | `badf4d23047432f6ae5b8fe4bca8960dea43457d2d71108786100f7353e96d2f` |
| NativeMods/DofusNetworkDumper.dll | `d1d147779302fd89b10a5a5fcc6836244735de441dc734ac8a9700523430d1ee` |
| NativeMods/DofusRuntimeInspector.dll | `b4ca9d87435839e94dc0ea66f6c87bb3329573732f8ff25b3eb46061c3b99a12` |
