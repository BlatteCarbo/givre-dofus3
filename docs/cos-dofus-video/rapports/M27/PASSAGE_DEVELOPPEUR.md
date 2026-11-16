# Passage au développeur — M27 Torkélonia, DOFUS 3.7

**Spécification de recherche seulement.** Fichiers liés : [RAPPORT.md](./RAPPORT.md) et [FICHE_TORKELONIA.md](./FICHE_TORKELONIA.md). Dernier client cité dans le kit : 3.7.13.17 **non testé**.

## Trois règles prioritaires avec provenance

**P1 — Prendre la table de phase de la version 3.7.** Au **Premier quartier, tour 3**, le bonus devient **500 points de bouclier** (ancienne +10 % résistance) ; au **Dernier quartier, tour 7**, il devient **50 dommages** (ancien 500 PB). Source : [Dofus pour les Noobs, notes 3.7, 04/10/2026, section « Torkélonia »](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). Le [guide Dofuserie révisé en octobre 2026](https://www.dofuserie.com/donjons/sanctuaire-de-torkelonia/) montre encore l'ancienne répartition et doit être marqué **obsolete-pour-cible** sur ces effets. Un test sur la cible ne doit pas interpréter 500 PB comme une résistance de 10 %.

**P2 — Ne pas restaurer l'ancienne invulnérabilité après un kill en Nouvelle/Pleine lune.** La [MAJ 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) supprime l'invulnérabilité **d'un tour supplémentaire** des survivants après une mort en **Nouvelle lune (tour 1)** ou **Pleine lune (tour 5)**. La [fiche du Sanctuaire mise à jour](https://www.dofuspourlesnoobs.com/sanctuaire-de-torkelonia.html) conserve la **reconstitution/soin** des survivants ; elle rappelle aussi le verrou fondamental de la **Lunorbe**, levé par une première ligne de dégâts du personnage **placé sur le glyphe bleu**. Le délock ne vaut que pour cet acteur, jusqu'à sa sortie du glyphe ou la fin de son tour. Une stratégie attendant toujours huit tours après un kill n'est donc **plus une nécessité de règles**, même si la reconstitution peut rendre une nouvelle élimination coûteuse.

**P3 — Enregistrer la phase, la Lunorbe et le coup final par identité.** Huit phases se succèdent et peuvent être accélérées par une ligne de dommages sur la Lunorbe ; le bonus de phase d'un monstre tué peut être transmis durablement aux survivants. La Parade lunaire peut infliger un retrait permanent de **1 PA ou 1 PM** lors du tour complet du cadran. Un **personnage** finissant Torkélonia hors Nouvelle/Pleine lune meurt ; une **invocation finisseuse** constitue une exception décrite. Après la mort de Torkélonia, Lunorbe et glyphe disparaissent et le verrou des autres monstres est supprimé. Source : [Sanctuaire de Torkélonia — guide détaillé](https://www.dofuspourlesnoobs.com/sanctuaire-de-torkelonia.html). Le moment exact et le résultat aléatoire de Parade sont **unknown** et nécessitent un événement serveur.

## Contrat des données

**Entrées :** identifiants stables de Lunorbe, boss, alliés, invocations et monstres ; round global, acteur et identifiant du tour actif, ordre d'initiative, phase index 0..7 (ou Unknown), événement de changement de phase, cellules effectives du glyphe actif, sorts/PA/PO/LoS, cible déverrouillée pour chaque acteur, état de chaque monstre (PV, invuln Lunorbe, bouclier, résistances, dommages, effets et durées, bonus permanents), auteur du kill et son type personnage/invocation, événements serveur acceptés/refusés, révision de l'état.

**Règles à isoler dans le simulateur :**
- **MoonPhaseRule** : phase 0 Nouvelle lune ; 1 Premier croissant x0,8 dégâts subis ; 2 Premier quartier +500 PB ; 3 Gibbeuse montante +2 PM ; 4 Pleine lune renvoi 200 ; 5 Gibbeuse descendante +2 PA ; 6 Dernier quartier +50 dommages ; 7 Dernier croissant +20 % dommages finaux. En Nouvelle lune, le guide donne renvoi de 20 % des dégâts dans un cercle de 2 cases autour de l'attaquant. L'arrondi et les exclusions de cibles sont à calibrer.
- **GlyphUnlockRule(actorTurnId, targetId)** : lorsque l'acteur sur glyphe inflige sa première ligne de dégâts, retirer l'invulnérabilité Lunorbe pour cette cible et cet acteur **sans dégâts normaux pour cette ligne** ; annuler l'ouverture si l'acteur quitte le glyphe ou termine son tour. Ne pas partager l'ouverture avec un autre membre.
- **KillEventRule(phase, sourceEntityType, targetId)** : transmettre les buffs de phase aux survivants ; en phase 0/4 appliquer reconstitution, **sans** ancien statut « invulnérable 1 tour » ; si le boss meurt, appliquer conséquence du finisseur et suppression Lunorbe/glyphe. Consigner les effets réellement reçus.
- **LunorbeClock** : un événement de ligne de dégâts sur Lunorbe peut faire avancer le cadran **sans attendre le prochain round** ; il peut cumuler des bonus de phase. Ne jamais dériver la phase du seul compteur de rounds lorsqu'une accélération a eu lieu. Gestion de Parade : choisir PA ou PM seulement suivant l'événement observé.
- **StateRevision/Unknown** : un simple envoi ou ACK ne confirme ni délock, ni nouvelle phase, ni mort, ni shield. Sur Refused/Timeout/Unknown conserver la dernière preuve et recalculer après réception des effets réels.

**Sortie proposée :** combatRevision ; currentPhase (ou Unknown) ; projectedTargetLock par paire (acteur, cible, tour) ; projectedGlyphPositions ; buffs by target and origin ; combatSafeCast (possible/impossible/unknown) ; reason ; projectedFinisherPenalty ; et provenance.

## Contre-exemples et tests proposés (zéro test exécuté)

| ID | Entrée / ordre | Observable attendu |
| --- | --- | --- |
| P01 — bouclier T3 | Phase Premier quartier, personnage au début de son tour, aucune autre protection | Un effet **500 PB**, pas +10 % résistance (3.7) |
| N01 — dommages T7 | Phase Dernier quartier, aucune ancienne protection | Un effet **+50 dommages**, pas +500 PB de cycle |
| N02 — kill Nouvelle lune | X meurt en P0, Y vivant, A encore sur glyphe et pouvant agir | Reconstitution Y, **aucun verrou temporaire post-mort d'un tour** ; délock ordinaire Y à réappliquer |
| N03 — kill Pleine lune | X meurt en P4, Y vivant | Même suppression du verrou post-mort, malgré renvoi de 200 |
| N04 — sortie glyphe | A ouvre X depuis glyphe, quitte glyphe avant frappe suivante | X à nouveau protégé |
| N05 — changement d'acteur | A délock X puis finit son tour ; B tente frapper X sans glyph | B doit effectuer son propre délock |
| P02 — bonus hérité | X meurt en P2, Y survit, puis P3 arrive | Bonus permanent du kill (PB P2) distinct du bonus cyclique courant +2 PM |
| N06 — kill boss | C personnage tue Torkélonia en phase P2 ; alternative avec invocation S | C meurt dans cas personnage selon guide ; exception S selon guide ; Lunorbe/glyphe disparaissent |
| N07 — accélération | Même round avant/après dommage sur Lunorbe | Changement de phase basé sur événement, sans déduction figée du round |

**Solo :** un acteur doit lui-même délocker avant son attaque ; enregistrer invocateur et invocation finisseuse séparément. **Duo :** placeur A joue avant attaquant B ; inverser initiative pour prouver l'échec d'un placement dépendant du futur. **Trio :** un kill en P2 donne un bouclier permanent aux survivants ; cet effet ne peut être traité comme résistance. **Quatuor :** un allié accélère Lunorbe avant le DPT ; la phase préparée devient périmée et les ressources de chacun restent indépendantes.

## Limites, sources secondaires et reprise

Le seul tutoriel 2026 vérifié par MCP est **Deplai**, [« FARM 100 DONJONS TORKELONIA ... TEAM SADI 3.6 »](https://www.youtube.com/watch?v=JXC57dZBYy4) publié **17/09/2026**, **6 min**, chapitre boss [03:38](https://www.youtube.com/watch?v=JXC57dZBYy4&t=218s) tiré de la **description**. La transcription horaire demandée en fr a échoué par blocage de l'IP YouTube ; **aucune parole ni pixel du plateau n'est affirmé**. Les chiffres 3.7 viennent des textes versionnés, non de cette vidéo 3.6.

**Validation indispensable sur client 3.7.13.17** : logs T3/T7 sans autre buff, deux kills possibles dans un même round Nouvelle lune, effet « Insoignable » sur la reconstitution, finisseur en phase non sûre, chemin et calendrier du glyphe, timing d'une ligne sur Lunorbe, dégâts et permanence de Parade, éventuel cumul de bonus hérités. Ne pas présumer d'une formule de dégâts, d'un numéro de cellule ou d'un type d'invocation inconnu. **Statuts probatoires : corrobore-source, hypothese, contradictoire, obsolete-pour-cible. Aucun observe-video, declare-video, valide-test ou valide-live.**
