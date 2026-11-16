# Fiche M16 — Entrave PM conditionnelle et survie (Enutrof Eau, DOFUS 3.7)

## Identité et sources
- **ID stable :** M16-ENU-RETRAIT-ESQUIVE-v1 ; branche M16-ENU-SAC-SURVIE.
- **Jeu :** DOFUS 3 ; références mécaniques [Grimoire 3.7.12](https://dofus-grimoire.fr/classes/enutrof/) et [Back from AFK diff 3.6→3.7](https://backfromafk.com/classes/enutrof/depuis-3-6/). Cible projet mentionnée dans kit : 3.7.13.17, compatibilité non exécutée.
- **Tutoriel V16-A :** [Yusonara — Guide de Classe](https://www.youtube.com/watch?v=rKGGoHvxevI), 2025-05-29, 7 min ; info/langues MCP lues, FR auto annoncé ; **aucun segment de transcription obtenu** (`get_timed_transcript` bloqué IP). Panneau « Afficher la transcription » chargé en navigateur mais demeure `loading`. Sa description porte sur rôle d'équipe et éléments, pas une formule de retrait.
- **Tutoriel V16-B historique :** [GOATESQUE — Tuto DOFUS 3](https://www.youtube.com/watch?v=GGbqdb_Ynv0), 2024-11-26, 10 min ; chapitres de **description** 03:19 Air/04:35 Eau/05:55 Terre/07:18 Feu, **pas passages parlés examinés** (même blocage IP MCP).
- **Analyse** : 2026-11-04, recherche documentaire ; aucune image de plateau, reçu de combat ou version client confirmée.

## Règles atomiques
| ID | Énoncé précis | Statut/preuve exacte | Limite |
| --- | --- | --- | --- |
| M16-R01 | Maladresse 1 PA, tente de retirer jusqu'à 2 PM sur ennemi au rang haut, PO jusqu'à 12. | `corrobore-source`, [Grimoire, Maladresse](https://dofus-grimoire.fr/classes/enutrof/) | Retrait effectif non garanti ; limitation par cible/tour à confirmer |
| M16-R02 | Pelle Aurifère 4 PA, attaque Eau et tente -3 PM au rang haut. | `corrobore-source`, [Grimoire, Pelle Aurifère](https://dofus-grimoire.fr/classes/enutrof/) | Jets, obstacles et esquive côté serveur |
| M16-R03 | Obsolescence 3 PA, dommage Eau + -20 Esquive PM à la cible. | `corrobore-source`, [Grimoire, Obsolescence](https://dofus-grimoire.fr/classes/enutrof/) | Application effective/expiration, calcul de l'esquive |
| M16-R04 | Orpaillage 4 PA, état à 4 charges de réaction à tentative de retrait PM (relance 3 tours selon fiche). | `corrobore-source` pour l'exemple, [Grimoire, tour Eau](https://dofus-grimoire.fr/classes/enutrof/) | La charge se déclenche-t-elle sur 0 PM retiré ? **Unknown** |
| M16-R05 | Sac Animé 2 PA, intercepte certains dommages destinés aux alliés à proximité, invocation entité propre. | `corrobore-source`, [Grimoire, objets animés](https://dofus-grimoire.fr/classes/enutrof/) | Zone, dégâts interceptés, team/owner, conditions de disparition |
| M16-R06 | Ruée vers l'Or cible un allié ; condition de Puissance/soin déclenchée à la **fin du tour du bénéficiaire** selon 3.7. | `corrobore-source`, [diff 3.7](https://backfromafk.com/classes/enutrof/depuis-3-6/) | Contact exact et phase du bonus suivant |
| M16-R07 | Pelle de Fortune inclut +10 Retrait PM pendant 2 tours en 3.7. | `corrobore-source`, [diff 3.7](https://backfromafk.com/classes/enutrof/depuis-3-6/) | Avantage exact en probabilité impossible à calculer sans formule contrôlée |
| M16-R08 | Calcul opposant retrait à esquive et tenant compte de PM déjà retirés décrit historiquement. | `hypothese` pour 3.7, [guide retrait/esquive non versionné](https://www.dofuspourlesnoobs.com/retrait-et-esquive.html) | Formule et taux hors périmètre prouvé |

## État initial fixture (géométrie relative indicative)
- **R** = Enutrof Eau joueur A, 12 PA, 6 PM hypothétiques ; **X** = monstre hostile B, 6 PM initialement disponibles ; **D** = DPS allié menacé. X est à distance permettant une attaque de proximité si ses PM suffisent, sans obstacle majeur dans la variante positive. Position relative possible R=(0,0), X=(4,0), D=(7,0) ; le moteur doit confirmer que les cellules ont les portées/LoS des sorts et la distance de menace voulues.
- **Ordre fixture P :** R→D→X, puis un éventuel deuxième ennemi Y. **Ordre fixture N-init :** X→D→R. On distingue tour courant, prochaine action adverse et phases d'expiration ; le tir D n'est pas automatiquement dans l'aire de portée si X n'a pas bougé.
- **Statistiques requises :** niveau/rang réel de R, `retraitPM` R, `esquivePM` X, PM max, PM courant, PM déjà perdus, effets +PM, états indéplaçable / invulnérable / impossible de subir retrait si applicable, version du serveur, résistance Eau, PV, cooldown et cases.
- **Deck :** Orpaillage, Obsolescence (variante d'Abattement), Pelle Aurifère, Maladresse, pas de variantes simultanées incompatibles. Les coûts/ranges suivent la fiche 3.7.12 et demandent une validation au moment du cast.
- **Protection :** pas de Sac Animé au début du tour central. Un autre fixture le posera et déduira les 2 PA réels.

## Séquence d'un tour 12 PA et transitions typées

| Ordre | Acteur, PA restants | Action/cible | Préconditions | Effet annoncé / état | Confirmation serveur requise |
| --- | --- | --- | --- | --- | --- |
| 1 | R, 12→8 | **Orpaillage(X)** | X vivant, à 5 PO max selon fiche, LoS légale, relance prête | dégâts Eau + état Orpaillage (jusqu'à 4 déclenchements futurs) | damage de base après résistances, état/charges `triggerCount` |
| 2 | R, 8→5 | **Obsolescence(X)** | X toujours ciblable à 7 PO max | dégâts Eau + **-20 Esquive PM** | malus effectivement appliqué, valeur précédente/nouvelle, expiration |
| 3 | R, 5→1 | **Pelle Aurifère(X)** | X vivante et ciblable ≤7 PO, sort disponible | dégâts Eau ; tentative jusqu'à **-3 PM** ; événement éventuel Orpaillage | **actualPmRemoved** venant du serveur, état réaction avec source `Pelle Aurifère` |
| 4 | R, 1→0 | **Maladresse(X)** | X vivante, cible autorisée et sort disponible | tentative jusqu'à **-2 PM** supplémentaires ; autre éventuel événement Orpaillage | Retrait confirmé, PM courants X, charges restantes ; ne pas écrire « X a perdu 5 » par défaut |
| 5 | Système | Réévaluation menace | R fini, prochain X encore capable de déplacement/attaque | Réévaluer trajet de X→D, portée du sort, teleport/placement, turn-order | décision défensive ou repli si X garde assez de PM/PO |

**Maximum théorique en termes d'annonces de sort :** `-3 + -2 = -5 PM tentés`. **Résultat réel :** ∈ une plage dépendant de la résistance/esquive/états et des PM encore accessibles ; ne donner ni probabilité ni `PMfin=1` sans ligne d'effet reçue. La durée du retrait en fin de tour et la réinitialisation au tour X sont à confirmer par événement. Ne pas supposer qu'un déclencheur Orpaillage exige un retrait réussi : les textes disponibles ne tranchent pas proprement « tentative » vs « PM effectivement perdus ».

## Branche survie si la menace est forte
Dans un *autre état initial indépendant*, choisir une dépense de **11 PA** : Orpaillage(X) 4 + Pelle Aurifère(X) 4 + **Sac Animé** sur case libre 2 + Maladresse(X) 1. Les 1 PA restants peuvent être conservés ; Obsolescence et son -20 esquive ne sont **pas** inclus. Le Sac est une entité de l'équipe R qui peut intercepter certains dommages d'un allié dans sa zone, si la règle de portée/protection et sa survie sont confirmées. Il ne garantit ni l'absence d'attaques à distance ni l'interception de tous les dégâts. Alternative si les 11 PA ne sont pas disponibles : à recalculer depuis le budget réel, sans négatif artificiel.

## Adaptations solo/duo/trio/quatuor
| Composition | Objectif et ordre proposé | Contrepartie à calculer |
| --- | --- | --- |
| Solo R | R→X, ralentir l'approche, sauver R avec Sac ou recul si possible | Si X esquive ou frappe à distance, Sac ne garantit pas la survie ; attaque directe peut être préférable |
| Duo R+Crâ | R→Crâ→X : tenir ennemi hors portée de mêlée pendant tir Crâ | Une cible bloquée peut être derrière obstacle de LoS du Crâ ; pas de bonus universel |
| Duo R+Iop/Sacrieur | R→frappeur→X : entrave de X puis Ruée vers l'Or si besoin d'approcher | Retirer des PM à X peut éloigner le corps-à-corps nécessaire au frappeur |
| Duo R+Eniripsa | R→Eni→X ou Eni→R→X : entrave, soins ciblés si nécessaire | Inverse avec X en premier : les soins doivent survivre au danger initial |
| Trio R+placeur+DPS | R→placeur→DPS→X : entrave/placement puis dégâts | Si placeur passe après DPS, plan offensif simultané impossible |
| Quatuor R+tank+placeur+DPS | R→placeur→DPS→tank→X ; choix Sac ou entrave selon menace | Tank veut parfois retenir X au contact ; choisir un état final compatible avec son tacle et son tour |

## Scénarios discriminants (tous proposés, non exécutés)

| ID | Entrée différentielle | Actions | Résultat à mesurer / principe |
| --- | --- | --- | --- |
| P16-01 | X 6 PM, esquive basse, R retrait PM connu, R→X | 4+3+4+1 PA | Deux tentatives PM distinctes, état -20 appliqué, PM effectivement perdus, charges Orpaillage et menace recalculée ; pas de nombre prédit |
| N16-01 (esquive) | Même X 6 PM mais très haute esquive, mêmes LoS/PA | Même séquence | 0 ou retrait partiel si serveur l'indique ; **ne jamais déduire -5 d'une action acceptée**, nouvelle trajectoire X→D potentiellement encore possible |
| N16-02 (initiative) | X→R, X a déjà atteint et frappé D | Même séquence R après X | Les dégâts déjà reçus par D restent réels ; protéger prochain cycle sans attribuer effet rétroactif au retrait |
| N16-03 (cible) | X1 devant DPS, X2 plus éloigné ; Orpaillage(X2), Obsolescence(X2), Maladresse(X1) | Actions sur cibles différentes | Charges et malus **attachés à X2** ; retrait de X1 dépend de sa propre esquive ; pas de transmission des malus X2 |
| N16-04 (protection) | État combat avec menace sur D ; Sac placé trop loin ou mort avant X | Sac puis attente | Aucun effet d'interception présumé hors zone/après mort ; R réévalue protection du D |
| N16-05 (condition fin de tour) | Ruée vers l'Or sur frappeur A ; A finit loin de tout ennemi, autre essai A finit au contact | Ruée, tour A | Bonus conditionnel de tour suivant seulement si contact final réel de A ; ne pas inférer de la case à l'instant du lancer |

## Modèle minimal de données et inconnues
`actorId`, `targetId`, `turnEpoch`, `teamId`, `spellId/rank/variant`, `APcost`, `MPcurrent/MPmax`, `MPalreadyRemoved`, `MPdodge`, `MPremoval`, `buffsSource+expires`, `castAccepted`, `effectResolved`, `attemptedRemoval`, `actualRemoval`, `orpaillageCharges`, `summonId`, `interceptionSource`, `damageApplied`, `threatMap`. Aucun montant de probabilité, formule d'esquive ou effet de début de tour non sourcé n'est à implémenter en dur. Fiche complémentaire : [PASSAGE_DEVELOPPEUR.md](PASSAGE_DEVELOPPEUR.md).

La source mécanique est communautaire mais versionnée 3.7.12 ; les vidéos historiques ne prouvent **aucun** état 3.7.13.17. Avant `valide-test` et `valide-live`, obtenir fixtures log serveur et client exact. Pas de test exécuté.

