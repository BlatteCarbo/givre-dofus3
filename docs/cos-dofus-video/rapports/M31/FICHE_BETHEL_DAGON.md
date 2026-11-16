# Fiche M31-BETHEL-DAGON-001 — transfert de camp et reprise de cycle

## Identité, versions et preuves

- **Sujet** : Boss Bethel Akarna, Monolithe, Dagon des Profondeurs, Nécronyx, Aquazomb ; solo/duo/trio/quatuor avec acteurs UID et équipes.
- **Cible** : DOFUS **3.7**, déploiement 06/10/2026 ; dernier client cité par le kit **3.7.13.17** sans contrôle. Étude effectuée le **04/11/2026**.
- **Source versionnée primaire** : [mise à jour 3.7 du 04/10/2026, Bethel](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html).
- **Guides mécanistes** : [Tour de Bethel, mise à jour 05/10/2026](https://www.dofuspourlesnoobs.com/tour-de-bethel.html), [Dofuserie](https://www.dofuserie.com/donjons/tour-de-bethel/), [Dofus Portals](https://dofus-portals.fr/donjons/tour-de-bethel/).
- **Vidéo** : aucune URL YouTube 2025/2026 spécifique validée en tant que tutoriel Bethel 3.7 ; donc **ni métadonnées MCP ni transcription horodatée**, aucun timestamp fabriqué et **aucune preuve observe-video**. La recherche est uniquement par textes.
- **Statuts** : corrobore-source, hypothese pour transitions exactes et ordre serveur, obsolete-pour-cible pour mécanismes anciens ; aucun valide-test/live.

## Règles atomiques avec preuve exacte

| ID | Affirmation | Statut/source et portée |
| --- | --- | --- |
| **M31-R01** | En début de combat, Bethel, Dagon ennemi invulnérables ; Monolithe invulnérable **à distance**, donc il faut l'attaquer au **contact**. | **corrobore-source** [DofusPourLesNoobs, Principe du combat](https://www.dofuspourlesnoobs.com/tour-de-bethel.html) ; [Dofuserie](https://www.dofuserie.com/donjons/tour-de-bethel/). |
| **M31-R02** | Mort du Monolithe libère/donne un **Dagon allié**, seul acteur apte à **infliger des dommages à Bethel** par Souffle Chaud au contact. | **corrobore-source** [DofusPourLesNoobs](https://www.dofuspourlesnoobs.com/tour-de-bethel.html), [Dofus Portals](https://dofus-portals.fr/donjons/tour-de-bethel/). Le contrôle exact et l'UID sont non observés. |
| **M31-R03** | **Mort du Dagon allié** lorsque Bethel vit encore : le combat continue et Bethel **ressuscite Monolithe et Dagon ENNEMIS** ; il faut tuer Monolithe de nouveau. | **corrobore-source** [patch DOFUS 3.7, 04/10/2026](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), règle nouvelle explicite. |
| **M31-R04** | Bethel doit être sous **Nécronyx au moment de sa mort**, sinon défaite de l'équipe. | **corrobore-source** [DofusPourLesNoobs](https://www.dofuspourlesnoobs.com/tour-de-bethel.html), [Portals](https://dofus-portals.fr/donjons/tour-de-bethel/). |
| **M31-R05** | Depuis 3.7, **lancer Nécronyx ne fait plus passer le tour** ; Bethel retire cet état **3 tours plus tard**, au tour du lanceur, selon exemple T1→T4. | **corrobore-source** [patch](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), [guide Bethel](https://www.dofuspourlesnoobs.com/tour-de-bethel.html). Ancien conseil contraire **obsolete-pour-cible**. |
| **M31-R06** | En 3.7, les monstres **ressuscités n'ont plus une invulnérabilité d'un tour** ; le système Aquazomb peut ressusciter le prochain monstre mort par le prochain allié, Nécronyx avant mort modifie son comportement. | **corrobore-source** [patch](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), [guide Bethel, Aquazomb](https://www.dofuspourlesnoobs.com/tour-de-bethel.html). |
| **M31-R07** | Monolithe : **bouclier 1 tour**, plus de réduction de poisons/pièges ; bonus PM/PO Aquazomb débuffable. | **corrobore-source** [patch 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). |
| **M31-R08** | **Pousser Dagon ennemi** entraîne Inébranlable (guide : pour 1 tour), supprime sa capacité à s'attirer ; placer Dagon allié près de Bethel pour attaquer. | **corrobore-source** [guide Bethel](https://www.dofuspourlesnoobs.com/tour-de-bethel.html). |
| **M31-R09** | À partir de tour 2, finir un tour à **plus de 6 PO de tous les monstres admissibles** attribue des bonus PM/PO et autres à l'ennemi (Aquazomb). | **corrobore-source** [guide Bethel](https://www.dofuspourlesnoobs.com/tour-de-bethel.html). |
| **M31-R10** | Ancienne invulnérabilité des ressuscités et tour passé par Nécronyx ne s'appliquent plus à 3.7. | **obsolete-pour-cible**, **contradictoire** [Dofuserie ancien](https://www.dofuserie.com/donjons/tour-de-bethel/) vs patch primaire. |

## État initial reproductible

- Boss Bethel B, UID non inventé, vivant, invulnérable aux joueurs ; **mE** Monolithe ennemi vivant et **dE** Dagon ennemi vivant ; équipes Tplayer et Tenemy. Pas de Dagon allié encore.
- Initiatives symboliques **P (tank/porteur de Nécronyx) → S (frappeur Monolithe) → B (Bethel) → dE** ; variant N8 inverser S/P ou placer B avant P. Ne pas prétendre que ces ordres sont ceux d'une vidéo.
- **P** possède Nécronyx, portée/cout/relance extraits des **données client au moment du replay**, non fabriqués ; M31-S2 parle « tous les deux tours » pour la disponibilité. Le compteur Nécronyx sur B est séparé du tour de B : exemple source expire au **tour 4 de P** si appliqué au tour 1 de P.
- **S** en mêlée **c_contact** de mE, case contrôlée légalement ; un autre acteur R hors mêlée en contrôle négatif. mE a éventuellement un bouclier « Hiéroglyphes » de durée 1 tour en 3.7 ; montant exact non indispensable à la transition.
- **Dagon allié dA** n'existe pas au départ ; après mort mE, son UID, son propriétaire et son ordre d'initiative sont **observables à obtenir**, pas attribués a priori à un personnage quelconque. Le guide suggère que la proximité avec dE peut influencer le contrôleur : hypothèse à tester.
- **Mort du boss** : B doit avoir Nécronyx **au moment exact de death(B)**, pas simplement au début de la préparation du tour.

## Machine à états, chronologie et branches

| Phase | Déclencheur et acteur | Préconditions | Transition prévue | Preuve / ce qui reste inconnu |
| --- | --- | --- | --- | --- |
| **S0 ENEMY_MONOLITH** | Début fight, B invoque mE et dE | B vivant ; mE camp ennemi | Bethel invulnérable ; approche Monolithe pour dégâts **mêlée** | R01, effet serveur / bouclier exact à capturer |
| **S0** | S frappe mE **à distance** | mE vivant | Ne retire pas l'invulnérabilité distance ; pas de mort monolithe ni de Dagon allié | R01 |
| **S0** | S élimine mE au **contact** | mE vivant, coup légal, réception de sa mort | **S1 ALLIED_DAGON** : création/changement camp d'un dA ; relecture contrôleur, UID et ordre | R02, source guide ; moment et identité à confirmer |
| **S1** | P applique ou renouvelle **Nécronyx(B)** | Tour de P, portée/relance valides | B a état Nécronyx, **P peut poursuivre son tour en 3.7** | R04/R05 ; statut confirmé nécessaire |
| **S1** | dA en mêlée lance **Souffle Chaud sur B** | dA vivant, légal, B vivant | Dommages sur B, distincts des attaques joueurs directes | R02 ; l'IA ou contrôleur peut faire une autre action |
| **S1** | dA meurt **avant** B | B vivant et dA camp des joueurs | **S0 RESTART** : Bethel recrée **mE2** et **dE2** ennemis, la quête ne s'achève pas ; cycle +1 | **R03 source 3.7**, timing/UID non déterminés |
| **S1** | death(B) par dA alors B a **Nécronyx** | Nécronyx présent au coup fatal | **S2 BOSS_DEAD**, finir autres ennemis si existants | R04, ordre fatal/effets simultanés à vérifier |
| **S1** | death(B) par dA sans Nécronyx | état absent/expiré | **SFAIL** : défaite d'équipe selon guide | R04 |
| **Parallèle NÉCRONYX** | Tour P1 applique puis P4 commence | Trois tours de son porteur écoulés | État de B **peut expirer** au P4 ; reposer avant fatal | R05 ; phase exacte à vérifier |
| **Parallèle AQUAZOMB** | mort d'un monstre | autre monstre porteur d'Aquazomb présent | RESURRECTION_PENDING → RESURRECTED ; **pas de 1 tour invulnérable** 3.7 | R06, ne pas confondre avec recréation mE/dE |
| **Parallèle MONOLITH_SHIELD** | début/relance tour de mE | mE vivant | bouclier persiste **1 tour seulement**, poison/piège non réduit | R07 |

**Rapport d'incertitude :** les colonnes « prévu » sont issues du patch/guide, sans journal client. Les changements d'identité/équipe et de tour devront être lus sur source serveur avant intégration automatique.

## Stratégies par taille et ordre initiative

| Taille | Séquence positive testable | Contre-scénario / repli |
| --- | --- | --- |
| **Solo** | Si classe possède un moyen légal de Nécronyx, d'atteindre mêlée Monolithe et de placer dA vers Bethel, passer S0 puis S1. | Faisabilité **non prouvée** : survie/tacle, absence de placeur et contrôle dA peuvent empêcher ; retour à plan défense plutôt que déclarer une victoire. |
| **Duo** | **P place/tank avant S** ; P éloigne dE, S élimine mE en mêlée ; dA peut agir si initiative/contrôleur l'autorisent. | **S avant P** : mE meurt avant placement et nouveau Dagon peut apparaître mal placé ; vérifier contrôle dA, prendre une action de repli. |
| **Trio** | **P** tank/Nécronyx, **S** tue Monolithe, **H** protège/soigne dA **avant son tour exposé** ; dA se place puis frappe B. | **H après Bethel/dA** : soin ne protège pas dA avant coups ennemis ; si dA meurt, nouveau cycle S0, réallouer ressources. |
| **Quatuor** | **P** tank, **S** mêlée, **H** survie, **C** contrôle/débuff, garde la distance de fin de tour ≤6PO, débuff du bouclier, surveille Nécronyx renouvelé, protège dA. | **C pousse dA hors contact** avant Souffle Chaud ou un acteur tue dA : perte de fenêtre ; S0 réinitialisé, pas de dégâts fictifs à Bethel. |

## Cas de tests discriminants — tous proposés et non exécutés

| Cas | Entrée et action | Sortie prévue | Règle |
| --- | --- | --- | --- |
| **P1** | S0 : Monolithe mE vivant, frappe mêlée S le tue. | Phase S1, Dagon allié nouvellement reconnu. | R01–R02 |
| **N1** | S0 : attaque à distance sur Monolithe uniquement. | Monolithe ne meurt pas par cette attaque, dA absent. | R01 |
| **P2** | S1 : dA frappe B en mêlée, mort B avec Nécronyx actif. | Mort B et sortie de phase boss, pas d'échec général. | R02/R04 |
| **N2** | S1 : même fatal, Nécronyx absent. | Combat perdu / wipe selon guide. | R04 |
| **N3** | S1 : dA meurt, B reste vivant. | **S0 restauré** avec nouveau mE et dE ENNEMIS, aucune conclusion de combat. | R03 version 3.7 |
| **N4** | Nécronyx sur B au tour 1 de P, puis fatal au tour 4 de P sans renouvellement. | État peut manquer au fatal ; invalidation plan / relancer Nécronyx. | R05 et initiative |
| **P3/N5** | Aquazomb ressuscite un ennemi, tenter dégât directement contre ressuscité (P3) ; modèle ancien attend 1 tour invulnérable (N5). | Absence d'invulnérabilité automatique spécifique résurrection en 3.7 ; autres immunités éventuelles séparées. | R06 |
| **N6** | Monolithe bouclier posé, vérifier après un tour et dégâts de piège/poison. | Bouclier **1 tour**, pas de réduction poison/piège par Monolithe en 3.7. | R07 |
| **N7** | Dagon ennemi sans poussée avant son tour ; variante avec poussée. | Variante poussée ajoute Inébranlable pour un tour et conditionne son attraction ; statut exact à contrôler. | R08 |
| **N8** | Permuter tour de P et S, puis dA et B ; identifier quel UID reçoit contrôle dA et qui agit d'abord. | Résultats d'initiative/contrôle non présumés ; enregistrer les différences observées. | R02, chrono |

## Coûts, dégâts, risques et passage au développeur

- **PA/PM, PO, dommages** : non calculés faute de table de sorts du client exact ; les guides suggèrent des chiffres mais un seul jet et une ancienne version ne prouvent pas une formule.
- **Bénéficiaire/acteur** : le Dagon allié est responsable de ses attaques, pas le tank ni le frappeur ; les soins/protections restent attribués à leur lanceur ; l'UID du nouveau Dagon conditionne sa légalité dans toutes compositions.
- **Risques** : mort dA = boucle, expiration de Nécronyx avant fatal = défaite, Bouclier mE un tour + tour d'invocation, Aquazomb réanime les mobs, éloignement et état Inébranlable.
- **Besoin du simulateur** : entités par UID/camp/état/alive, machine à phases, compteur de cycles, origine de résurrection, transferts de camp, owner du Dagon, ordre initiative global et par tour, dates d'expiration Nécronyx et bouclier, reçus morts et casts.
- **À prouver** : UID et équipe des entités après death(mE)/death(dA), déclenchement phase exacte, état Nécronyx au fatal, génération Aquazomb, contrôleur de dA et sa première fenêtre d'action ; aucun test autorisé/ réalisé ici.

**Reprise** : consigner dans un replay 3.7.13.17 chaque événement S0→S1→S0→S1→S2 et comparaisons de variantes, puis seulement attribuer une validation.
