# Fiche M15 — Un tour Eniripsa : trois bénéficiaires et une menace

## Identité
- ID stable : **M15-ENI-ENCOURAGEMENT-TARGET-TIMING-v1** ; complément : **M15-ENI-RECONSTITUTION-INSOIGNABLE**.
- Jeu : DOFUS 3, cible projet 3.7.13.17 mentionnée dans le kit, référence mécanique [Dofus Grimoire 3.7.12](https://dofus-grimoire.fr/classes/eniripsa/) ; [diff Eniripsa vers 3.7](https://www.backfromafk.com/classes/eniripsa/depuis-3-4/). Compatibilité exacte à valider.
- Vidéo réelle : [XAWPIYA, 8 août 2026](https://www.youtube.com/watch?v=SbrNWSScKPc), vidéo DOFUS Unity 8 min (lecteur 8:30) ; description compare duos Féca/Iop/Sadida. Métadonnées et langues MCP fonctionnelles, **transcription horodatée impossible** : erreur de blocage IP ; aucun passage oral ni tableau vidéo attesté. [SAPEUH 28 décembre 2024](https://www.youtube.com/watch?v=ER2LxfeKbHs), contrôle secondaire de métadonnées uniquement.
- Relevé documentaire du 4 novembre 2026. Statuts : `corrobore-source` pour les textes 3.7.12 ; `hypothese` pour les déclenchements exacts serveur ; `avis-video-description` pour choix de compo (ce n'est **pas** un statut de preuve mécanique).

## Règles atomiques

| ID | Affirmation circonscrite | Provenance / statut | Variables à ne pas figer |
| --- | --- | --- | --- |
| M15-R01 | Mot Stimulant 2 PA, +2 PA sur allié Encouragé, rang haut, 1/tour. | [Grimoire](https://dofus-grimoire.fr/classes/eniripsa/) — `corrobore-source` | Phase exacte du bonus (tour cible), portée LoS, source/durée/cumul |
| M15-R02 | Mot Vivifiant 2 PA, +2 PM et +30 Fuite sur allié, Encouragé ; un nouveau Mot sur même allié remplace l'ancien. | [Grimoire, les 8 Mots](https://dofus-grimoire.fr/classes/eniripsa/) — `corrobore-source` | Ciblage, règles de cumul, famille/ID de l'Encouragement |
| M15-R03 | Mot de Jouvence 2 PA, soin % PV max 10 % au rang haut ; -1 durée des effets et soin différé selon fiche. | [Grimoire, Mot de Jouvence](https://dofus-grimoire.fr/classes/eniripsa/) — `corrobore-source` | Ordre soin immédiat/réduction d'effets/soin au début de tour |
| M15-R04 | Mot Accablant 2 PA, cible hostile, enlève jusqu'à 3 PM et 20 esquive PM au rang haut, état Moqué. | [Grimoire, Mot Accablant](https://dofus-grimoire.fr/classes/eniripsa/) — `corrobore-source` | Part esquivable, ordre retrait/esquive, durée |
| M15-R05 | Mot d'Envol 3 PA échange avec allié ; Mot de Frayeur 1 PA repousse sous conditions. | [Grimoire, tour « Distribuer les quatre Mots »](https://dofus-grimoire.fr/classes/eniripsa/) — `corrobore-source` | Légalité et collision avant/après mouvement |
| M15-R06 | Reconstitution 5 PA et relance 6 tours au rang max : soin total et état Insoignable. | [Grimoire, Mot de Reconstitution](https://dofus-grimoire.fr/classes/eniripsa/) — `corrobore-source` | Durée exacte et dispell de l'état, coïncidence morts / Pv max |
| M15-R07 | Lapino/Mot d'Amitié : invocation 3 PA, contrôle, effets à mort version 3.7 ; un Mot sur Lapino peut appliquer la zone cercle 2 autour de lui selon fiche. | [Grimoire](https://dofus-grimoire.fr/classes/eniripsa/) ; [Back from AFK](https://www.backfromafk.com/classes/eniripsa/depuis-3-4/) — `corrobore-source`, zones à confirmer | ID invocation, propriétaire, contrôleur, ordre glyphe début/fin, applicabilité à chaque équipe |

## État initial proposé, reproductible quand la carte est fournie
- **Acteurs et ordre, tour N :** E (Eniripsa allié), puis P (placeur), puis D (frappeur), puis X (ennemi) ; T (tank allié) agit au tour suivant ou après D selon fixture. Équipes distinctes. L'ordre imposé `E → P → D` est *une hypothèse d'équipe choisie*, pas un ordre naturel des classes.
- **Compteurs individuels :** E commence avec 12 PA disponibles, 6 PM **hypothétiques**, D et P ont PA/PM serveur individuels ; aucun transfert des PA de E à leur budget courant sans confirmation du buff côté destinataire.
- **Positions relatives :** E=(0,0), T=(1,0), P=(1,1), D=(2,0), X=(3,0) à titre de repères ; carte réelle, alignements, LoS et cases de destination **inconnus**. N'engager un lancer que si le serveur atteste sa légalité. L'exemple suppose une configuration où les six lancers sont autorisés ; les coordonnées ne prouvent pas cette légalité.
- **État des cibles :** T à 900/3000 PV, D à 1700/3000, P à 2100/3000 (exemples **fabriqués pour fixtures**, pas vidéo), X menace T. D/P/T n'ont pas déjà un Encouragement du lanceur ; sinon noter sa famille et son expiration. Aucun allié Insoignable avant le tour.
- **Sorts disponibles :** Mot Stimulant (pas variante Déclin), Mot Vivifiant (pas variante Galvanisant), Mot de Jouvence (pas variante Déprimant), Mot Accablant (pas variante Décourageant), Mot d'Envol (pas variante Fontaine de Jouvence), Mot de Frayeur ; niveau/rangs hauts pour les valeurs utilisées. Le bot doit lire le deck effectivement choisi.

## Un tour de soutien à 12 PA — séquence conditionnelle

| Étape | Acteur / PA E restants | Sort → cible | Préconditions | Attendu de source | Confirmation exigée |
| --- | --- | --- | --- | --- | --- |
| 1 | E, 12→10 | Stimulant → **D** | D vivant, allié, portée OK, pas variante ; 1/tour | D reçoit Encouragé/Stimulant, bonus +2 PA en phase à déterminer | ID D ciblé, état + provenance, valeur/expiration côté D |
| 2 | E, 10→8 | Vivifiant → **P** | P distinct de D, portée/LoS légales | P reçoit Encouragé/Vivifiant, +2 PM, 30 Fuite | ID P et PM/fuite ; **aucune addition au D** |
| 3 | E, 8→6 | Jouvence → **T** | T en vie, blessé, hors Insoignable, portée/rang | Soin immédiat % PV max, -1 durée d'effet, Encouragé/Jouvence ; soin début de tour proposé | État PV T avant/après, effets diminués, événement différé avec bénéficiaire T |
| 4 | E, 6→4 | Accablant → **X** | X hostile, ciblable, portée et LoS | Moqué, tentative retrait PM jusqu'à 3 et -20 Esquive PM | Retrait réel enregistré ; ne pas fixer PM=0 si esquive |
| 5 | E, 4→1 | Envol → **T** | T encore vivant, échange légal, cases non bloquées | Échange de positions E/T ; la cible des soins antérieurs **reste T** | Deux `entityId` et deux cellules confirmées ; retester la portée de l'étape 6 |
| 6 | E, 1→0 | Frayeur → **X** | Nouvelle position E, X toujours éligible, cible légale | Recul d'une case si déplacement autorisé | Mouvement effectif X ou Refused/Unknown sans inventer nouvel emplacement |

**Ordre temporel :** les buffs de P/D restent enregistrés sur **leurs identités** indépendamment de leur nouvelle position. Lors des tours de P et D, relire les valeurs de PM/PA réellement actives, leur expiration et les effets des sorts ; ne pas dépenser « +2 PA » dans le tour de E. Au tour de T, vérifier le soin différé de Jouvence **si encore actif**. Pour X, le retrait PM de Accablant peut être partiellement/totalement esquivé. Le montant exact de soin effectif est plafonné par PV manquants, états et bonifications ; le 10 % n'est **pas** nécessairement des PV gagnés.

## Branche sauvetage par Reconstitution
Si T est presque mort et menacé avant son prochain tour : E peut remplacer une partie de la séquence par **Reconstitution T (5 PA)**, rang haut à 8 PO modifiable **en ligne** selon la fiche. Effet source : soin total puis Insoignable ; durant l'état, ne pas ajouter Jouvence, soin de zone ou transferts sur T comme s'ils allaient restaurer des PV. La survie future dépend de dégâts entrants, réduction/résistances et placement ; Reconstitution ne rend pas invulnérable. Le cooldown de six tours appartient au **sort et lanceur E**, pas à T.

## Duo, trio, quatuor
| Composition illustrative | Ordre favorable demandé | Valeur réelle | Perte/risque si l'ordre change |
| --- | --- | --- | --- |
| Solo E | E→X | Vol de vie, retrait/placement ou auto-soin si légal ; **le tour de soutien sur trois alliés ne s'applique pas** | Plus de dépenses défensives, durée longue ; aucune cible alliée pour transfert de rôle |
| Duo E+Iop | E→Iop→X | +2 PA sur Iop **si effet confirmé** ; soigner le DPS si nécessaire | Iop→E : pas de sort supplémentaire sur un tour Iop déjà fini ; garder le buff pour son prochain tour |
| Duo E+Féca | E→Féca→X | Soins + couverture défensive envisagée par la description V15-A | Si Féca protège après un coup fatal ou est hors portée, le soin prioritaire n'est plus facultatif |
| Duo E+Sadida | E→Sadida→X | Usure/contrôle annoncé en **avis de vidéo** ; dépend des invocations | Cibles/effets de Sadida non documentés dans M15, donc coût tactique inconnu |
| Trio E+P+D | E→P→D→X | Vivifiant P, Stimulant D, Jouvence E/T si existant | D→P : le placeur n'a pas déplacé la cible avant DPS ; gain décalé au cycle suivant |
| Quatuor E+T+P+D | E→P→D→T→X (fixture) | Trois bénéficiaires distincts ; choisir entre buff et soin d'urgence, conserver PV T | Buff sur mauvais allié, Insoignable, collatéraux d'Envol ; un deuxième Mot sur même allié écrase le premier |

## Tests discriminants proposés (non exécutés)

| Cas | Initial et action | Observable attendu | Règle discriminée |
| --- | --- | --- | --- |
| P15-01 positif | E→P→D ; 12 PA ; D/P/T différents ; six sorts ci-dessus | Dépense E=12, 3 acteurs/identités d'Encouragement distincts, PM X réels, échanges E/T confirmés, temps d'activation des buffs par cible | R01–R05 |
| N15-01 initiative inverse | D→E→X, D a déjà dépensé son tour lorsque E lance Stimulant | **Aucun bonus comptabilisé rétroactivement** dans le tour terminé de D ; prochaine fenêtre d'activation/expiration à enregistrer | R01 + calendrier |
| N15-02 cible écrasée | E lance Stimulant(D), puis Vivifiant(**D**) dans la même fenêtre légalement disponible | Un seul Encouragement final effectif sur D selon source ; pas d'addition automatique +2 PA/+2 PM. Témoin : Vivifiant(P) préserve Stimulant(D) | R01–R02 |
| N15-03 Insoignable | Reconstitution(T), ensuite tentative Jouvence(T) pendant Insoignable | Premier soin total, statut posé, second soin nul/refusé suivant règles réellement observées ; **pas de PV prédits** | R03/R06 |
| N15-04 cible éliminée | T meurt ou disparaît avant l'étape 3 ou 5 | Invalidité de la cible, plan raccourci/refusé ; aucune guérison sur une entité absente | Identité et état de vie |
| N15-05 position modifiée | Envol E↔T, X devient hors portée de Frayeur | Nouvelle LoS/portée calculée, action 6 remplacée ; pas d'ancienne position réutilisée | R05 et ordre |

## Inconnues avant implémentation
Descriptions officielles du client 3.7.13.17, rang/deck réel, phase d'application des +PA/+PM, durée en tours et rafraîchissement, ordre exact soins vs réduction d'effet, statut Insoignable et suppression, règles d'effet sur Lapino/Fées, LoS et obstacles, interactions sur compagnon, cap d'Encouragements par lanceur, résolutions serveur et tests visuels. Le journal doit distinguer `CastRequested`/`CastAccepted`/`EffectApplied`/`Unknown` : pas de succès présumé sur ACK. Aucun test ni combat exécuté.

