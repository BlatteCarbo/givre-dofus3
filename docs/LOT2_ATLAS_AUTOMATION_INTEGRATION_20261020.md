# GivreWork — lot 2 : assistants sans code, atlas, MCP

20 octobre 2026. Branche d'intégration uniquement. **Aucune admission gameplay,
aucun serveur ACK, aucun chargement Dofus ou injection de modules natifs.**

## Fonctionnalités accessibles depuis la fenêtre

- Dans la barre de titre, navigation latérale ou recherche **Ctrl+K → Atlas du monde**,
  l'écran `WorldAtlasPanel` donne une liste filtrable par mapId, coordonnées X/Y
  et couche (surface, mine, grotte, zaap, inconnue), inspecteur des sorties,
  provenance et état VERIFIED/UNVERIFIED. Les fiches manuelles restent
  systématiquement UNVERIFIED et ne créent aucune arête.
- Dans Bots → assistant sans code existant, le nouveau panneau **Lot 2** propose
  cinq modèles : Mineur/grotte, donjon, observation HDV, simulation de combat,
  préparation banque. Les champs identifiants sont saisis par l'opérateur.
  « Simuler cette routine » produit une liste de contrôles d'évidence hors jeu.
  Les modèles **ne génèrent pas** de Lua exécutables ni de demandes gameplay.
  L'ancien assistant trajet/récolte/combat et sa confirmation d'import Lua restent
  en place et inchangés dans leur sémantique.
- Un bouton Atlas dédié et un panneau au sein de MainWindow reprennent les mêmes
  styles sombres, sans recopier un moteur cartographique dupliqué.

## Sources et niveaux de confiance

`AtlasAccessService.UiSnapshot()` est branché au bouton **Actualiser la source
locale** du panneau Atlas. Il ne lit aucune connexion de jeu :

1. Si `GIVRE_ATLAS_REVIEW_DIR` est défini, `WorldAtlasReader.ReadDirectory`
   accepte uniquement un répertoire local avec `atlas-manifest.json` et deux
   fichiers JSON épinglés par SHA-256 ; `GIVRE_ATLAS_MANIFEST_SHA256` peut
   épingler le manifeste lui-même. L'état des cartes et des liens reste tel
   qu'établi par le lecteur ; un simple hash ne prouve pas un ACK.
2. Sinon, si `data/atlas-manifest.json` existe à côté de l'exécutable, le
   lecteur utilise la même politique stricte.
3. À défaut, `WorldAtlasReader.ReadLegacyCacheFiles` lit les caches
   `data/map_positions.json` et `data/worldgraph5.json`, sans les modifier.
   Tous ces éléments sont **CANDIDATE/UNVERIFIED**, même si leurs SHA sont
   calculés : ils permettent la **visualisation**, pas un trajet certifié.

La version attendue est détectée sur le marqueur client installé, ou fournie
explicitement par `GIVRE_ATLAS_VERSION` pour revue. Un numéro de version
choisi par utilisateur ne prouve pas la compatibilité réelle des cartes.

Les coordonnées identiques sur plusieurs couches ne donnent pas une connexion
entre ces couches. Une transition type 32 est une porte/interactif, pas
nécessairement une entrée de grotte ; une ScrollAction type 2 n'est pas forcément
un zaap. Les sorties conditionnelles doivent recevoir la preuve pertinente.

## Accès par MCP

Outil ajouté : `query_world_atlas` (`ReadOnly = true`) :

```json
{"requestJson":"{\"op\":\"list_maps\",\"limit\":20}"}
```

Les cinq opérations de la façade pure sont `capabilities`, `list_maps`,
`inspect_map`, `map_transitions` et `find_path`. `find_path` accepte
`mapId`, `targetMapId`, `maxHops` et `startZone` (si secteur connu) ;
recherche bornée, suivi des zones par sommet, arêtes explicites et
inconditionnelles seulement, résultat `UNKNOWN` lorsque la preuve manque.

Le pont `LocalControlBridge` refuse le MCP lorsque celui-ci est désactivé,
et utilise seulement la **source locale source-owned** `local-unpinned-cache`
pour le moment. Un import `GIVRE_ATLAS_REVIEW_DIR`, un dossier manifesté
ou un export partenaire ne sont pas divulgués à l'IA/MCP par ce pont.
La façade transporte statut/provenance, jamais des secrets, identifiants de
compte, capture réseau ou commandes de mouvement.

### DofusDB : partenaire et scraping

L'utilisateur affirme disposer d'un partenariat permettant de scraper le
**site** DofusDB. L'agent collecteur a examiné la page carte HTML publique,
son script SPA et les noms de routes `/map-positions`,
`/map-references`, `/interactives`, `/transport-from-maps`,
`/point-of-interest`, `/worlds` et `/subareas`. Une requête ponctuelle
de contrôle `/map-positions` a donné un vrai élément, mais **zéro liaison
grotte/zaap démontrée** : aucune collecte exhaustive n'est réalisée.

Le scraper `tools/dofusdb-map-importer/importer.py` valide les chemins
HTTPS, limite tailles, nombre de pages, cadence et retries, respecte les
blocages robots/CAPTCHA/403, conserve SHA et checkpoint, et sait normaliser
un export partenaire déjà obtenu. Le format d'export proposé n'est **pas**
un schéma de réponse DofusDB certifié. Le jeu de fixtures est 100 % synthétique.

La licence publique de l'API indique des restrictions pour usage IA
(LPNC-IA 1.0). L'autorisation de **scraper le site** et l'autorisation
de **transmettre les données à un agent IA/MCP** sont deux droits distincts.
En attendant preuve de l'exception partenaire applicable à l'IA,
les exports DofusDB restent en quarantaine pour revue humaine ; aucun
MCP ou générateur automatique ne les ingère. Voir
`DOFUSDB_PARTNER_MAP_IMPORT_20261020.md`.

## Vérifications et limites

- Build Release : 0 erreur (205 avertissements ; delta incrémental non comparé).
- `GivreClone.Tests` : **1 823 PASS / 2 SKIP / 0 FAIL** au dernier passage
  global, après correction du faux positif `MappingContractTests` produit par
  un helper local `F(...)` confondu avec une consultation protobuf.
- `GivreClone.PureTests` : 211 PASS.
- `GivreMcp/Smoke.ps1` : 53 outils exacts PASS, incluant
  `query_world_atlas` déclaré read-only.
- Python `unittest` collecteur : 15 PASS.
- Cas négatifs atlas/MCP : couches superposées sans lien, secteurs disjoints,
  critère conditionnel, version/hash invalides, destination inconnue,
  provenance non autorisée, requête excessive, aucun faux chemin.

Des échecs intermittents historiques ont été observés en suite chargée :
`MZP0PacketBusTests.Publish_commits_waiter_before_slow_subscriber_even_if_timer_fires`
et `LuaRuntimeTraceProjectionTests.Reports_actual_moonsharp_line_and_bounded_neighbouring_source_lines`.
Ils ont réussi isolément ou lors de la dernière suite globale mais leur
cause de timing n'est pas corrigée.

**Non certifiés** : toutes les cartes et grottes du monde 3.7.12.12, transitions
réelles de zaap, disponibilités de minerai, banque/HDV live, comparaison de
prix, trajet Mineur exécuté, achats, combat, script Lua produit par templates,
droits d'usage IA des exports DofusDB, rendu visuel dans une session Windows
interactive et ACK serveur. PR conservée en brouillon ; `master` non modifié.
