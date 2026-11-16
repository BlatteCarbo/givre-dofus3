# P02 — Complément indépendant : origine du décompte et frontières discriminantes

**04 novembre 2026.** Ce complément conserve intégralement les livrables `RESULTAT.md`, `PREUVES.md` et `chronologies.json` déjà présents dans le dossier P02 lors du contrôle final. Il ajoute une source historique primaire reprise et dix **contre-épreuves synthétiques non exécutées** sous `chronologies_candidates.json` (50 événements ordonnés). Les ID P02-01 à P02-10 correspondent aux mêmes **familles et sens d'initiative** que le premier JSON, mais chaque document utilise ses propres candidats et sa propre granularité. Ne pas fusionner leurs `spellId` implicites ni interpréter une cible alliée sans décodage de `targetMask`.

## Source historique nouvellement retrouvée, périmètre strict

- [JeuxOnline, 26/09/2011, reproduction du devblog Ankama « Modifications sur la durée des effets de sorts »](https://dofus.jeuxonline.info/actualite/32815/devblog-modifications-duree-effets-sorts) : la section **« Les inconvénients du système actuel »** explique qu'avant la 2.4 le décompte/fin des effets se produisait **à la fin du tour de la cible**. La section **« Les différences du nouveau système »** précise le basculement **au début du tour du lanceur**, à partir de la mise à jour 2.4 ; elle compare explicitement une protection sur un allié à une frappe ennemie après le tour de cet allié. La source mentionne également les sorts différés et effets déclenchés comme distincts des effets immédiats. **Version attestée par le texte : 2.4, en 2011 ; serveur 3.7.13.17 : Unknown.**
- [JeuxOnline, 26/09/2011, reproduction des notes officielles « Modifications apportées par la version 2.4.0 »](https://dofus.jeuxonline.info/actualite/32814/modifications-apportees-version-240) : recoupement de la convention historique. Ne pas présenter cet article comme preuve d'une mécanique restée inchangée en 2026.

**Frontière qui départage ces deux hypothèses :** ordre `S^n.cast → B^n.start → B^n.end → E^n.hit → S^(n+1).start` pour **un effet défensif de durée nominale 1** réellement appliqué à B. Sous l'ancienne convention `H_BENEFICIARY_END`, la protection serait retirée après `B^n.end` ; sous la convention `H_CASTER_START`, elle persisterait à `E^n.hit` puis disparaîtrait au début du tour `S^(n+1)` si aucun désenvoûtement, rafraîchissement ou déclenchement spécial n'intervient. **Ces deux sorties sont des prédictions conditionnelles historiques, aucune n'est une observation du serveur cible.** Une seule capture d'icône ne suffit pas à déterminer si le bouclier s'est épuisé sur les dégâts ou si la durée est arrivée à zéro.

## Ce que les sources courantes permettent d'observer

| Source locale consultée en lecture seule | Preuve accessible | Frontière qui reste inconnue |
| --- | --- | --- |
| `server-mapping/fields.json:3087-3099` | `FightTurnEvent.character_id/turn_index/round` et `FightTurnEndEvent.character_id` : candidates pour horloger les deux acteurs | Pas d'ordre réel S/B/E ou de paquet historique joint à l'effet |
| `server-mapping/fields.json:3348-3386` | `GameActionFightEvent.modify_effects_duration`, `reduce_damages`, `points_variation`, `action_id`, `source_id` : champs mappés | Mapping ≠ occurrence, ni corrélation d'un buff à un retrait naturel |
| `GivreClone.Protocol/FightStatsReader.cs:476-488` | Décode une variation PA/PM avec cible et delta si message de combat disponible | Pas d'`effectUid`, de date d'expiration ni de cause de retrait dans le retour |
| `GivreClone.App/Services/LaunchService.Packets.cs:1386-1407` | Lit une variation PA/PM puis un état absolu de ressources si reçu | La variation n'est pas reliée dans ce chemin à l'horloge d'un effet |
| `GivreClone.App/Services/Combat/SpellResolver.cs:62-74,280-294,389-420` | Une ligne avec `delay>0` est conservée `Deferred`; les ressources et boucliers sont modélisés immédiatement pour les lignes non différées | Les durées/expirations ne sont **pas** simulées |
| `GivreClone.App/Services/Combat/CombatTurnPlanner.cs:479-484,549-568` | Valeur PA/PM pour allié immédiate, simulation du follow-up du lanceur | Aucun bénéfice hors du tour déjà clos de B démontré |
| `GivreClone.App/Services/LaunchService.Combat.SpellMods.cs:12-15,32-60` | `ApplySpellModifierEvent` utilisé pour les **modificateurs de caractéristiques des sorts** (portée, coût) | Ce reçu n'est **pas** un reçu universel pour les buffs PA/PM/PB |

## Comptage vérifié et contrôles client

Entrée `docs/cos-dofus-video/rapports-v2/N01/EFFECTS_3.7.13.17.csv`, **SHA256 `cbd685e8852966961a7d155c17d7a21dd23ec8e45cac70aa31be06300f89384b`**. Sous-ensemble N01 : quatre classes, **2 040 lignes d'effets**, client installé identifié **3.7.13.17**, build `2026-11-03T14:09:52+00:00` (`N01/CLIENT_SOURCES.json:2-6,33-40,57-75`).

Commande réellement exécutée, **exit 0**, répertoire racine du worktree :

```powershell
$d=Import-Csv docs/cos-dofus-video/rapports-v2/N01/EFFECTS_3.7.13.17.csv
foreach($f in @('duration','delay','effectTriggerDuration')) {
  $d | Group-Object -Property $f | Sort-Object { [int]$_.Name } |
    ForEach-Object { "$f=$($_.Name): $($_.Count)" }
}
```

Sorties : `duration` **-1:48 / 0:1586 / 1:237 / 2:85 / 3:84** ; `delay` **0:1997 / 1:20 / 2:19 / 3:4** ; `effectTriggerDuration` **0:1869 / 1:122 / 2:38 / 3:10 / 63:1**. Chacune des distributions totalise **2 040**. Ne pas convertir `effectTriggerDuration=63` en soixante-trois tours ni `duration=-1` en perpétuité sans moteur documenté.

Contrôles explicites, tous **lignes exactes** du CSV N01, **grade 1 normal** : `Flou Temporel` 13246/action111/uid210099 **ligne 1013**, `duration=1 delay=1` ; `Double` 12915/action128/uid405000 **ligne 110**, `duration=1 delay=2` ; `Détonateur` 13432/action120/uid259140 **ligne 374**, `duration=0 delay=0 targetMask=C` ; `Truanderie` 12902/action138/uid371825 **ligne 3**, `duration=3 targetMask=C` ; `Roublardise` 13431/action1020/uid259838 **ligne 354**, `duration=1`, masque complexe. Ces lignes **ne démontrent pas** à elles seules un transfert à B, une activation différée ni un retrait en combat.

Empreintes d'autres lectures calculées via `Get-FileHash -Algorithm SHA256`, **exit 0** : `SpellResolver.cs` `0d31722c41f62b1ed175a533ec73b1790391e3be60c25f045aeb6e05536cd2df` ; `CombatTurnPlanner.cs` `7618468265fc2d32b6c186adfb0e3917947a1c03b6c7c5e903344399bb142a3f` ; `FightStatsReader.cs` `c8a9bdf3e173a2548e1fb1669c0e9b96d38c5ef8798713074054a8cf7bb59ba1` ; `server-mapping/fields.json` `d5654436063351aff3c0179b2903fc302a9acc6181f2e9e9f2695404072bb4ea`.

## Relecture des chronologies complémentaires

Le fichier [`chronologies_candidates.json`](chronologies_candidates.json) conserve les **10 scénarios synthétiques** PA/PM/puissance/bouclier/réduction × S→B/B→S, **5 événements par scénario**, avec `serverOracle=Unknown` pour toute frontière qui demande un reçu. Chaque ligne d'effet client citée est étiquetée `clientRowControl`; aucun `spellId` de la source communautaire Eniripsa/Féca n'est réinventé. La proposition d'un bénéficiaire B reste conditionnée par l'éligibilité du masque, la présence du reçu de l'effet et son caractère actif avant la consommation de l'avantage.

**Validation réalisée :** `ConvertFrom-Json`, 10 scénarios, 50 événements ordonnés, deux ordres pour chacune des cinq familles, exit 0. **Blocages :** aucune trace d'application + suppression de la même instance de buff entre `B.end`, `E.hit` et `S.start`, donc impossible d'affirmer la convention 3.7.13.17. La cause d'un retrait (expiration, désenvoûtement, consommation) demeure `Unknown`.
