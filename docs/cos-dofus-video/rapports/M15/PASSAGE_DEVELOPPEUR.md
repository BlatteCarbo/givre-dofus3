# M15 — Passage développeur Eniripsa, coordination de cibles et tours

**Objectif d'intégration future :** le moteur conserve les personnes bénéficiaires, l'ordre des tours et les effets temporels plutôt que de traiter « soin / buff Eniripsa » comme une statistique globale.

## Trois règles prioritaires

| ID | Transition | Source & niveau de certitude |
| --- | --- | --- |
| **M15-R01/R02** | Stimulant(+2 PA)/Vivifiant(+2 PM, +30 Fuite), chacun 2 PA sur une **cible alliée identifiée** ; la famille des Encouragements peut se remplacer sur la même cible. | [Grimoire 3.7.12, Les Mots](https://dofus-grimoire.fr/classes/eniripsa/), `corrobore-source`. Horloge exacte et cumul à valider serveur. |
| **M15-R03/R06** | Jouvence 2 PA : soin % PV max + réduction de durée et effet au début du tour ; Reconstitution 5 PA / relance 6 tours : soin total puis **Insoignable**. | [Grimoire 3.7.12](https://dofus-grimoire.fr/classes/eniripsa/), `corrobore-source`, phase/expiration hypothétiques sans reçus serveur. |
| **M15-R05/R07** | Déplacement Envol E↔allié ou Frayeur, changement de topologie ; si jeu autour du Lapino, distinguer la cible invocation et les alliés dans sa zone, ainsi que son événement de mort. | [Grimoire 3.7.12](https://dofus-grimoire.fr/classes/eniripsa/), [Back from AFK diff 3.4→3.7](https://www.backfromafk.com/classes/eniripsa/depuis-3-4/). |

## Variables nécessaires au simulateur
- `combatVersion`, `turnEpoch`, `actorId`, `ownerId`, `teamId`, ordre des tours ; association **source du buff → cible bénéficiaire**, sans appliquer les PA/PM d'un acteur à un autre.
- `spellDeck` choisi (Mot de Déclin **variante** de Stimulant ; Mot Galvanisant variante de Vivifiant ; Mot Déprimant variante de Jouvence ; Mot de Solidarité variante de Reconstitution). Conserver rang, portée/LoS, coût, relance, limite de lancer, état sur cible et ID serveur.
- `effectsByTargetId` avec `family`, `sourceActorId`, `startPhase`, `expirePhase`, `remainingDuration`, stack/remplacement, heal immédiat/différé, Insoignable.
- `hp/hpMax` avant/après, réduction de soin, bouclier, état morte/invulnérable ; clore l'effet si sa cible cesse d'exister. Les pourcentages sont des **descriptions de sort**, pas des montants garantis.
- Invocations séparées `LapinoId`, `controllerId`, cellules/équipe/rayon ; événements de mort et glyphes début/fin. Pas de diffusion de buff aux trois autres joueurs par défaut.
- Journaux de résolution : `CastRequested`, `CastAccepted/Refused/Unknown`, `BuffApplied`, `Healed`, `StatusApplied/Removed`, `UnitSwapped`, `ActorTurnStarted`, `EffectExpired`. Ces noms sont *conceptuels*, pas des IDs protocolaires. ACK de commande ≠ résultat combat.

## Séquence positive entièrement définie en ressources
E=Eniripsa, T=tank, P=placeur, D=frappeur, X=ennemi. Budget initial de **12 PA E** ; deck de sorts compatibles, rang haut. Ordre de tour *exemple* E→P→D→T→X, positions légales et cibles vivantes à confirmer.

1. **2 PA Stimulant(D)** : stocker buff sur **D**, et non sur E ou P ; recevoir son application serveur.
2. **2 PA Vivifiant(P)** : stocker +PM/Fuite sur P, distinct de D ; disponibilité de PM à son tour à vérifier.
3. **2 PA Jouvence(T)** : soin et réduction d'effets, préparer événement au début de son prochain tour si confirmé.
4. **2 PA Accablant(X)** : retrait PM et esquive PM annoncés, **valeur retirée variable**.
5. **3 PA Envol(T)** : recalculer les deux positions et toutes les portées suivantes.
6. **1 PA Frayeur(X)** : n'autoriser que depuis la nouvelle position E et si X est encore vivant/ciblable.
Après chacune, enregistrer l'état et annuler la suite si une précondition n'est plus vraie. Au tour de P puis D, lire les buffs actifs ; au tour de T lire les soins différés. **Aucun gain de PA rétroactif** pour les tours déjà passés.

## Deux contre-scénarios obligatoires et deux extensions
- **N15-01 initiative** : ordre D→E→X et Stimulant(D) après D. Ne pas compter le bonus pour le tour achevé ; prochain début de tour de D et expiration doivent être tracés. Témoin : E→D.
- **N15-02 cible** : Stimulant(D) puis Vivifiant(D), comparer au témoin Stimulant(D)+Vivifiant(P). Dans le premier cas, vérifier le remplacement d'Encouragement ; ne pas cumuler automatiquement les deux bénéfices. Aucun résultat « magique » sur P.
- **N15-03 Insoignable** : Reconstitution(T) 5 PA, puis nouvelle tentative de soin sur T. T peut être plein PV et Insoignable ; vérifier chaque effet et sa durée, sans annoncer un deuxième soin.
- **N15-04 placement** : Envol change E/T et ferme la ligne de Frayeur. Abandonner Frayeur et proposer repli si légal ; pas de replay destructeur.

## Duo/trio/quatuor en entrée de planification
- **Duo** : E+Iop avec E→Iop = orientation offensive ; E+Féca = réduction de dégâts ; E+Sadida = durée/contrôle. Ces trois *préférences de composition proviennent exclusivement de la description de [XAWPIYA, 8 août 2026](https://www.youtube.com/watch?v=SbrNWSScKPc)*, pas de la vérification des sorts des partenaires. La vidéo est antérieure à 3.7.
- **Trio** : E/P/D avec P→D et E au préalable lorsque possible ; si D joue avant P, évaluer les dégâts directs sans placement ou la préparation pour le cycle suivant.
- **Quatuor** : E/T/P/D ; buffs différents sur P/D/T, prioriser Reconstitution du T gravement menacé malgré coût/effet Insoignable. Une activation au tour d'un allié ne donne **jamais** des PA à E par confusion d'identité.

## Preuve et validation manquantes
MCP metadata et langues disponibles sur [XAWPIYA](https://www.youtube.com/watch?v=SbrNWSScKPc) mais `get_timed_transcript` et `get_transcript` **bloqués par l'IP YouTube** le 4-11-2026 ; aucun horodatage ni parole attribué. Sources de mécanique : [Grimoire version affichée 3.7.12](https://dofus-grimoire.fr/classes/eniripsa/) et [Back from AFK 3.7](https://www.backfromafk.com/classes/eniripsa/depuis-3-4/). Obtenir journal serveur expurgé, capture d'effet en combat et texte client réel avant `valide-test`/`valide-live`. Tous les scénarios sont **proposés non exécutés**. Aucun fichier de code, SYNTHESE.md, Git ni jeu modifié.

