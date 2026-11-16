# M06 — Korbax : dégâts Air, contrôle, mobilité et soutien en équipe

Recherche documentaire du **04/11/2026**. Cible locale connue : DOFUS **3.7.13.17**. La mécanique étudiée couvre un **compagnon jouable sur son propre tour**, allié d'un personnage dans un duo et utilisable dans une composition plus large (personnages de toute classe, duos, trios, quatuors). Elle ne valide pas automatiquement les quelque 50 autres compagnons ni leurs interactions.

## Résultat documentaire et statut

**Korbax est principalement un combattant Air et placeur** : attaque Air avec vol de Fuite, téléportation et poussée, piège d'attirance et retrait PM, échange de position, poussée avec bonus de dommages de poussée, boost de PM/Fuite/dom-pou. Un passif confère des dommages de poussée aux alliés en zone en fin de tour ; l'apport de PM aux *Multiman* a une cible bénéficiaire plus étroite. Six identités de sorts et le passif sont attestés par [Gamosaurus, guide de Korbax lors de la refonte 2.67 (2023)](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman) et corroborés nominalement par [Wiki Dofus : Multiman](https://fr.wiki-dofus.eu/w/Multiman). **Les chiffres, coûts et relances sont ceux de la fiche historique : aucun n'est confirmé pour 3.7.13.17.**

La transcription [INFI, BWORKER DUO ft. Korbax, 02:52–03:01](https://www.youtube.com/watch?v=L8nxkgYvQTc&t=172s) décrit l'usage du retrait de Fuite pour retenir le Bworker ; [05:50–06:01](https://www.youtube.com/watch?v=L8nxkgYvQTc&t=350s) évoque le tour « Becquetage, Explumession deux fois » pour finir un ennemi ; [06:48–07:07](https://www.youtube.com/watch?v=L8nxkgYvQTc&t=408s) mentionne Bec Aiguisé et Souitche comme alternatives de dégâts. [INFI, SKEUNK DUO ft. KORBAX, 02:35–02:41](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=155s) décrit un piège visant le retrait PM ; [05:10–05:26](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=310s) décrit le choix des dommages de poussée face à une cible à forte résistance Air. **Ce sont des déclarations audibles en sous-titres automatiques et non des observations vérifiées des cases/effets.**

Le compagnon est **contrôlé par le joueur** et **prend la place d'un joueur dans le combat** selon [Dofus Pour Les Noobs — Les compagnons](https://www.dofuspourlesnoobs.com/les-compagnons.html). Il dispose de son identité et de son propre tour. Le rang des six sorts est indiqué par cette page pour les niveaux 30, 100 et 160 ; attributs réels et restrictions de chaque sort doivent être relus dans le client 3.7.13.17. Le guide [DOFUS 3 : Guide des combats pour les noobs, 21/12/2024, 21:10–22:13](https://www.youtube.com/watch?v=ne-6NC0HF3Q&t=1270s) mentionne séparément le placement initial contrôlé du compagnon ; preuve externe de principe et **non preuve d'un sort Korbax**.

## Corpus vidéo et accès éprouvé

| Réf. | Métadonnées récupérées par MCP (pas déduites) | Sous-titres et passage examiné | Valeur probante |
| --- | --- | --- | --- |
| V06-A | [BWORKER DUO ft. Korbax](https://www.youtube.com/watch?v=L8nxkgYvQTc), **INFI**, publié **16/02/2025**, durée retournée **8 min** | FR auto, **279 segments**, transcription horodatée entière reçue, curseur final nul ; passages [01:35–03:59](https://www.youtube.com/watch?v=L8nxkgYvQTc&t=95s) et [05:47–07:44](https://www.youtube.com/watch?v=L8nxkgYvQTc&t=347s) | Stratégie déclarée en combat Xélor/Korbax : placement, dégâts, retrait Fuite |
| V06-B | [SKEUNK DUO ft. KORBAX](https://www.youtube.com/watch?v=y1qofhFD9NQ), **INFI**, publié **04/01/2025**, durée retournée **9 min** | FR auto, **311 segments**, curseur final nul ; passages [00:39–02:42](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=39s), [04:40–05:27](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=280s), [07:20–07:38](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=440s) | Impacts de la Pesanteur, dégâts de poussée contre résistances Air, gestion des menaces |
| V06-C | [DÉBLOQUER les COMPAGNONS KORBAX et MASSE dans Dofus 3](https://www.youtube.com/shorts/g29wAwZ9USg), **Capitemflam**, publié **19/10/2026**, **15 secondes** | FR auto, **7 segments**, curseur final nul ; [00:00–00:11](https://www.youtube.com/shorts/g29wAwZ9USg) | Confirmation **déclarée** d'obtention des deux compagnons à 1000 points de succès, **aucune démonstration des sorts** |

Tous les appels MCP `get_video_info`, `get_available_languages` et `get_timed_transcript(lang="fr")` ont fonctionné pour V06-A/B/C. Les sous-titres sont générés automatiquement ; « corx, carx, CBX » désigne vraisemblablement Korbax, « bquette » peut correspondre à Becquetage selon contexte. Aucune frame vidéo n'a été inspectée, aucune durée d'animation ni cellule réelle n'a été mesurée. **Aucun statut `observe-video`, `valide-test` ou `valide-live`.**

## Règles atomiques et versionnage

| ID | Affirmation bornée | Statut | Preuve, réserves |
| --- | --- | --- | --- |
| M06-R01 | Un compagnon est un combattant **contrôlé** prenant une place de joueur, avec tour/ressources propres ; ses sorts ont trois rangs en fonction du niveau | **corrobore-source** | [Dofus Pour Les Noobs, « Un compagnon, c'est quoi ? »](https://www.dofuspourlesnoobs.com/les-compagnons.html). Rangs 30/100/160 ; états exacts version cible `unknown` |
| M06-R02 | **Bec Aiguisé** : frappe Air et vole de la Fuite à une cible | **corrobore-source**, **declare-video** | [Gamosaurus, « Bec Aiguisé »](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman) et [INFI Bworker 02:52–03:01](https://www.youtube.com/watch?v=L8nxkgYvQTc&t=172s). Coût historique 4 PA ; version cible `unknown` |
| M06-R03 | **Becquetage** : téléporte Korbax sur une cellule puis frappe Air/pousse des entités en zone ; son placement affecte à la fois le risque et les dégâts ultérieurs | **corrobore-source**, **declare-video** | [Gamosaurus, Becquetage](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman), [INFI Bworker 05:50–06:00](https://www.youtube.com/watch?v=L8nxkgYvQTc&t=350s). Ordre exact TP/zone/dégâts/poussée à **prouver** |
| M06-R04 | **Nimbostratus** : piège d'attirance et retrait PM, dommages Air aux ennemis dans la zone décrite historiquement | **corrobore-source**, **declare-video** | [Gamosaurus, Nimbostratus](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman) et [INFI Skeunk 02:35–02:41](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=155s). Timing de déclenchement/consommation `unknown` |
| M06-R05 | **Souitche** : échange de positions avec la cible et dégâts Air aux ennemis selon une fiche historique | **corrobore-source** ; occasion tactique **declare-video** | [Gamosaurus, Souitche](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman), [INFI Bworker 06:48–07:07](https://www.youtube.com/watch?v=L8nxkgYvQTc&t=408s). Éligibilité cible alliée/environnement/états `unknown` |
| M06-R06 | **Explumession** repousse la cible, augmente les dommages de poussée du lanceur ; exploitable pour dommages de collision sous condition géométrique | **corrobore-source**, **declare-video** | [Gamosaurus, Explumession](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman), [INFI Skeunk 05:10–05:26](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=310s). 4 cases/3 PA **valeurs de 2.67**, `unknown` en 3.7 |
| M06-R07 | **Envolée** : bonus de PM, Fuite et dommages de poussée sur des alliés en zone ; **Sang de Corbac** passif déclencheur en fin de tour du lanceur, PM réservé aux autres Multiman de la zone | **corrobore-source** | [Gamosaurus, Passif et Envolée](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman). Zone, durée et montant à revalider |
| M06-R08 | Dans le combat Skeunk, l'auteur privilégie parfois les dégâts de poussée de Korbax lorsque l'adversaire résiste fortement à l'Air | **declare-video** | [INFI Skeunk 05:10–05:26](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=310s) ; **pas** une formule de résistance ni règle universelle |
| M06-R09 | Un tour sans attaque peut être cohérent si aucun dégât légal/utile n'est accessible, ou si contrôle/soutien prépare l'action d'un allié | **hypothese de décision**, pas règle de jeu | [INFI Skeunk 01:30–01:47](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=90s) : le **Xélor** explique un tour sans frappe pour éviter un soin immédiat. Ne **pas** attribuer ce cas à Korbax |
| M06-R10 | Préserver le **vrai bénéficiaire** des boosts : Envolée peut profiter à tout allié en zone ; le bonus PM du passif concerne les Multiman selon la fiche historique | **corrobore-source** | [Gamosaurus](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman). Pas de transmission fictive de PA du Sram/Xélor au Korbax |
| M06-R11 | Les six sorts et le passif, leurs rangs, dégâts et géométries doivent être **résolus dynamiquement dans le client cible** avant toute proposition exécutable | **hypothese d'architecture** | Écart temporel V06-A/B (2025), Gamosaurus (2023) et version cible 3.7.13.17 ; [MAJ 3.7, 04/10/2026](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) n'est **pas** une fiche Korbax |

### Tableau de sélection tactique, sans nombre supposé pour 3.7

| Choix candidat | Cible / bénéficiaire réel | Conditions critiques à examiner | Risque d'un plan naïf |
| --- | --- | --- | --- |
| Bec Aiguisé | Ennemi endommagé ; Korbax récupère de la Fuite ; cible perd Fuite | PA et portée, cible légale, relance, résistances Air | Attaque tentée hors portée ; vol de Fuite sans utilité réelle |
| Becquetage | Korbax se repositionne ; ennemis dans zone endommagés ; entités repoussées | case de destination libre, portée en ligne, états anti-TP, risques alliés, obstacle final | Compter les dommages avant déplacement, pousser un allié ou entrer dans une zone dangereuse |
| Nimbostratus | Cibles traversant le piège ; ennemis subissent éventuellement Air/retrait PM | pose autorisée, ordre piège vs déplacement, cycle de vie, occupants | Créditer l'effet dès la pose ; déclarer succès de retrait PM sur simple demande |
| Souitche | Korbax et cible échangent leurs positions ; ennemi potentiellement endommagé | cible typée et atteignable, état de la cible, règle d'échange, effets secondaires | Déduire une permutation d'un ACK ou supposer qu'elle fonctionne sous Pesanteur |
| Explumession | Korbax gagne le bonus personnel ; cible recule ; ennemi peut subir collision | géométrie, obstacle, déplacement réellement possible, résistances poussée | Garantir dégâts de poussée en espace libre |
| Envolée / Sang de Corbac | Tous les alliés éligibles dans la zone ; PM du passif pour Multiman éligibles | moment du tour, zone, cooldown, identité des bénéficiaires | Utiliser des buffs absents pour planifier l'attaque du joueur précédent |

## États et transitions à modéliser

**Identités :** `fighterId(Korbax)` ≠ `fighterId(personnage)`, même si tous deux sont commandés par le même contrôleur humain. Il existe un lien `controller/companionOwner` mais **les coûts PA, PM, dégâts, états, relances et ordre de tour appartiennent à chaque fighter**. Les règles d'un challenge peuvent dépendre de l'identité du combattant ; cette relation est étudiée dans M07.

**Entrées nécessaires :** version/client/spell rank ; carte et cellules ; équipes de tous alliés ; initiative ; action courante et statut serveur (demandée, acceptée, effets reçus, refusée/unknown) ; PA/PM propres ; sorts actifs, rangs, PO/LoS/ligne, cooldown/casts, buff en cours et bénéficiaire, pièges et cible(s) ; PV/résistances Air et poussée ; états Pesanteur/Indéplaçable et variantes.

**Transitions proposées :** action légale → requête → confirmation serveur → coût débité → déplacement/zone/effect réellement observés → état mis à jour. Piège : `absent→posé_confirmé→armé→déclenché→retiré_confirmé` avec état `unknown` si lacune ; effet de fin de tour : après événement de fin de tour confirmé, calcul des bénéficiaires depuis une zone **à confirmer selon la version**. Dégâts directs et dommages de poussée restent deux événements attribués à leur **source réelle**.

**Plan conservateur :** sur chaque tour, comparer des candidats `dégât`, `entrave`, `déplacement`, `boost utile`, `attente/repli`. L'absence d'attaque n'est un bug que si **au moins une action de dégâts légale et réellement avantageuse** est prouvée dans l'état exact. Sinon l'étiquette doit être `unknown` ou décision admissible, pas « Korbax refuse de jouer ».

## Tests proposés et limites

| ID | Préconditions minimales / entrée | Action | Résultat attendu observable | Statut |
| --- | --- | --- | --- | --- |
| M06-P01 — dégâts utiles | Korbax actif avec PA, Bec Aiguisé appris au rang du client, ennemi vulnérable Air en ligne légale | lancer Bec Aiguisé sur l'ennemi | Confirmation serveur, dégâts Air, variation Fuite selon effet du rang ; bénéficiaire Korbax, cible ennemie | **propose, non exécuté** |
| M06-N01 — géométrie | mêmes acteurs mais cible hors PO / cellule bloquée selon règle réelle | soumettre le candidat dans simulateur uniquement | Aucun lancer proposé ; **pas de dommage crédité** ; si serveur refuse, état exact inchangé hors événement confirmé | **propose, non exécuté** |
| M06-P02 — placement | Korbax et cible éligible, 2 cases vides pour un changement de position | Souitche | Vérifier deux `fighterId` permutés, dégâts Air éventuels sur l'ennemi, aucun changement d'identité de contrôle | **propose, non exécuté** |
| M06-N02 — état | même fixture, état anti-échange/TP confirmé chez une entité | Souitche / Becquetage en candidat | ne pas optimiser sur une permutation non confirmable ; résultat/refus véritable `unknown` jusqu'à preuve | **propose, non exécuté** |
| M06-P03 — boost | Korbax et deux alliés (dont un Multiman) dans la zone de passif à fin de son tour | fin de tour confirmée | Capturer buffs par identifiants : dommages poussée aux alliés éligibles ; PM au Multiman selon source historique | **propose, non exécuté** |
| M06-N03 — absence d'attaque | Korbax à PA insuffisants ou tous dégâts hors PO ; ennemi à résistance Air élevée, soutien disponible | choix d'une action de soutien ou fin de tour | Absence de dommage immédiat **admissible**, pas d'attaque fictive ni d'inférence de bug. Variante avec attaque disponible à comparer | **propose, non exécuté** |
| M06-D01 — piège coupé | Nimbostratus confirmé et cible déplacée à travers la zone | entrée → effets → retrait | Distinguer activation à l'entrée, retrait PM, dégâts Air et retrait de marque ; ordre `unknown` jusqu'au replay | **propose, non exécuté** |
| M06-D02 — combo challenge | joueur A, Korbax K, ennemi M1 déjà attaqué par A ; challenge Duel actif | K propose un dégât Air sur M1 | **Bloquer par politique conservatrice** l'attaque attribuée à K tant que règles spécifiques à compagnon inconnues (M07) | **propose, non exécuté** |

Les coordonnées exactes, coûts 3.7, résistances et état de la carte seront injectés depuis un replay. Les passages du guide 2023 indiquant 12 PA, 6 PM et autres statistiques de Korbax au niveau 200 **ne sont pas repris comme valeurs d'exécution**.

## Reprise et suites

- Vidéos A/B/C : métadonnées, langues et transcriptions **récupérées entièrement**, aucune pagination restante.
- Images de plateau : **non inspectées** ; prochaine séquence à inspecter [Bworker 05:48–06:01](https://www.youtube.com/watch?v=L8nxkgYvQTc&t=348s) et [Skeunk 05:10–05:26](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=310s). Aucune action en jeu.
- Preuve prioritaire : exporter d'une source versionnée 3.7.13.17 le rang, effets et contraintes exacts des six sorts/passif ; ensuite replay 1 action de dommage + 1 effet d'entrave + 1 effet de placement + 1 tour « sans attaque ».
- Contradiction non résolue : les règles numériques de Gamosaurus 2.67 ont possiblement changé ; ne pas prétendre qu'un combat 2025 valide leur fidélité 2026.

