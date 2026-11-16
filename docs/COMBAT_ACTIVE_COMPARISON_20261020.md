# Audit du combat : GivreActive (lecture seule) vs GivreWork

**20 octobre 2026 — P1.** Analyse statique. GivreActive n'a été ni modifié ni démarré ; aucun client, script réel ou compte n'a été utilisé. Départ du worktree d'intégration : HEAD 80be2aa.

## Sources GivreActive auditées (chemins relatifs à GivreActive)

| Preuve (fichier : lignes) | Constat |
| --- | --- |
| AGENTS.md : 9-47, 76-96 | Règles de non-divulgation, compte de test, mesures ABBA ; les chiffres historiques ne prouvent rien sur GivreWork |
| EasyBot/PacketAnalyser/Function/Combat/IA-BAsic/Spell.vb : 9-115 | Configuration par sort : Target, Turns, Relaunch, TargetLife, IsHandToHand, TouchMonster |
| EasyBot/PacketAnalyser/Function/Combat/IA-BAsic/PacketsFight.vb : 4216-4310 | Réordonnancement tactique dynamique : AP, portée immédiate, dégâts/zone, élimination de cible, dégâts gaspillés et recherche limitée |
| EasyBot/PacketAnalyser/Function/Combat/Ia-Avancee/BasicAI.vb : 13-61, 111-179 | Réglages de délai d'action, début/fin de tour, approche, configuration de sorts et tactiques Lua |
| EasyBot/PacketAnalyser/Function/Combat/Ia-Avancee/fightAI.vb : 48-89, 174-245, 365-542 | Raisons de rejet de lancer, plan de tir, mouvement tactique, plans de zone et attaque-retour |
| EasyBot/PacketAnalyser/Function/Combat/Ia-Avancee/characterAI.vb : 89-105 | Lectures des sorts connus/configurés et des états actifs |
| EasyBot/PacketAnalyser/Function/Combat/ChallengeSelectionPolicy.vb : 43-78, 123-175 | Sélection des défis par bonus attendu, prioris lissés et historique de réussite propre à l'ancien client |

## GivreWork : preuves et différences

| Preuve (fichier relatif au worktree) | Comparaison vérifiée |
| --- | --- |
| GivreClone.App/Services/CombatProfile.cs : 11-35, 60-99, 103-172 | Les profils couvrent Aggressive/Fleeing/Passive, Ally/Enemy, OnMe, IsSummon, conditions PV/PA/tour/zone et limites ; ils ne sont pas simplement « moins permissifs » |
| GivreClone.App/Services/LaunchService.Combat.Turn.cs : 337-386 | Rotation fixe par Priority avec sélection FinishKill et budget de dégâts ; pas le réordonnancement beam-search de GivreActive |
| GivreClone.App/Services/CombatSimulator.cs : 110-150 | Simulation des limites par sort/cible et du ciblage létal, distincte du planificateur tactique Active |
| GivreClone.App/Services/ChallengeCatalog.cs : 145-173 | Politique du meilleur bonus espéré déjà existante (probabilités heuristiques), à distinguer de l'apprentissage historique d'Active |
| GivreClone.App/Services/GivreLuaHost.Combat.cs : 298-355 | Introspection du profil et des défis ; choix de bonus Lua non confirmé/non exposé comme action |
| GivreClone.App/Services/LaunchService.Combat.Turn.cs : 719-815 | Deux passes de recast sur cellule ennemie après mouvement, filtrées par SpellConfig.CanUseOffensiveFallback |

### Analyse

1. **Plus riche sur la planification :** Active possède un ordonnancement tactique tenant compte des PA, dégâts attendus, hits de zone et cibles éliminables (PacketsFight.vb : 4216-4310). Work privilégie la priorité choisie, avec KillFirst. Reproduire cette recherche demanderait des états fiables de résistance, portée et phase de combat ; aucune promesse de gain sans test ABBA.
2. **API Lua plus détaillée chez Active :** BasicAI.vb : 27-61 et fightAI.vb : 365-542 exposent distances, temporisations et plans de combat. Il ne faut pas assimiler un réglage Lua plus « permissif » à une permission de contourner des restrictions du jeu ; les paramètres de vitesse/ACK/protocole ne sont pas portés ici.
3. **Diagnostics du lancer :** Active expose des raisons chiffrées d'impossibilité (fightAI.vb : 48-89). Work contrôle déjà PA, portée, LdV, cast-per-turn et cast-per-target dans LaunchService.Combat.Turn.cs : 337-357, 730-796 ; l'interface de diagnostic n'est pas au même niveau de détail.
4. **Défis :** les deux produits savent estimer une valeur attendue. Les taux d'Active (ChallengeSelectionPolicy.vb : 43-64) proviennent d'un contexte historique 3.7.7.6 et ne sont pas à importer comme des faits universels dans Work.
5. **Anomalie P1 certaine :** une invocation marquée IsSummon=true peut conserver le TargetKind Enemy par défaut ; l'ancienne méthode SpellConfig.CanUseOffensiveFallback ne vérifiait que OnMe=false et TargetKind=Enemy. Or le filet offensif passe une cellule **occupée par un ennemi** (Turn.cs : 730-755, 780-800) tandis que la branche normale d'invocation exige une cellule libre (Turn.cs : 622+). Cette classification incorrecte pouvait déclencher une tentative de lancement inappropriée.

## Correction P1 effectivement raccordée au moteur existant

- GivreClone.App/Services/CombatParity/OffensiveFallbackPolicy.cs : fonction pure sans effet ni paquet, retourne true uniquement si le sort est actif, ID positif, PerTurn positif, pas OnMe, pas IsSummon, ciblage Enemy.
- GivreClone.App/Services/CombatProfile.cs : CanUseOffensiveFallback délègue à la fonction ; elle est **déjà appelée** par les deux phases de recast du moteur. La branche d'invocation normale reste inchangée.
- GivreClone.Tests/CombatParityFallbackTests.cs : tests directs du vrai SpellConfig et du prédicat liés depuis les sources App par le projet de tests, sans fenêtre ni assembly App précompilée ; cas positifs/négatifs et clone profond.

La correction est délibérément un filtre restrictif, pas un nouveau lanceur. Le moteur conserve ses contrôles de droits, PA, portée, LdV, état et limites serveur. Aucun mapping, paquet ou identifiant de sort n'a été inventé.

### P1 complémentaire demandé par le prime : filet de dernier recours

L'audit a confirmé une deuxième incohérence entre la rotation principale et le filet
LastChanceAttack de GivreWork (LaunchService.Combat.Turn.cs : 763-800).
La boucle normale (lignes 492-503, 588-605) applique le cooldown du profil, les
conditions du lanceur/de la cible et la plage de portée personnalisée. LastChanceAttack
ignorait explicitement le cooldown, n'appliquait pas les conditions Self/Target, utilisait
la portée native au lieu de PlacementRange et ne contrôlait pas le nombre de lancers
déjà effectués pour le PerTurn du profil.

- GivreClone.App/Services/CombatParity/LastChanceCastPolicy.cs : garde pur (PA,
  coût, cooldown, numéro de tour et nombre de lancers déjà consommés).
- GivreClone.App/Services/LaunchService.Combat.Turn.cs : LastChanceAttack réutilise
  les vérifications SelfSpellGateOk, TargetSpellGateOk et PlacementRange déjà
  employées ailleurs par le moteur, en plus du nouveau garde pur.
- GivreClone.Tests/CombatParityLastChanceTests.cs : tests déterministes directs des
  sources liées, cas de budget disponible, dépassement PerTurn, PA insuffisants,
  cooldown non expiré et numéros de tour non cohérents.

Ce changement retire des tentatives autrefois autorisées à tort par le filet, mais
conserve les tirs valides qui avaient simplement été oubliés avant le mouvement final.
Il ne requiert aucun protocole supplémentaire et ne désactive pas le comportement
de repli lorsqu'un sort reste disponible selon ses propres règles.

## Limites / suites non implémentées

- Pas de mesures de combats/min, XP/min, taux de défis ni ABBA, pas de vérification d'ACK serveur ou d'efficacité gameplay.
- Pas de portage du beam-search ni d'API de simulation de combat en direct : exigeraient un protocole et des instantanés vérifiés et une modification de fichiers hors OWN.
- Éventuelle suite : diagnostics **en lecture seule** expliquant l'éligibilité des sorts, puis planificateur hors ligne et tests comparatifs contrôlés.
- Validation finale : build App ciblé antérieur **PASS, 0 erreur, 193 avertissements** ;
  nouveau csproj avec sources CombatParity liées, tests CombatParityFallbackTests +
  CombatParityLastChanceTests **30/30 PASS après recompilation des tests**, sans dépendance
  à un binaire App précompilé ; diff --check des fichiers de production modifiés **PASS**.
- Ces validations prouvent les prédicats et les chemins applicatifs compilés, **pas** les résultats de combat dans Dofus. Aucun A/B ou serveur ACK n'a été testé.
