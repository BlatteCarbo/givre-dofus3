# M10 — Passage au développeur : poussée/collision DOFUS 3.7

Recherche du 2026-11-04. Enjeu : savoir quand une poussée vaut davantage qu'une action de dégâts directs, sans confondre le sort Air avec le **supplément de collision**. La mission produit des spécifications/tests proposés, sans code ni exécution sur le jeu.

## Trois règles prioritaires et leur autorité
| ID | Règle candidate | Preuve | Limite |
| --- | --- | --- | --- |
| M10-R01 | Dégagement (Forgelance, Lance présente) : 4 PA, **ligne Air + pousse de 4 cases depuis le centre** ; rappeler la Lance si Désarmé. | [OpenDofusDB Dégagement](https://openddb.dev/spells/23743-degagement), fiche Dofus 3.7 [Grimoire](https://dofus-grimoire.fr/classes/forgelance/) | Texte de base de données de sort ; ordre exact et conditions client/serveur non exécutés. |
| M10-R02 | Distance de poussée effectivement parcourue et premier obstacle influent sur le gain de collision ; un mur est activement recherché pour optimiser ce gameplay. | [INFI V1, 02:17–02:25](https://www.youtube.com/watch?v=_YXtVLQnCU8&t=137s), [17:13–17:39](https://www.youtube.com/watch?v=_YXtVLQnCU8&t=1033s). | declare-video ; la **formule/arrondi de collision** n'est pas prouvée. Une action peut conserver du dommage Air en ligne libre. |
| M10-R03/R04/R06 | Le dommage de poussée doit exploiter la **résistance de poussée propre à la cible**, ses malus et la version du combat : Pluie d'Airain donne -40 pendant 2 tours (2 PA) ; plusieurs centaines de monstres ont changé en 3.7, certains restent à résistance extrême. | [OpenDofusDB Pluie d'Airain](https://openddb.dev/spells/24219-pluie-d-airain) ; [MAJ 3.7, Dofus pour les Noobs](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) ; [INFI V1 03:55–04:03](https://www.youtube.com/watch?v=_YXtVLQnCU8&t=235s) pour une cible PvP annoncée à 160 ré pou. | corrobore-source + declare-video ; valeur par monstre/état doit venir du snapshot courant, pas du titre vidéo ni d'une table présumée. |

## État et événements du simulateur
**Entrées versionnées :** version client/protocole ; `actorId` / équipe ; tour, PA disponibles, rang/variante/cooldown ; `spellId` résolu depuis données du client (aucun ID numérique inventé) ; présence, ID, position et état Armé/Désarmé de la Lance ; identité, cellule, PV, résistances Air fixe/% et **résistance poussée** de chaque cible ; buffs/débuffs avec source, expiration et ordre ; obstacles statiques, cellules libres, occupants, zones et état Indéplaçable.

**Transitions distinctes :** `CastRequested`, `CastAccepted|Refused|Unknown`, `DirectAirDamageResolved`, `PushRequested(direction,4)`, `PushMovementResolved(from,to,actualMoved)`, `PushBlocked(obstacleId,type,remainingDistance)`, `CollisionDamageResolved(targetId,sourceId,amount)`, `PushResistDebuffApplied(targetId,-40,expiry)`, `WeaponLanceRecallConfirmed`, `Death/Shield/StateChanged`. Ce sont des **types conceptuels**, non des noms du protocole garanti. Les effets et l'ordre doivent être réconciliés avec reçus serveur ; un ACK de commande seul ne prouve aucun dégât.

**Modèle de décision :** calculer séparément la légalité, le déplacement abouti, le risque de finir près/loin, les dommages Air, la collision et ses victimes. Conserver un champ `collisionDamage=unknown` jusqu'à formule validée ou à un intervalle estimé documenté. Ne pas appliquer le taux de résistance Air aux dommages de poussée ; ne pas additionner une ligne de dommages Air de Pluie à celle de Dégagement sans l'afficher séparément.

## Mini-séquence reproductible
Lance (L=(0,0)), cible (E=(1,0)), direction de poussée (+x) ; Forgelance `R` en une case de lancement légale distincte. Dégagement : 4 PA, 4 cases de recul prévues. Trois instances isolées du même état :
- **A mur proche :** obstacle fixe en (2,0), distance libre 0. Proposition `moved=0`, `unfulfilled=4` ; dommages de collision à mesurer.
- **B mur médian :** obstacle en (4,0), cases libres (2,0) et (3,0). Proposition `moved=2`, `unfulfilled=2` ; collision à mesurer indépendamment du Air.
- **C ligne libre :** cases (2,0)…(5,0) libres. Proposition `moved=4`, `unfulfilled=0` ; aucun événement de collision sur cet obstacle ; la ligne de dommages Air subsiste.

La géométrie est **relative et proposée** ; la traversabilité réelle des cases de DOFUS et la direction depuis la Lance restent à vérifier. Conserver même rang, niveau, caractéristiques, jet/coup critique, résistances et états dans A/B/C. Les trois instances sont indépendantes ; ne pas jouer trois fois Dégagement dans le même tour puisqu'il est limité à une fois.

## Tests discriminants proposés — non exécutés
| Cas | Entrée / action | Résultat observable | Ce que le cas distingue |
| --- | --- | --- | --- |
| P1/P2/N1 | Dégagement 4 PA sur Lance, variante A/B/C | Tracer cases parcourues, obstacle atteint, ligne Air, ligne collision, victime et retrait éventuel de PV | Distance effectivement parcourue, collision mur adjacent / partielle / absente ; éviter de traiter tout dommage du sort comme une collision. |
| P3 | Deux A identiques, cible avec ré pou 0 puis 160 ; troisième A avec Pluie d'Airain -40 confirmée puis Dégagement | Isoler uniquement les événements de Dégagement et leur montant, les effets de Pluie ayant été comptés séparément | Sensibilité à résistance poussée vs Air, différence entre résistance de base et état temporaire. |
| N2 | A mais état Indéplaçable sur E, même résistance | Tracer l'éventuel refus de déplacement et tous dommages effectivement appliqués | Distinguer immunité au mouvement et résistance de poussée : leur effet sur collision reste inconnu. |
| N3 | A mais remplace mur par une entité alliée/ennemie vivante | Tracer collisions sur E et sur l'autre entité, causalité, ordre et dégâts éventuels | Mur fixe vs occupant dynamique, éventuels dégâts collatéraux non présumés. |
| N4 | Charge Héroïque (3 PA) sans contact final | Pas de recul conditionnel si l'absence de contact est confirmée ; direct et déplacement de R potentiels | Un déplacement de lanceur n'implique pas une poussée de cible. |

## Limites bloquantes et validation attendue
- **Calcul chiffré :** ancienne formule [Raceko](https://raceko.wifeo.com/formulaire.php) non versionnée ; vérifier 3.7 en tests avec niveau, dommages poussée, résistance et nombre de cases non parcourues, arrondis, min/max, boucliers et cas dégâts négatifs. Ne pas coder à partir d'un jet annoncé (~400 sur 2 cases [V1 22:31–22:37](https://www.youtube.com/watch?v=_YXtVLQnCU8&t=1351s)).
- **Orientation et collisions :** origine de la poussée depuis Lance ou lanceur selon le sort, bord, murs, entités, obstacles détruits, états Indéplaçable et ordre d'effets.
- **Compatibilité 3.7 :** verrouiller le rang du sort, les variations d'équipement, l'état Lance, les données monsterId exactes ; [MAJ 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) atteste la tendance PvM, pas les valeurs d'une cible.
- **Preuve d'exécution :** fixtures hors jeu et replay de combat expurgé avec requête, acceptation, mouvements, PV, effets et horodatage ; vérifier ensuite en combat réel autorisé avant de marquer `valide-live`. Aucun test exécuté dans M10.

## Critère tactique futur
Prédire la valeur d'une attaque de poussée seulement si le chemin et la résistance sont connus et si la part collision est validée. Comparer `(dégâts directs + collision attendue − exposition/placement)` **par PA** aux options de dégâts directs accessibles. Une cible 3.7 vulnérable à la poussée avec mur proche peut avantager la première ; une cible à haute résistance, sans obstacle ou Indéplaçable peut inverser le classement. Sans nombre testé, la mission ne fournit **aucun seuil de décision** ni formule prétendument exacte.

Référence détaillée : [FICHE_POUSSEE.md](FICHE_POUSSEE.md) et liste des preuves dans [RAPPORT.md](RAPPORT.md). Aucun fichier `SYNTHESE.md` modifié.

