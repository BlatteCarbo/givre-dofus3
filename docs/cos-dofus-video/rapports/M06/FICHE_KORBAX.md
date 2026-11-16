# M06-KORBAX-001 — Compagnon Air, soutien et déplacement

## Identité

- **Mécanique retenue :** choisir une action légale de Korbax (dégât, contrôle, placement ou soutien) et mettre à jour son **propre état** ; comparer au cas sans attaque.
- **Équipe :** un combattant Korbax, un personnage qui le contrôle et des ennemis ; les politiques doivent fonctionner avec duo/trio/quatuor et toute classe alliée. Un joueur et son compagnon sont deux combattants.
- **Version :** vidéos DOFUS Unity 2025, source d'effets remontant à la refonte **2.67 (2023)** ; cible locale **3.7.13.17** ; compatibilité des valeurs **unknown**.
- **Date :** 04/11/2026.
- **Vidéos MCP :** [INFI Bworker 16/02/2025, 8 min](https://www.youtube.com/watch?v=L8nxkgYvQTc) ; [INFI Skeunk 04/01/2025, 9 min](https://www.youtube.com/watch?v=y1qofhFD9NQ) ; [Capitemflam 19/10/2026, 15 s](https://www.youtube.com/shorts/g29wAwZ9USg).
- **Langue :** FR automatique, transcription horodatée MCP entière (respectivement 279, 311, 7 segments ; `next_cursor=null`). Intervalles utiles : [Bworker 02:52–03:01](https://www.youtube.com/watch?v=L8nxkgYvQTc&t=172s), [05:50–06:01](https://www.youtube.com/watch?v=L8nxkgYvQTc&t=350s), [Skeunk 02:35–02:41](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=155s), [05:10–05:26](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=310s), [Short 00:00–00:11](https://www.youtube.com/shorts/g29wAwZ9USg).
- **Méthode :** métadonnées/langues/transcriptions MCP ; recoupement [Gamosaurus Korbax](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman), [Dofus Pour Les Noobs — Compagnons](https://www.dofuspourlesnoobs.com/les-compagnons.html), [Wiki Dofus — Multiman](https://fr.wiki-dofus.eu/w/Multiman). Pas de frame ni de log serveur.

## Règles atomiques et preuve

| ID | Règle | Statut | Preuve |
| --- | --- | --- | --- |
| M06-R01 | compagnon = combattant contrôlé à part entière ; ses ressources ne sont pas celles du joueur | **corrobore-source** | [Dofus Pour Les Noobs](https://www.dofuspourlesnoobs.com/les-compagnons.html) |
| M06-R02 | Bec Aiguisé = dégâts Air et vol de Fuite | **corrobore-source**, **declare-video** | [Gamosaurus](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman), [Bworker 02:52](https://www.youtube.com/watch?v=L8nxkgYvQTc&t=172s) |
| M06-R03 | Becquetage = téléportation du lanceur puis dommages Air/poussée en zone | **corrobore-source**, **declare-video** | [Gamosaurus](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman), [Bworker 05:50](https://www.youtube.com/watch?v=L8nxkgYvQTc&t=350s) |
| M06-R04 | Nimbostratus = piège attirance, dommages Air ennemis, retrait PM | **corrobore-source**, **declare-video** | [Gamosaurus](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman), [Skeunk 02:35](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=155s) |
| M06-R05 | Souitche = échange de positions, attaque Air conditionnelle | **corrobore-source** | [Gamosaurus](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman) |
| M06-R06 | Explumession = poussée et bonus personnels dommages poussée | **corrobore-source**, **declare-video** | [Gamosaurus](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman), [Skeunk 05:10](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=310s) |
| M06-R07 | Envolée donne un soutien en zone ; Sang de Corbac proc en fin de tour et donne du PM aux autres Multiman éligibles | **corrobore-source**, version actuelle **unknown** | [Gamosaurus](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman) |
| M06-R09 | Un tour Korbax sans attaque directe peut être acceptable | **hypothese**, pas règle confirmée | [Skeunk 01:30–01:47](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=90s) traite **le Xélor**, ne prouve pas une cause pour Korbax |

## État initial reproductible proposé

Fixture de recherche **K-01**, coordonnées **artificielles**, non extraites d'images : K = Korbax en (0,0) ; A = personnage allié en (-1,0) ; M = ennemi en (3,0) ; P = position cible libre à (1,0). Une deuxième entité ennemie N en (4,1) permet de tester la zone. Les portées exactes depuis K, obstacles et effets sont **chargés depuis la version cible** plutôt que supposés. L'ordre de tour du fixture : A puis K puis M puis N. Les véritables PA/PM, résistances Air/poussée, états, relances, niveaux et rangs des six sorts sont **unknown** jusqu'à acquisition de données client ; aucun compte ou capacité n'a été utilisé.

## Séquence / transitions conditionnelles

| Étape | Acteur | Action / cible | Préconditions | Coût | Résultat et transition attendus | Preuve |
| --- | --- | --- | --- | --- | --- | --- |
| K0 | Serveur | début de tour K | identité K distincte de A, initiative arrivée | 0 | charger PA/PM et buffs/relances **confirmés de K** | [Compagnons](https://www.dofuspourlesnoobs.com/les-compagnons.html) |
| K1 | K | Bec Aiguisé sur M | portée/cible et PA légaux, sans obstacle incompatible | coût **unknown 3.7** (fiche 2.67 : 4 PA) | confirmer dégâts Air et vol Fuite appliqué à M et K | [Gamosaurus](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman), [Bworker 02:52](https://www.youtube.com/watch?v=L8nxkgYvQTc&t=172s) |
| K2 | K | Explumession sur M | M dans la ligne/portée du rang réel, K a PA restants, trajectoire calculable | **unknown 3.7** (fiche 2.67 : 3 PA) | mise à jour bonus de K, déplacement réel de M ; dommage collision seulement si vérifié | [Skeunk 05:10](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=310s), [Gamosaurus](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman) |
| K3 | K | Envolée en zone alliée | rang, relance, centre et zone compatibles | **unknown 3.7** (fiche 2.67 : 2 PA) | appliquer buffs **par fighter bénéficiaire**, pas au groupe abstrait | [Gamosaurus](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman) |
| K4 | K | fin de tour | événement serveur confirmé | 0 | évaluer Sang de Corbac et attribuer buffs en zone au bon moment **si présent dans rang actuel** | [Gamosaurus](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman) |
| K-alt1 | K | Nimbostratus posé en P | cellule libre, PO, rang et nombre de marques autorisés | **unknown 3.7** | piège armé confirmé ; déclenchement ultérieur par entrée, dégâts/retrait PM à confirmer | [Skeunk 02:35](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=155s) |
| K-alt2 | K | Souitche sur M | échange légal, états et deux cellules permutables | **unknown 3.7** | positions K/M échangées seulement après résultat serveur | [Gamosaurus](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman) |
| K-alt3 | K | Becquetage sur P | ligne/portée/terrain/états autorisent téléportation | **unknown 3.7** | position K changée, zone repoussée/dégâts suivant ordre réel **unknown** | [Bworker 05:50](https://www.youtube.com/watch?v=L8nxkgYvQTc&t=350s) |

**N.B.** K1–K3 forment des **branches de plan**, pas une exécution vidéo unique prouvée dans un tour. Ne pas additionner leurs coûts ou buffs sans démontrer les PA disponibles et les relances. Les deux Mortels du Sram M01 n'interviennent pas ici.

## Intérêt tactique / repli

Un Korbax peut frapper lui-même, retenir une cible en abaissant sa Fuite, éloigner une menace pour préserver A, ou renforcer le placement/dommages de poussée d'un allié **qui pourra encore agir après K**. Si A a déjà terminé son tour, le buff PM/DoPou de K ne justifie pas rétroactivement une action de A ; vérifier la durée jusqu'à son prochain tour. Pour duo/trio/quatuor, recalculer l'ordre exact d'initiative de **chaque** personnage et compagnon, puis l'état challenge (voir M07). Le soin direct n'apparaît pas dans les six sorts historiques : ne pas assigner à Korbax un rôle de soigneur sans nouvelle preuve.

Si M n'est pas atteignable par Bec Aiguisé : tenter une action de contrôle légale utile (Nimbostratus, placement d'un ennemi, Envolée) ou un repositionnement. Si aucun sort légal confirmé, conserver `unknown` pour la raison d'inaction ; **ne pas déclarer une panne IA**. Tout refus invalide l'hypothèse de la transition projetée ; replanifier depuis l'état serveur, pas depuis l'animation.

**Dégâts :** types Air et poussée attestés par les sources ; formule, résistances, résistances de poussée, jets et montants 3.7 `unknown`. Une frappe réalisée dans V06-A/B n'est pas une formule.

## Tests discriminants proposés

| Cas | Entrées / actions | Oracle observable attendu | Règle distinguée | Statut |
| --- | --- | --- | --- | --- |
| **Positif P01** | K avec Bec Aiguisé disponible, M en zone légale ; K lance | événement de cast par **K**, dommages Air de M, Fuite cible/K séparément ; débits PA de K seulement | R01/R02 | **propose, non exécuté** |
| **Négatif N01** | M hors PO ou ligne bloquée pour Bec Aiguisé | pas de cast proposé et pas de dégâts inventés ; si refus serveur, état Unknown/refus explicite | R02 | **propose, non exécuté** |
| **Positif P02** | K et M éligibles à Souitche, cellules distinctes et libres | deux positions réelles permutées ; cible blessée seulement si ennemie et effet confirmé | R05 | **propose, non exécuté** |
| **Négatif N02** | K ou M sous Pesanteur ou indéplaçabilité, variante identique | comparer refus/effect reçu ; ne pas supposer que l'état interdit tous les sorts de façon uniforme | R03/R05 | **propose, non exécuté**, sortie exacte `unknown` |
| **Discriminant D01** | M dans ligne de poussée : terrain libre versus obstacle derrière M | dégâts de collision seulement s'ils sont confirmés ; trajectoire et résistances modifient l'intérêt d'Explumession | R06/R08 | **propose, non exécuté** |
| **Positif P03** | A et un deuxième Multiman à portée du passif, K termine son tour | buffs individuels effectivement reçus, PM uniquement aux bénéficiaires éligibles selon version | R07/R10 | **propose, non exécuté** |
| **Négatif N03** | K sans cible attaquable, soutien disponible et challenge interdisant une attaque | choix de support ou fin de tour ; ne pas simuler un hit pour « remplir » un tour | R09 | **propose, non exécuté** |

## Passage au développeur / reprise

Les **trois priorités** : (1) identité de combattant et ressources séparées ; (2) typage des actions et de leurs bénéficiaires/zone, avec passage exact par le serveur ; (3) selection légale face à la géométrie, l'état challenge et les résistances, incluant « aucune attaque optimale ». Oracle à obtenir sur un replay versionné comprenant un tour Korbax et son passif en fin de tour. Visionnage de frames et protocole serveur non réalisés ; toutes ces propositions restent théoriques. Dernier passage de transcription examiné : [Bworker 07:44](https://www.youtube.com/watch?v=L8nxkgYvQTc&t=464s), [Skeunk 07:38](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=458s), [Short 00:11](https://www.youtube.com/shorts/g29wAwZ9USg) ; pas de pagination restante.

