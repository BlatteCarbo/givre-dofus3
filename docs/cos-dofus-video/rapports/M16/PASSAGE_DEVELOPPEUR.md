# M16 — Passage au développeur GivreClone, retrait esquivable et survie

**Règle de décision à ne pas confondre :** `tentative de -5 PM` ≠ `-5 PM effectivement appliqués`. La valeur opérationnelle d'Enutrof dépend de l'esquive/état du monstre et de l'ordre d'initiative.

## Trois règles prioritaires

| ID | Décision de moteur | Justification versionnée |
| --- | --- | --- |
| **M16-R01/R02/R03** | Maladresse 1 PA tente -2 PM ; Pelle Aurifère 4 PA tente -3 PM ; Obsolescence 3 PA baisse l'Esquive PM de 20 **avant** les retraits ; les résultats PM doivent venir des effets reçus. | [Grimoire Enutrof 3.7.12](https://dofus-grimoire.fr/classes/enutrof/), `corrobore-source`. Calcul probabiliste non démontré. |
| **M16-R04/R05** | Orpaillage 4 PA pose un état à déclenchements bornés sur les tentatives de retrait ; Sac Animé 2 PA crée une entité protectrice distincte, alternative au tour d'entrave complète. | [Grimoire Enutrof, Orpaillage et Sac](https://dofus-grimoire.fr/classes/enutrof/), `corrobore-source` ; timing/proc en cas d'esquive inconnus. |
| **M16-R06/R07** | En version 3.7, Ruée vers l'Or évalue le **contact à fin du tour du bénéficiaire** pour la suite ; Pelle de Fortune donne +10 Retrait PM pour 2 tours, parmi autres bonus. | [Back from AFK 3.6→3.7](https://backfromafk.com/classes/enutrof/depuis-3-6/), `corrobore-source`. Initialisation exacte et phase d'expiration à rejouer. |

## Entrées, transitions et événements
- **Identité et ordre :** actorId R, targetId X, ally D, team IDs, turnEpoch (R→X vs X→R), position actuelle et historique de déplacement. Un gain PM du frappeur via Ruée ne change pas le budget de l'Enutrof.
- **Statistiques dynamiques :** PM max/courant **réel** avant/après chaque sort, esquive PM de X, retrait PM de R, états modifiant ces chiffres, duration/owner des debuffs, présence de cible à la résolution. Ne pas calculer une chance à partir d'une formule ancienne avant confrontation version 3.7.
- **État de sort :** 12 PA dans fixture, deck, variante, rang, cooldown, PO/LoS, cellules occupées et obstacles, buffs Pelle de Fortune, état Orpaillage et charges par `targetId`.
- **Survie :** Sac Animé avec `summonId`, owner, team, case, PV, zone d'interception, état actif ; victime initiale des dégâts et victime finale après transfert ; protection d'un allié blessé, sans déduire du simple spawn.
- **Événements conceptuels** (pas IDs protocole attestés) : `CastRequested`, `CastAccepted/Refused/Unknown`, `DodgeDebuffApplied`, `MPRemovalAttempted`, `MPRemovalResolved`, `OrpaillageChargeTriggered`, `DamageApplied`, `SummonSpawned/Died`, `DamageIntercepted`, `TurnEnded`. ACK d'un cast n'est ni une réussite de retrait ni un proc. Sans reçu, garder Unknown et recalculer la menace.

## Tour positif minimal, puis repli
Fixture : R Enutrof Eau, X menace à 6 PM, R→D→X, 12 PA, deck 3.7.12 disponible, X vivant, accessible à Orpaillage ≤5 PO et à tous les sorts suivants ; état et cartes à vérifier. 
1. `Orpaillage(X)` : 4 PA, état/charges à confirmer.
2. `Obsolescence(X)` : 3 PA, dommage Eau distinct et -20 Esquive PM si effet confirmé.
3. `Pelle Aurifère(X)` : 4 PA, tentative -3 PM, lire **N3 retirés**.
4. `Maladresse(X)` : 1 PA, tentative -2 PM, lire **N2 retirés**.
5. Le résultat consommé par le planificateur est `MPcurrent(X)` issu des messages et ses moyens de menace ; `N3+N2` peut être inférieur à 5. Ne jamais déclarer X immobile uniquement parce que les deux sorts ont été acceptés.

**Repli protection :** nouvel état indépendant R=12 PA : Orpaillage 4 + Pelle Aurifère 4 + Sac Animé 2 + Maladresse 1 = **11 PA** (un PA libre). Mesurer l'interception et les dommages subis avant de choisir. Les sorts de placement/soin des alliés peuvent intervenir seulement sur leurs propres tours.

## Deux contre-tests obligatoires et extensions
| Test | Préconditions | Résultat qui discrimine |
| --- | --- | --- |
| **N16-01 esquive** | X haute esquive PM ; 6 PM ; actions identiques au positif | Le serveur peut retirer moins que 5 ou 0 ; le plan de survie doit conserver la capacité de déplacement restante. Un combat de référence avec faible esquive sert de témoin. |
| **N16-02 initiative** | X frappe D **avant** R | Retrait par R ne restaure pas rétroactivement PV perdus de D. Préparer prochain tour en fonction des PA/PM réels et des soins disponibles. |
| **N16-03 cible** | Orpaillage et Obsolescence sur X2, Maladresse sur X1 | L'effet -20 esquive et les charges restent sur X2. Retrait sur X1 utilise l'esquive X1, sans transfert de buffs/debuffs de X2. |
| **N16-04 mort du Sac** | Sac interceptant, puis disparaît avant prochain coup | Aucune interception nouvelle par entité morte ; risque recalculé. |
| **N16-05 fin de tour** | Ruée sur allié A ; A termine hors du contact annoncé | Ne pas appliquer conditionnement Puissance/soin du tour suivant si contact final non observé. |

## Solo, duo, trio, quatuor
- Solo R : retirer PM et vérifier si ennemi encore à portée d'attaque/téléportation ; un plan Sac coûte 2 PA et peut réduire le DPS immédiat.
- Duo R+Crâ : entrave avant déplacement ennemi, puis frappe du Crâ si LoS ; duo R+Eniripsa : initiative de l'Eni et soins ciblés, ne pas imaginer un soin instantané par l'Enu.
- Trio R+placeur+DPS : ordonner l'action du placeur **avant** le tour de dégâts pour rendre le placement exploitable ; une cible sans PM peut rester trop loin d'un frappeur de mêlée.
- Quatuor R+tank+placeur+DPS : comparer entrave qui maintient loin, tacle qui maintient au contact, Ruée qui rapproche le tank et Sac qui protège ; chaque acteur avec budget PA/PM propre et son tour d'initiative.

## Dépendances bloquantes
- **Version :** fiches communautaires [Grimoire 3.7.12](https://dofus-grimoire.fr/classes/enutrof/) et diff [3.7](https://backfromafk.com/classes/enutrof/depuis-3-6/), pas de preuve sur client 3.7.13.17.
- **Transcriptions :** [Yusonara 29 mai 2025](https://www.youtube.com/watch?v=rKGGoHvxevI) et [Goatesque 26 novembre 2024](https://www.youtube.com/watch?v=GGbqdb_Ynv0) ont titre/auteur/date/description/langue vérifiés par MCP. `get_timed_transcript(lang=fr)` a échoué avec **IP YouTube bloquée**, `next_cursor` inexistant ; navigateur a affiché `Transcription: loading` sans segment. Aucun contenu oral horodaté inventé.
- **Esquive :** la formule historique de [Dofus pour les Noobs](https://www.dofuspourlesnoobs.com/retrait-et-esquive.html) n'est pas certifiée 3.7 ; pas de probabilité arbitraire.
- **Vérification :** fixtures serveur expurgées avec événements de tentative/résultat PM, charges Orpaillage, source du malus, Sac et Ruée ; les tests sont **proposés non exécutés**, sans `valide-test` ni `valide-live`. Aucun code, Git, jeu ou SYNTHESE.md touché.

