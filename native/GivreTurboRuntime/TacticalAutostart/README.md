# Tactique : activation au chargement

## Limite du source courant au 5 novembre 2026

Les validations chronologiques ci-dessous ne prouvent pas le masquage du
source courant. `mods/native-tactical/src/lib.rs::suppress_map` conserve tous
les renderers et retourne `false` : l'association de construction entre les
elements de carte et leurs batches n'est pas disponible. Le candidat ne fournit
donc pas actuellement de decor tactique fonctionnel. Les trois racines typees
et leur backlink ne suffisent pas a prouver la propriete de chaque renderer.

Voir `../../../docs/validation/tactical-current-source-boundary-20261105.md`
et `../../../docs/validation/tactical-interactives-gap-20261105.md` pour les
preuves manquantes : association a l'entree de `ub.qil<T>`, ABI concrete,
synchronisation et quiescence des callbacks, puis classification positive des
decors sans masquer les interactifs, HDV, services ou portes. Ne pas apparier
les indices du pool apres `qii`, qui peut compacter cette collection.
La DLL distribuee reste distincte des candidats testes hors jeu ; aucun PASS
visuel ni promotion ne decoule des builds et tests ci-dessous.

## Etat de validation au 4 novembre 2026

Correctif du candidat apres audit : une cellule instanciee garde maintenant
un handle de nettoyage jusqu'au succes de SetParent. Une exception de
rattachement interrompt la construction ; si sa destruction echoue ensuite,
cleanup_pending reste actif jusqu'a une restauration reussie. Le handle est
aussi libere lors du teardown sans nouvel appel Unity sous bail revoque.
Regression bash1293 : echec attendu sur le code precedent, aucun handle pour
la cellule non rattachee. Suite native bash1294 : exit0,20 tests reussis,
aucun echec. Construction release isolee bash1295 : exit0. Nouveau candidat
313344 octets,SHA-256
`26a3e16cd7a63768e5c823470ad131edad968f12afc9f7c1f7ff755956e6b525`.
L'empreinte85868235... ci-dessous designe le candidat precedent, pas ce source.
Ces tests simulent les exceptions IL2CPP, pas le rendu ni la destruction
differee Unity. L'atomicite visuelle et les autres setters restent a verifier.

Configuration de cellule : les exceptions de positionnement, identifiant,
classification, visibilite du renderer et affectation du sprite interrompent
maintenant la construction avant son comptage. Regression bash1296 rouge sur
le positionnement ignore ; bash1297 a expose une fixture sans sprite serialise,
corrigee pour exercer aussi ce setter. Suite bash1298 : exit0,21 tests reussis,
0 echec,avec temoin sans exception et six points de panne. Le source est plus
recent que le candidat26a3e16c... et doit etre reconstruit avant essai.
Extension SetActive : bash1301 rouge pour l'activation ignoree ; propagation
de l'erreur avant/apres rattachement, suite bash1303 exit0,21 tests reussis,
0 echec,avec temoin et sept points de panne. Build isole bash1304 exit0.
Candidat actuel312832 octets,SHA-256
`9df51247fccc60a5bf7f43f097d97f6616f75831f2217853ea74b06cef4a6209`.
Les empreintes precedentes ne designent pas cette derniere construction.
Miri non execute : cargo +nightly miri --version (bash1299) echoue car le cargo
installe ne gere pas +toolchain ; cela ne constitue pas une preuve memoire.

Parcours d'essai existant, non execute : desactiver via l'application sur carte,
attendre son ACK de generation et l'absence du mod dans STATUS ; copier le
candidat verifie sous un nom distinct dans le StageRoot reel du PID ; utiliser
LOAD du host sur ce fichier voisin puis activer via l'application. Le bail et
l'ACK restent alors ceux de l'application. Ne pas utiliser RELOAD pour remplacer
le module charge : cette commande n'attend pas le nettoyage applicatif.
Les fichiers et empreintes de distribution restent inchanges.

La preuve metadata et les specifications generiques courantes sont consignees
dans `METADATA-3.7.13.17.md`. Les noms inspectes sont conserves ; cela ne valide
ni les pointeurs generiques executables ni le rendu en jeu.

La DLL livree `9e9a7983...` decrite ci-dessous reste l'ancienne recherche
par nom : le client officiel 3.7.13.17 signale `map_<id> not found` avec
des enfants `ZoneTile(1)` sous `parentTiles`. Son chargement ne prouve pas
un decor tactique visible. Le source courant utilise deja les trois racines
typees possedees par le MapController et verifie leur identite avant masquage.
Il n'a pas ete promu dans les distributions ni valide sur cette version.

Le module visuel complet est un probleme distinct : l'audit statique
`../../../docs/validation/combat-visuals-signatures-371317-20261104.md`
constate 752 signatures divergentes sur 762. Ce resultat ne s'applique pas
automatiquement au candidat tactique, qui resout ses membres par metadata et
verifie la propriete des racines. Inversement, des noms metadata conserves ne
certifient ni ses appels generiques ni le placement visuel.

Validation hors jeu du source courant : `cargo test --offline` puis
`cargo build --offline --release`, paquet `dofus-native-tactical`, dans le
target-dir isole `%USERPROFILE%/AppData/Local/Temp/givre-tactical-owned-20261104`.
Resultat : 17 tests reussis, aucun echec ; build release exit0. Candidat
`release/dofus_native_tactical.dll`, 313344 octets, SHA-256
`85868235c0fa6d346801742a4bb0c520adb1c1dc9dbddecb10681e29834b523f`.
Aucun fichier livre, empreinte applicative ou processus de jeu modifie.

Restent a verifier sur 3.7.13.17 : chaine typee et backlink du controleur,
alignement des 560 cellules, conservation des acteurs/UI, restauration,
reactivation et transition. Les verificateurs binaires voisins sont epingles
a 3.7.12.12 : ne pas relacher leurs empreintes pour faire passer ce client.

Le module tactique attend un bail de carte frais appartenant a l'application avant tout appel gere. La case demande l'activation ; F8 ne bascule que le client au premier plan.

DLL x64 5.0.5 reconstruite le 2 novembre 2026. SHA-256 : `9e9a7983360e51b1992e0971ef58a7ca442e5ed38a0a725302bc2a72ba618532`. Le projet utilise cette DLL pour NativeTurboRuntime et SourceOwnedMods. Les empreintes du runtime et du catalogue sont mises a jour ensemble.

La racine statique `parentTiles` vide n'est plus acceptee comme preuve de decor. Le module cherche `map_<id>` si la racine statique n'a aucune branche visuelle, et exige une branche qu'il sait masquer puis restaurer. Les huit tests natifs passent ; la visibilite de cette nouvelle construction reste a verifier en jeu.

`build.ps1` reconstruit les bibliotheques Rust, execute les huit tests natifs et produit la DLL depuis un terminal Visual Studio x64.

Le remplacement de MapRenderer par MapController -> fpm/epf -> ClientMapData/GameObject est documente dans `METADATA-3.7.12.12.md`. GameAssembly confirme que LoadAssetAsync retourne Task<T>, pas UniTask<T>. Le resultat n'est lu qu'apres completion reussie ; construction limitee a huit cellules par tick, reprises et erreurs bornees.

Le host epingle appelle directement MethodInfo.MakeGenericMethod : cette implementation de base leve NotSupportedException, "Derived classes must provide an implementation." Le module utilise maintenant les methodes de la classe concrete de l'objet de reflection, avec les specialisations AOT existantes LoadAssetAsync<GameObject> et Release<UnityEngine.Object>. Les objets de reflection et les arguments boxes sont proteges par des racines GC ; les predicats de completion sont resolus sur Task, leur classe de declaration. Aucun host ni empreinte de host modifies.

`bun verify-map-root.mjs <installation Dofus>` verifie les empreintes installees, les champs de carte, les tokens de methodes, les specialisations AOT et l'exception de l'implementation de base sans injection.

Transition, perte du lien et fin du process revoquent le bail. Une desactivation sur carte attend l'accuse de restauration avant de decharger la DLL. Hors carte, le nettoyage attend la prochaine autorisation.

Validation du 2 novembre : build natif reussi, huit tests reussis, verificateur binaire reussi, empreintes synchronisees. Aucun client injecte : l'effet visuel, F8, le changement de carte et la restauration en jeu restent a verifier. Une case cochee ou un STATUS charge ne prouve pas l'effet visuel.

Journal attendu apres acquisition effective : `Native Tactical: tacticalCell prefab acquired from completed Task<GameObject>.`
