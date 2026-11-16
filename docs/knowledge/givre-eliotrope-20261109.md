# Éliotrope : remplacement du Sacrieur, montée 1 → 200

L'utilisateur remplace Bastionbot (Sacrieur 199) par un **Éliotrope** à monter jusqu'au niveau 200,
et demande que le bot le joue au mieux **à chaque palier**, pas seulement à 200 :
« faut qu'il soit le meilleur eliotrope genre il sait tout ce qu'il doit faire ».

## Le support portails était du CODE MORT

`GivreClone.App/Services/Combat/ClassMechanics.cs` déclare `record Portal(int Id, int Cell, long OwnerId)`,
`ResolveExitPortal`, `CanHitThroughPortals`, `PortalDamageMultiplier`, `EvaluateIopThroughPortal`.
Vérifié par grep : **appelés uniquement depuis `ClassMechanicsTests.cs`**. Aucun `Portal` n'est jamais
construit depuis l'état de jeu, et le drapeau `ThroughPortal` du résolveur est **figé à `false`**.
Conséquence : un Éliotrope piloté aujourd'hui poserait ses portails sans jamais s'en servir pour viser.

Même audit sur les autres classes : seules 4 mécaniques sont réellement branchées au planner
(Roublard `EvaluateRogueBombTactics`, Sacrieur `EvaluateSacrieurCombo`, Zobal `EvaluateZobalShieldOnSummons`,
Forgelance `EvaluatePandaForgelanceCluster`). 17 membres sont morts ou testés seuls, dont tout le Xélor
(`GeneratesTelefrag`, `EvaluateXelorTelefrag`).

## À VÉRIFIER : le breed id

Le code déclare `EliotropeBreedId = 15`. Un agent qui a lu les bundles du client
(`data_assets_breedsdataroot.asset.bundle` croisé avec `fr.bin`) rapporte que **15 = Steamer** et
**16 = Éliotrope**. **Non vérifié par moi-même** : ne pas toucher la constante avant contrôle direct,
sinon tout le code portail viserait la mauvaise classe.

## Données de sorts extraites du client

Livrable : `reference/eliotrope-sorts-donnees-client-20261109.md` (39 Ko), extrait des bundles Unity
locaux via un extracteur autonome sous `artifacts/elio-dump/`.
- 44 sorts (22 paires de variantes), 24 sorts de dégâts, **6 par élément**, aucun multi-élément.
- 7 sorts de portail : `Portail` (14574, 1 PA), `Errance` (14604), `Sillage` (14591), `Stupeur` (14585,
  pose DEUX portails), `Exil` (14609), `Résonance` (14611), `Vestige` (14612). Fermeture : `Neutral` (14582),
  `Interruption` (14607, ferme tout le terrain).
- Plafond 4 portails actifs, durée de vie `-1` (permanente).
- Bonus mesuré : **+2 % de dégâts finaux par case** entre portail d'entrée et de sortie.
- 20 sorts changent de comportement routés dans un portail, repérables au `targetMask` `r` (direct) vs `R` (routé).
- **État 7 = Pesanteur** : `Sillage`, `Exode`, `Stupeur` portent `statesCriterion = "HS!7"` (interdit sous
  Pesanteur) → c'est la cause des refus serveur message 175 sur ces sorts.
- Meilleur ratio brut : `Sermon` (Air, 4 PA) à 10,0 dégâts/PA.

## Deux correctifs de sélection de sorts livrés le 09/11

1. **Filtre élémentaire qui dormait** (`AutoSpellRotation.cs`) : il ne s'activait qu'à partir de
   **80 points** de caractéristique. Tout personnage en montée reste sous ce seuil, donc les 4 éléments
   étaient admis, et comme les dégâts de BASE d'un sort hors-élément dépassent ceux d'un petit sort dans
   le bon élément, le mauvais sort passait devant. C'est le défaut signalé en jeu (« des sorts d'éléments
   que j'ai pas et ça fait très peu de dégâts »). Seuil ramené à **15**, avec plancher proportionnel
   `max(min(40, maxStat*0.6), maxStat*0.35)` pour préserver les builds hybrides.
2. **Sorts débloqués ignorés** (`LaunchService.Packets.cs`) : `SpellsEvent` n'arrive **qu'à la connexion**,
   donc la liste des sorts appris restait figée et un sort gagné en montant restait invisible pour l'IA
   jusqu'à reconnexion. La liste est désormais invalidée sur `CharacterLevelUpEvent` ; la rotation, elle,
   était déjà reconstruite à chaque tour depuis `LearnedSpells(pid)`.

## Reste à faire

- Vérifier le breed id (15 vs 16) avant toute autre modification Éliotrope.
- Construire un `Portal` depuis les paquets et alimenter `ThroughPortal` pour que la portée à travers
  les portails entre dans le planner.
- Modéliser le placement de portail, dont le bénéfice n'arrive qu'au tour suivant : un scoreur
  mono-tour le sous-évalue toujours à 0.
