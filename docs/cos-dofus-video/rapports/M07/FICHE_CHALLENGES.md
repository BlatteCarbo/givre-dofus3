# M07-CHALLENGES-001 — Duel et De la même lignée

## Identité

- **Identifiant stable :** M07-CHALLENGES-001.
- **Sujet :** conditions de validation et attribution d'acteur dans deux challenges ; toutes classes, contrôleurs multi-comptes, compagnons, duo/trio/quatuor, ennemis X et Y.
- **Source de règle :** descriptions [Dofus Pour Les Noobs — challenges, mise à jour 2.68 (2023)](https://www.dofuspourlesnoobs.com/les-challenges.html) et [Gamosaurus — 2.68](https://www.gamosaurus.com/jeux/dofus/dofus-2-68-tous-les-challenges-apres-la-refonte). **Cible : DOFUS 3.7.13.17**, compatibilité exacte non démontrée (`unknown`).
- **Analyse :** 04/11/2026.
- **Vidéo V1 :** [Ouatdefok, DOFUS 3 : GUIDE DES COMBATS POUR LES NOOBS, 21/12/2024, 46 min](https://www.youtube.com/watch?v=ne-6NC0HF3Q) ; FR auto, 1362 segments MCP, `next_cursor=null`. Extraits [14:27–17:02](https://www.youtube.com/watch?v=ne-6NC0HF3Q&t=867s), [21:10–22:13](https://www.youtube.com/watch?v=ne-6NC0HF3Q&t=1270s), [42:58–43:08](https://www.youtube.com/watch?v=ne-6NC0HF3Q&t=2578s).
- **Vidéo V2 :** [Laniyelle, NEWS - MAJ 2.68: Suppression des Idoles et Refonte Eniripsa, 16/06/2023, 4 min](https://www.youtube.com/watch?v=BbcElOsyEO8) ; FR auto, 122 segments MCP, `next_cursor=null`, [01:20–01:34](https://www.youtube.com/watch?v=BbcElOsyEO8&t=80s).
- **Méthode :** MCP métadonnées, langues et transcription horodatée ; recoupement de textes de challenges 2.68 ; consultation de l'extrait indexé [Fandom](https://dofuswiki.fandom.com/wiki/Fighting_Challenge) (page intégrale inaccessible 402). **Aucune image de combat inspectée** ; aucun test.

## Règles atomiques et preuves

| ID | Règle à rendre explicite | Statut | URL exacte et portée |
| --- | --- | --- | --- |
| M07-R01 | Duel : l'ennemi X attaqué par un combattant allié A ne doit plus être attaqué par un autre combattant allié K pendant le reste du combat | **corrobore-source**, 3.7 **unknown** | [Dofus Pour Les Noobs, entrée « Duel »](https://www.dofuspourlesnoobs.com/les-challenges.html) ; [Ouatdefok 42:58](https://www.youtube.com/watch?v=ne-6NC0HF3Q&t=2578s) mentionne Duel mais ne montre pas le cas |
| M07-R02 | De la même lignée : chaque combattant allié doit finir son tour **en ligne avec ≥1 autre combattant allié** | **corrobore-source**, 3.7 **unknown** | [Dofus Pour Les Noobs](https://www.dofuspourlesnoobs.com/les-challenges.html) et [Gamosaurus](https://www.gamosaurus.com/jeux/dofus/dofus-2-68-tous-les-challenges-apres-la-refonte) |
| M07-R03 | Duel doit conserver `enemyId → attackerFighterId` pour tous les tours du combat | **hypothese de formalisation** | Découle du texte R01 ; transition serveur réelle `unknown` |
| M07-R04 | Lignée requiert un test `sameLine(current, any other eligible ally)` **à la fin du tour courant** ; ne pas confondre avec « En ligne et contre tous » | **corrobore-source** pour exigence, **hypothese** pour instant exact de mise à jour | [Gamosaurus](https://www.gamosaurus.com/jeux/dofus/dofus-2-68-tous-les-challenges-apres-la-refonte) |
| M07-R05 | Le compagnon contrôlé est un fighter distinct ; sa participation au challenge est probable, **pas prouvée sur 3.7** | **corrobore-source** général + **unknown** actuel | [Dofus Pour Les Noobs, compagnons](https://www.dofuspourlesnoobs.com/les-compagnons.html), [Fandom extrait Sidekicks](https://dofuswiki.fandom.com/wiki/Fighting_Challenge) |
| M07-R06 | Attribution des dégâts d'invocations, pièges, poisons, pousses et refus de cast : pas de preuve suffisante pour l'algorithme Duel 3.7 | **unknown** | [Fandom extrait générique](https://dofuswiki.fandom.com/wiki/Fighting_Challenge) sans accès au texte complet ; tests à obtenir |
| M07-R07 | Un échec d'un combattant compromet le challenge collectif | **corrobore-source** | [Dofus Pour Les Noobs](https://www.dofuspourlesnoobs.com/les-challenges.html) |
| M07-R08 | Cas du dernier tour, décès et invocation en Lignée | **unknown** | Ancienne note indexée [Fandom — « Does not fail on the last turn »](https://dofuswiki.fandom.com/wiki/Fighting_Challenge), non validée 3.7 |
| M07-R10 | « Duel » n'exige pas de tuer chacun un monstre ; cette clause est dans « Chacun son monstre » | **corrobore-source** | [Dofus Pour Les Noobs — entrées distinctes](https://www.dofuspourlesnoobs.com/les-challenges.html) |

## État initial reproductible

Équipe alliée : **A**, joueur de n'importe quelle classe, et **K**, compagnon Korbax contrôlé par A mais possédant son propre `fighterId`. Extension : **B**, personnage supplémentaire ; **C**, quatrième combattant. Les deux ennemis **X** et **Y** sont distincts et vivants. Version du combat, sélection du challenge, initiative de chaque entité et classe de chaque personnage sont des **entrées**, jamais des conditions imposées par cette étude. Le fixture utilise des **positions relatives abstraites** ; le module de carte doit convertir les cases DOFUS dans son propre référentiel.

Pour **Duel** : au départ `duelOwner={}`, aucune attaque alliée confirmée. A dispose d'une attaque légale sur X ; K dispose d'une attaque légale sur X ou Y, coût PA et sort choisis dynamiquement. A et K possèdent **des PA séparés**.

Pour **De la même lignée** : A=(0,0), K=(2,0), X/Y sur cellules quelconques distinctes ; `sameLine(A,K)=true` dans le référentiel abstrait choisi, et tous deux sont éligibles à la vérification. Le tour de A précède celui de K. Équipements, obstacles, état immobilisé, invocations, morts et condition de dernière action sont **unknown** pour la version cible.

## Séquences et transitions

| Étape | Acteur | Action / cible | Préconditions | Coût | Effet immédiat / différé | Preuve |
| --- | --- | --- | --- | --- | --- | --- |
| D0 | Serveur | challenge Duel sélectionné | ID et texte versionnés | 0 | initialiser `Active`, carte de premier attaquant vide | [Dofus Pour Les Noobs — challenges](https://www.dofuspourlesnoobs.com/les-challenges.html) |
| D1 | A | attaque confirmée sur X | cast légal, X vivant | PA du sort A **unknown** | `duelOwner[X]=A` si l'action entre dans « attaquer » | [Duel 2.68](https://www.dofuspourlesnoobs.com/les-challenges.html) |
| D2 | K | attaque confirmée sur Y | Y vivant et sans propriétaire, cast légal de K | PA de K **unknown** | `duelOwner[Y]=K` candidat, sous réserve du statut Sidekick | [Duel 2.68](https://www.dofuspourlesnoobs.com/les-challenges.html), [Fandom extrait](https://dofuswiki.fandom.com/wiki/Fighting_Challenge) |
| D3 (négatif) | K | attaque sur X, déjà assigné A | propriété X=A | PA de K **unknown** | plan rejeté en mode conservateur ; **échec serveur à observer** si action réalisée | [Duel 2.68](https://www.dofuspourlesnoobs.com/les-challenges.html) |
| L0 | Serveur | challenge Lignée sélectionné | identités/positions actuelles | 0 | `Active`, aucun propriétaire par cible | [Lignée 2.68](https://www.dofuspourlesnoobs.com/les-challenges.html) |
| L1 | A | se place puis finit tour à (0,0), K=(2,0) | `TurnEnded(A)` réellement confirmé | PM utilisés **unknown** | `sameLine(A,K)` vrai → maintenir `Active` prédit | [Gamosaurus](https://www.gamosaurus.com/jeux/dofus/dofus-2-68-tous-les-challenges-apres-la-refonte) |
| L2 | K | finit à (2,0) | `TurnEnded(K)` confirmé ; A reste (0,0) | ressources K uniquement | `sameLine(K,A)` vrai → maintenir `Active` prédit | [Gamosaurus](https://www.gamosaurus.com/jeux/dofus/dofus-2-68-tous-les-challenges-apres-la-refonte) |
| L3 (négatif) | K | finit à (2,1), A=(0,0) | aucun autre allié en ligne | ressources K | aucun partenaire → violation de la description **sous réserve des exceptions de version** | [Dofus Pour Les Noobs](https://www.dofuspourlesnoobs.com/les-challenges.html) |

Aucune séquence ne prouve la sortie exacte côté serveur ; notamment on **ne débite pas des PA fictifs** sur simple proposition, et on n'annonce pas `FailedConfirmed` sans signal de challenge. L'état `PredictedFailure` reste distinct de `FailedConfirmed`.

## Intérêt tactique, tous groupes et limites

**Duel :** choisir pour chaque ennemi un combattant capable de le frapper seul jusqu'à la fin ; un support peut soigner/booster un autre allié si cela ne compte pas comme une attaque de la cible ennemie. Un personnage peut aider le Korbax **par placement**, mais **le fait de placer un ennemi par poussée est-il une « attaque » dans Duel ? unknown**. Toute action indirecte doit être prudente. Préparer une solution de repli (changer de cible plutôt que partager X).

**Lignée :** prévoir une position de fin de tour pour chaque combattant **incluant Korbax** et ses mouvements de déplacement. La contrainte de ligne est un coût d'opportunité : PM consommés, exposition et ligne de vue ennemie possible. Dans un **trio** A=(0,0), K=(2,0), B=(2,3), chacun peut avoir au moins un partenaire ; dans un **quatuor** A=(0,0), B=(2,0), C=(2,3), K=(5,3), idem. Un partenaire peut satisfaire la relation sans que toute l'équipe partage une seule ligne. Éviter les actions qui rendent la position de fin de tour impossible et préparer un repli (se réaligner avant de finir). Le texte de challenge n'est pas une définition de LoS : obstacle entre combattants et définition de ligne **unknown**.

**Inconnues bloquantes :** coups de grâce et dernier tour, attaque sans dégât, dégâts renvoyés/poisons/invocations/pièges, challenge appliqué après mort d'un compagnon, invocation éligible comme partenaire, zone de ligne exacte et version 3.7.

## Scénarios de test proposés, non exécutés

| Cas | Entrées / actions | Résultat observable attendu (oracle) | Règle distinguée | Statut |
| --- | --- | --- | --- | --- |
| **D-P01** | A attaque X, K attaque Y, A attaque X | aucun conflit propriétaire dans le modèle ; challenge reste valide **selon texte 2.68**, à vérifier via serveur | R01/R03/R05 | **propose, non exécuté** |
| **D-N01** | A attaque X puis K attaque **X** | violation **présumée** si Sidekick est un autre combattant dans challenge ; lire indicateur serveur | R01/R05 | **propose, non exécuté** |
| **D-D01** | A empoisonne X puis K inflige dommages de poussée à X | sortie `unknown` ; collecter événement d'attribution et règle « attaque » | R06 | **propose, non exécuté** |
| **L-P01** | A=(0,0), K=(2,0), fins de tour A puis K sans perturbation | à chacune des deux fins de tour, au moins un autre allié en ligne | R02/R04 | **propose, non exécuté** |
| **L-N01** | fin du tour A : A=(0,0), K=(2,1), seulement deux alliés | pas de partenaire en ligne ⇒ violation **selon texte 2.68** | R02/R04 | **propose, non exécuté** |
| **L-P02 (trio)** | A=(0,0), K=(2,0), B=(2,3) en fins de tour successives | partenaires A↔K et K↔B ; pas de ligne commune obligatoire | R02/R04 | **propose, non exécuté** |
| **L-D02 (quatuor)** | A=(0,0), B=(2,0), C=(2,3), K=(5,3), chaque acteur garde sa case en fin de tour | tous ont un partenaire, mais **« En ligne et contre tous »** échouerait ; oracle de discrimination | R02/R04 | **propose, non exécuté** |
| **L-D03** | K décède, A est seul survivant et tue X avant fin de son tour | **unknown** : exceptions dernier tour / acteurs morts / nombre minimal d'alliés | R08 | **propose, non exécuté** |

## Passage au développeur et reprise

Donner au simulateur `challengeId + versionedText + attackLedger + fighterIdentity + currentPosition + turnEnded + challengeServerStatus`, et calculer une prédiction **tri-valuée** `safe/unsafe/unknown` distincte du verdict serveur. Source commune à toutes classes et groupes ; ne pas coder deux variantes spécialisées « Korbax vs joueur ».

Les deux transcriptions ont été téléchargées avec curseur final nul ; dernière prise de parole ciblée [V1 43:08](https://www.youtube.com/watch?v=ne-6NC0HF3Q&t=2588s) et [V2 01:34](https://www.youtube.com/watch?v=BbcElOsyEO8&t=94s). Pas de captures de vidéo ou replay, aucune preuve de gameplay directement intitulée « De la même lignée ». Prochaine collecte : capture de l'infobulle 3.7.13.17 et deux courts replays avec Korbax actif.

