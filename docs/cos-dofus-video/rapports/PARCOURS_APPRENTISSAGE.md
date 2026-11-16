# Apprendre le combat DOFUS 3 — parcours guidé et cas pratiques

**Public** : joueur débutant, chercheur vidéo, et développeur du simulateur GivreClone. Objectif : savoir lire un combat, comprendre les interactions, identifier un bon combo et vérifier s'il est reproductible. Les valeurs non établies sont laissées ouvertes, conformément aux fiches spécialisées.

**Parcours recommandé après ce cours** : travailler un cas par classe dans [l'atelier des 19 classes](ATELIER_19_CLASSES_CAS.md), comparer le solo au duo/trio/quatuor dans [M32](M32/RAPPORT.md), puis mesurer l'occupation/attribution/challenges d'une équipe étendue dans [M33](M33/RAPPORT.md). Un boss à phase doit être étudié avec [M27 Torkélonia](M27/RAPPORT.md), [M28 Solar](M28/RAPPORT.md), [M30 Protozorreur/Krobes](M30/RAPPORT.md) ou [M31 Bethel](M31/RAPPORT.md), et l'équipement avec [EQUIPEMENT_STATS_ET_VOIES.md](EQUIPEMENT_STATS_ET_VOIES.md). Pour transformer une règle en contrat technique, consulter [CONTRAT_INTEGRATION_IA_COMBAT.md](CONTRAT_INTEGRATION_IA_COMBAT.md). Les exercices ne sont jamais des replays observés.

## Leçon 1 — Lire un combat, pas seulement un sort

Sur chaque image d'un tour, formuler cinq questions :
1. **Qui agit ?** Un personnage, un compagnon, une invocation contrôlée, une bombe/tourelle ou un monstre ? Quel propriétaire et quelle équipe ?
2. **Qu'est-ce qui est connu ?** Cellule, PV, PA, PM, états, relances, ligne de vue ; ce qui n'est pas observé demeure incertain.
3. **Quel résultat cherche-t-on ?** Tuer une cible, ouvrir une ligne de vue, survivre au tour suivant, remplir un challenge, débloquer un boss ou gagner du temps de préparation ?
4. **Quel événement rend l'action utile ?** Un sort direct, déplacement confirmé, zone traversée, début de tour, changement de résistance, mort d'une bombe ?
5. **Quel est le plan B ?** Si le sort est refusé, le monstre change de place, une invocation meurt ou un effet est esquivé, que peut-on faire maintenant ?

**Exercice** : comparer un Crâ qui inflige 100 dégâts immédiatement et un Enutrof qui empêche un ennemi d'atteindre un allié le tour suivant. On ne peut choisir le meilleur sans connaître les dégâts entrants évités et la nécessité de finir la cible. Les valeurs « 100 » sont un exemple pédagogique inventé, pas un jet observé.

## Leçon 2 — Déplacement forcé, obstacles et enchaînement

Illustration **purement conceptuelle** d'une ligne de cases relatives :

```text
    sens du déplacement →
 P  E  .  T  X
       │  │
       │  └─ obstacle X
       └──── T = piège ou marque éventuelle
P = placeur ; E = ennemi ; . = case libre
```

Si un placeur veut pousser E jusqu'à T, le résultat dépend de la distance réelle, des obstacles, des protections, du chemin exact et du déclencheur du piège. **Ne pas supposer que traverser T et finir sur T produisent le même effet.**

- Plan positif proposé : état initial connu, déplacement légal confirmé vers T, puis événement de marque observé.
- Négatif A : X bloque une partie du mouvement ; mesurer la cellule d'arrêt et les effets éventuels.
- Négatif B : E est indéplaçable ; aucun déplacement ni déclenchement attendu.
- Négatif C : piège d'un allié retiré avant la poussée ; pas de dégâts fantômes.

**Pourquoi c'est important pour le code** : une action de déplacement fait partie de l'état du plateau et peut déclencher d'autres effets. Le planificateur doit attendre son résultat réel avant de prédire la prochaine cible. Voir [Sram](M01/FICHE_SRAM.md) et [poussée](M10/FICHE_POUSSEE.md).

## Leçon 3 — Sram : penser en séquences de marques

Un réseau fonctionne à travers une **chronologie** : pose de marque A, pose de marque B, force de déplacement, éligibilité à déclenchement, effets puis retrait. Deux pièges placés à des instants différents peuvent demander une priorité de résolution. L'existence d'un événement de déclenchement ne prouve pas à elle seule que la marque a disparu.

```text
S0 = {A absent, B absent, E à e0}
  -- pose A confirmée --> S1
  -- pose B confirmée --> S2
  -- déplacement E confirmé --> S3
  -- déclencheur(s) observés --> S4
  -- retrait de marque(s) observé --> S5
```

**Exercice :** comparer A puis B versus B puis A, et une version avec obstacle qui arrête le déplacement avant B. Décrire les états observables attendus ; la priorité interne exacte n'est pas démontrée par la seule transcription. Pour les coûts et pièges actuels voir la [fiche Sram M01](M01/RAPPORT.md), qui distingue les tutoriels de 2023/2025 des données communautaires 3.7.12.

## Leçon 4 — Roublard : préparer sans compter des bombes mortes

Le principe à comprendre est l'**effet différé**. Une bombe doit être posée, rester vivante, éventuellement évoluer avec les tours et interagir avec d'autres bombes/avec la cible. Deux bombes alignées peuvent former un mur dans la base consultée ; il faut en conserver le type, les cases, la vie, le propriétaire et l'état de charge. Un déclenchement par destruction sous Poudre et un Détonateur ne sont pas le même événement.

```text
T1 : bombe B1 sur cellule b1 ; bombe B2 sur cellule b2
T2 : état des bombes réobservé (une peut être morte)
     mur M = relation calculée à partir de B1 et B2 vivantes
     si déplacement légal confirmé : cible entre dans zone de M
     si explosion : enchaînements confirmés, dégâts calculés ensuite
```

**Exercice positif :** deux bombes appropriées restent présentes jusqu'au tour 2 ; le joueur amène l'ennemi dans la zone et observe un événement de mur. **Négatif :** B2 meurt à la fin du tour 1 : un simulateur qui garde un mur malgré sa disparition échoue.

Voir [M02 Roublard](M02/FICHE_ROUBLARD.md), tutoriels Titiloo 2025 et 2026 ; les valeurs de « Combo » affichées oralement sont sujettes à divergence et ne sont pas copiées dans cet exercice.

## Leçon 5 — Xélor : un état, une position passée et un gain retardé

Pour comprendre un Téléfrag, dessiner **deux positions avant** et **deux positions après** une tentative d'échange. Les transitions doivent porter sur des entités identifiées. Des guides 3.7 indiquent un remboursement de PA sous conditions : celui-ci a besoin d'un événement confirmé et d'un compteur propre au sort/tour. Un état consommé ne doit plus être requis par l'action suivante.

```text
avant : X@x0, E@e0, Telefrag(X)=?, Telefrag(E)=?
       demande de déplacement/échange
après : X@x1, E@e1 (cases confirmées)
        état Telefrag observé sur X et/ou E
        points PA observés sur X (ne jamais présumer)
        prochain lancer soumis aux états encore actifs
```

**Exercice :** deux situations identiques sauf la case de destination libre/occupée ; décider si le plan peut utiliser un sort consommant Téléfrag. Voir [M03 Téléfrag](M03/FICHE_XELOR.md) ; ancien tutoriel 2022 seulement historique, source de mécanique à revérifier en 3.7.

## Leçon 6 — Éliotrope : une sortie de portail qui n'est pas intuitive

Le tutoriel Yossat [axes à 1:36](https://www.youtube.com/watch?v=PTSHfNPspSc&t=96s), [diagonales à 4:04](https://www.youtube.com/watch?v=PTSHfNPspSc&t=244s), [erreurs à 10:25](https://www.youtube.com/watch?v=PTSHfNPspSc&t=625s) explique comment un portail supplémentaire et une situation d'équidistance peuvent modifier la redirection voulue. Il faut **recenser tous les portails** et conserver leur état plutôt que choisir la sortie visuellement « la plus proche ».

**Exercice avec carte réelle à fournir :** disposer deux portails P2 et P3, puis une entrée P1. Proposer deux placements de P1 dont un crée une égalité selon la métrique du jeu. Prédire le trajet avec la règle versionnée et comparer à des captures à temps précis. Sans coordonnées visuelles, la transcription ne suffit pas à calculer l'algorithme complet. Voir [M04 Éliotrope](M04/RAPPORT.md).

## Leçon 7 — Synergie en deux, trois et quatre personnages

Une **interaction de duo** est exploitable si l'action d'A change l'action légale ou le résultat attendu de B ; associer deux noms n'est pas une preuve de combo. Pour un trio, montrer la contribution marginale de C. Pour un quatuor, montrer comment D apporte survie, contrôle ou dégâts supplémentaires et s'il perturbe le plan.

Exemple **commenté par Xawpiya 2026** : Pandawa → Enutrof → Crâ → Féca (ordre illustratif, pas celui d'un combat réel). Le Pandawa choisit une nouvelle position ennemie, l'Enutrof cherche à réduire la menace de mobilité, le Crâ capitalise sur les lignes de tir, et le Féca protège les alliés exposés. Si l'Enutrof joue après le Crâ, un avantage supposé au tour actuel disparaît. Si le boss est immunisé au placement ou au retrait, un rôle doit changer.

Autres contrastes pédagogiques :
- **Huppermage + Steamer** : deux systèmes qui mettent du temps à se développer, selon [Xawpiya](https://www.youtube.com/watch?v=TmHIdyxtGmY&t=65s) ; risque face à une fenêtre de kill immédiat.
- **Huppermage + Sadida** : contrôle prolongé, mais coût initial en invocations/runes [Xawpiya 3:15](https://www.youtube.com/watch?v=TmHIdyxtGmY&t=195s).
- **Sram + Féca** : retenir l'adversaire pour préparer un piège, selon [HUZ 10:59](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=659s).
- **Pandawa + Roublard** : placement au moment où le mur de bombes fonctionne, selon [HUZ 28:18](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1698s).

Les trois fichiers [duos / trios / quatuors](LIRE_MATRICES.md) fournissent **l'inventaire** des compositions, pas une garantie d'efficacité. Chaque comparaison sérieuse doit préciser niveau, variantes, éléments, objets, initiative, monstres, carte et objectif.

## Leçon 8 — Boss, invocations et challenges

Les adversaires et objectifs changent le meilleur plan. Dans la [fiche Gourlo](M11/FICHE_GOURLO.md), les sources communautaires décrivent une résistance initiale élevée et une interaction avec les tonneaux. Déplacer le boss une case trop tard peut manquer le tour du tonneau ; tuer l'invocation avant son déclenchement peut annuler toute stratégie ; le challenge « Tonneaux truands » ajoute des conditions par ennemi.

Pour un compagnon, le **compteur d'acteur** doit distinguer personnage, compagnon et invocation. Le challenge peut nécessiter l'identité du véritable attaquant, du tueur, de la cible ou de l'élément ; un sort offensif automatique ne doit pas être attribué au mauvais acteur.

## Leçon 9 — Tester ce qu'on croit comprendre

Lorsqu'une vidéo montre un combo spectaculaire, ne demander « combien de dégâts ? » qu'après avoir documenté : `version, acteur, cible, cellule, PA/PM, état avant, déclencheur, état après, preuve horodatée`. Puis construire :
- un test **positif** qui satisfait toutes les conditions ;
- un test **négatif** où un seul paramètre change et le résultat doit différer ;
- un test **inattendu** de refus, perte d'invocation, état absent ou même action déplacée dans l'ordre des tours.

Si les deux premiers tests produisent le même résultat, c'est que la mécanique décrite est insuffisante ou le test n'est pas discriminant. La rubrique `PASSAGE_DEVELOPPEUR.md` de chaque mission traduit cette pratique en état et transition.

## Leçon 10 — Synthèse d'une étude de tutoriel

Pour une lecture efficace d'un nouveau tuto :
1. noter auteur, date, version et sujet mécanique précis ;
2. récupérer le transcript horodaté et les passages exacts ;
3. inspecter les frames où la cellule ou le compteur change ;
4. écrire **une règle** par ligne, provenance et statut compris ;
5. recouper le sort sur la version 3.7 et enregistrer les écarts ;
6. produire un exemple, un contre-exemple et le gain tactique par rapport à l'action simple ;
7. mettre à jour l'index et la matrice de compositions uniquement si une nouvelle interaction a été établie.

**À garder en tête :** une explication pédagogique peut être bonne sans être une preuve de l'algorithme exact du jeu. Les rapports de ce kit font apparaître cette frontière pour que le lecteur comme le développeur sachent ce qu'ils comprennent déjà et ce qu'ils doivent encore vérifier.

