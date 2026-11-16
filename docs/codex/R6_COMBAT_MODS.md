# Round 6 — mods combat (observation offline)

Version cible : Dofus **3.7.11.11**. Les sources et les empreintes ci-dessous sont des preuves de build **local**. Le rendu graphique en combat et les touches ne sont pas certifiés sans observation en jeu sur un client de test.

## Complément prompt 1 — durcissement autonome

Le fork **CombatSkipper 1.0.8** remplace la version 1.0.7 : un cache des animations Animator2D borné à 8192 entrées est vidé en sortie de combat, de sorte que les adresses d'objets Unity périmées ne s'accumulent pas indéfiniment et ne survivent pas au combat suivant. Les transitions et les compteurs déjà présents restent conservés. Dans `NativeTurboRuntime.InjectBootstrapOwned`, un refus de `MZP25InjectionPermit.TryRegister` interrompt désormais l'injection avant tout appel à `DllInjector.Inject`. Le refus sort aussi de la boucle de retry (les refus d'autorisation ne sont pas des délais de chargement Unity).

| Module embarqué actuel | SHA-256 local |
| --- | --- |
| `NativeTurboRuntime/DofusCombatAnimationSkipper.dll` | `90f0b00cdf48d09feaf349f5c7c88f529b09c0b3b35a7aa105d3a61a85cea89b` |
| `NativeTurboRuntime/DofusNativeTactical.dll` | `21888fba284112bf32aa368c2da06d1dde9d41d7d3d59347589c79a8a6f1b3fd` |

Vérification offline : `cargo test --manifest-path native/CombatSkipper/Cargo.toml --offline` (3/3), `cargo build --manifest-path native/CombatSkipper/Cargo.toml --release --offline`, puis `dotnet test ... --filter FullyQualifiedName~MitmTurboSourceTests`. Les empreintes sont épinglées dans `GivreClone.App/Services/NativeTurboRuntime.cs`.

### Collapse et déplacements instantanés

Le clone sait encore écrire les options `collapse` et `instantmove` dans `ModControl.WriteForPid`. Aucun mod natif source indépendant de GivreMods n'est actuellement relié à ces drapeaux. Leurs effets visuels restent **NON IMPLÉMENTÉS/NON CERTIFIÉS** ; ne pas les marquer fonctionnels sur la seule base de la case UI ou d'une confirmation du protocole réseau.

Le dossier Ghidra local `artifacts/ghidra-dofus-long-20261018` prouve l'existence de GameAssembly 3.7.11.11 avec 313735 fonctions inventoriées, mais son analyse a expiré ; les chaînes de métadonnées restent des **candidats**, sans méthode native sémantiquement démontrée. `tools/R6-FightClassProbe.js` a été ajouté : il ne lit que les types/méthodes IL2CPP et ne modifie pas les objets du jeu. À lancer **uniquement sur le PID d'un client de test vérifié par l'opérateur**, puis à faire examiner avant de créer `native/CombatInstant`. Une signature unique (classe + paramètres + adresse distincte) et une confirmation du comportement de déplacement/temporisation doivent être disponibles avant le hook.

```powershell
# Après vérification du PID, du chemin Dofus.exe, de l'heure de naissance et de la version :
frida -p <PID_TEST> -l tools/R6-FightClassProbe.js

# Après UN seul combat de test, synthèse locale sans reproduction des autres logs :
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Inspect-R6-CombatMods.ps1 -GamePid <PID_TEST>
```

Le rapport du second script apparaît sous `artifacts/R6-combat-mods/pid-<PID>-diagnostics.json` et donne `MISSING_NATIVE_LOG`, `STRUCTURAL_ERROR`, `INCOMPLETE_RUNTIME_EVIDENCE` ou `RUNTIME_LOG_EVIDENCE_ONLY`. **Aucun de ces états ne certifie l'efficacité visuelle à lui seul**. Les traces attendues en sortie de combat sont `combat transition OFF` puis animations normales ; pour le tactique, `Ready. Press F8` puis `Native tactical mode enabled on map` après F8. Les tests des 3 états de diagnostic sont purement synthétiques : `tools/Test-R6-CombatOffline.ps1`.

| Option Combat | Chemin effectif | État vérifié |
| --- | --- | --- |
| **No-anim** | `NativeTurboRuntime` charge `DofusCombatAnimationSkipper.dll` sur le PID possédé, ID `bubble.dofus3.combat-animation-skipper`. `native/CombatSkipper` accélère les animations Animator2D non bouclées si InputChecker signale un combat. | Rust 1.0.8 : cargo test 3/3 ; compilation release Windows. Les hooks et transitions demandent encore la preuve du log `native-host.log` et un combat. |
| **Collapse** | `LaunchService.Combat.cs:752` écrit `collapse=1` dans le contrôle `ModControl.cs:27-40`. | Aucune DLL Rust/source de ce mod dans le fallback autonome actuel. La case seule ne prouve pas l'effet. |
| **Déplacements instantanés** | Même contrôle local `instantmove=1`. | Aucune implémentation native indépendante prouvée ; pas de mod `CombatInstant` ajouté sans cible IL2CPP structurale vérifiée. |
| **Mode tactique F8** | `NativeTurboRuntime` charge `DofusNativeTactical.dll`, ID `bubble.dofus3.native-tactical`, version source `native/dofus3-native-host/mods/native-tactical`. | Build Rust release OK. Le résolveur refuse maintenant 0 ou plusieurs candidats `TacticalDebugCell` ; le toggle visuel reste à certifier. |

## Corrections concrètes

- `native/CombatSkipper/src/lib.rs` : un choix unique de témoin `InputChecker` et d'état de service optionnel règle le cas « service trouvé mais champ illisible », qui pouvait conserver `IN_COMBAT=true` après un combat. Transitions ON/OFF journalisées ; refus des signatures ayant plus de deux adresses de hook ; compteur d'animations accélérées existant conservé.
- `native/dofus3-native-host/mods/native-tactical/src/lib.rs` : lorsque le résolveur trouve plusieurs classes structurelles, il refuse maintenant de charger le mod au lieu de sélectionner arbitrairement la première.
- Empreintes SHA-256 des deux DLL locales épinglées dans `GivreClone.App/Services/NativeTurboRuntime.cs` ; tests dans `MitmTurboSourceTests`.

| DLL locale embarquée | SHA-256 |
| --- | --- |
| `native/GivreTurboRuntime/CombatSkipper/DofusCombatAnimationSkipper.dll` | `90f0b00cdf48d09feaf349f5c7c88f529b09c0b3b35a7aa105d3a61a85cea89b` |
| `native/GivreTurboRuntime/ours-010eaac/NativeMods/DofusNativeTactical.dll` | `21888fba284112bf32aa368c2da06d1dde9d41d7d3d59347589c79a8a6f1b3fd` |

## Vérification opérateur ciblée (un seul combat simple)

1. Sur un **client Dofus de test autorisé**, ouvrir un bot MITM possédé par GivreWork, cocher No-anim et Tactique, laisser Collapse et InstantMove désactivés (ils n'ont pas de mod indépendant établi).
2. Relever seulement le PID et les messages `native-host.log` correspondant à ce PID : `Combat Animation Skipper ready`, `combat transition ON`, puis `combat transition OFF` et le compteur d'animations ; vérifier que les deux IDs de mods apparaissent en LOAD et STATUS.
3. Lancer **un combat facile**, observer qu'une animation non bouclée est accélérée, puis que le comportement graphique redevient normal à la sortie ; activer/désactiver F8 et vérifier les cases tactiques.
4. Si le log montre classe ou méthode absente, arrêter le test, consigner la seule signature manquante et conserver le verdict non certifié. Ne pas compenser avec un hook arbitraire.

Le chemin `native-host.log` du client test n'était pas retrouvé dans l'espace local inspecté durant cette passe ; aucune ligne d'exécution de mod en combat n'est inventée.
