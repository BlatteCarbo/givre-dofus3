# Audit de parité des sorts — GivreActive / GivreWork (20 octobre 2026)

## Périmètre et méthode

Inspection **statique, hors jeu, en lecture seule** du répertoire `GivreActive/EasyBot/PacketAnalyser/Function/Combat`, de ses sous-dossiers `IA-BAsic` et `Ia-Avancee`, et du formulaire WinForms de configuration des sorts. Comparaison avec `GivreWork-Integration-20261020` (branche `integration-opus-20261020`). Aucun client, paquet ou compte Dofus utilisé. Les fonctionnalités citées comme « présentes » sont observées dans le code source, **pas certifiées en combat réel**.

## Constats par fonction

| Fonction | GivreActive (preuve source) | GivreWork (preuve source) | Verdict |
|---|---|---|---|
| Types de cibles configurables | `IA-BAsic/TeamEnum.vb:1-17` : **16** valeurs : Enemy, Ally, Self, adjacents, invocation/non-invocation, companion et combinaisons ; `PacketsFight.vb:5659-5750` dispatch des 16 | `Services/CombatProfile.cs:27-34,61-79` : Enemy/Ally et booléen OnMe ; invocation configurée séparément ; `LaunchService.Combat.Turn.cs:598-625` | **Écart réel d'expressivité UI**. Ne pas ajouter les 13 variantes sans prises en charge dans le moteur et données fiables de combattants |
| Restrictions de géométrie | `Ia-Avancee/FightManager.vb:3302-3349` : ligne/diagonale, PO, occupation, LdV | `LaunchService.Combat.cs:35-50`, `LaunchService.Combat.Turn.cs:581-604`, `GameNameResolver.SpellCastConstraints` : contraintes correspondantes | Présentes côté code, valeurs de runtime non certifiées |
| Lancers **par cible** | `IA-BAsic/PacketsFight.vb:3200-3233` : compteur indexé par **identifiant d'acteur** (survit au changement de cellule) ; `FightManager.vb:2943-2944` | `LaunchService.Combat.Turn.cs:348-357`, `Script.cs:416-420`, `CombatSimulator.cs:168-174` : clés (SpellId, ActorId) | **Parité de principe**, aucun bug de compteur par cellule constaté |
| Lancers **par tour** | `SpellDataCache.vb:163-166`, `PacketsFight.vb:5921-5927` + relais en IA avancée | `LaunchService.Combat.Turn.cs:490-507`, `Script.cs:415-420` ; profil `PerTurn` et plafond global | Présents ; Work applique à la fois plafonds par sort et profil |
| Cooldown initial / intervalle minimal (règles serveur) | `SpellDataCache.vb:163-165` les lit ; `FightManager.vb:2853-2856` vérifie `initialCooldown` ; `PacketsFight.vb:5921` | `CombatProfile.cs:68` et `LaunchService.Combat.Turn.cs:492-496` : cooldown **configuré** ; `GameNameResolver.SpellCombat` et `GetSpellDamage` ne fournissent pas ici les deux contraintes serveur | **Écart de validation statique** : évaluer un accès fiable aux cooldowns natifs avant toute correction moteur |
| Priorité | `IA-BAsic/PacketsFight.vb:524-536` : candidats classés selon possibilité immédiate, kills, dégâts/PA, zone, dégâts, sur-dégâts, puis priorité utilisateur ; `Spell.vb:104-115` | `CombatProfile.cs:66` et `BotsViewModel.Combat.cs:442-477` : ordre explicite par priorité ; `LaunchService.Combat.Turn.cs:485-507` | **Politiques différentes**, choix métier. Ne pas importer l'arbitrage dynamique sans métriques ni moteur propriétaire |
| Limites et options UI | `BotformSwift.Designer.vb:3057` : 16 types cibles ; `BotformSwift.vb:4630-4635,4831-4837` : tours, relances, corps-à-corps, PV cible, TouchMonster | `CombatOverlays.axaml:60-200` : priorité, tours/relances, cible allié/soi, invocations, PV, conditions et zones ; `BotsView.axaml:939-940` affiche MaxPerTarget | Work offre davantage de conditions génériques mais moins de familles de cibles |
| Données de sorts | `SpellDataCache.vb:113-130,144-172` : parse complet **mais commentaire explicite sur le lecteur GameData D2O obsolète** et branchement SQLite non terminé dans ce chemin | `GameNameResolver.cs:374-397,495` : données bundle pour PO/flags/limites | L'existence de champs en Active **ne prouve pas** qu'ils sont tous alimentés au runtime |

## Bug d'interface corrigé dans le lot

`CombatImportLogic.SameSpell` compare 21 attributs d'une ligne de sort (`Services/CombatImportLogic.cs:68-74`), y compris cible, activation, limitation, portée et fenêtres de PV. L'ancien `Describe` (`CombatImportLogic.cs:76-78`) n'affichait que priorité, nombre de lancers, cooldown et activation : un changement de **cible ennemi vers allié** était marqué différent, mais les colonnes `Actuel/Importé` pouvaient afficher un texte identique.

Correction : `SpellParityDisplay.ChangedFields` calcule les écarts de toutes les propriétés suivies par `SameSpell`. `CombatImportLogic.Describe` et `BotsViewModel.CombatExtras.cs:130-136` affichent **en premier les champs qui vont changer et leur valeur actuelle/importée**, puis le résumé, sans modifier la rotation ni le moteur. Les champs de ciblage et restrictions précèdent les différences de priorité. La prévisualisation est pure.

## Risques moteur et observations transmises au prime, hors fichiers autorisés

1. **Corrigé en parallèle par un autre intervenant du worktree pendant cet audit** : la version initialement inspectée de `LaunchService.Combat.Turn.cs:763-800` comportait un filet `LastChanceAttack` ignorant `Cooldown`, `TargetSpellGateOk`, `SelfSpellGateOk` et `PlacementRange`. La version relue après modification concurrente (`LaunchService.Combat.Turn.cs:763-803`) appelle désormais `CombatParity.LastChanceCastPolicy.CanAttempt`, `SelfSpellGateOk`, `PlacementRange` et `TargetSpellGateOk`. **Correction source observée, non certifiée en jeu par cet audit**. Elle appartient à un autre propriétaire ; aucun hunk de ce fichier n'a été modifié par worker-8.
2. Les 16 types de cibles d'Active exigeraient des informations sûres sur équipe, invocations, compagnons, adjacency, ainsi que tous les chemins de combat (rotation, rattrapage, recast après déplacement, simulateur, Lua). Exposer seulement un nouveau menu créerait des réglages non appliqués. **Aucune extension non opérationnelle n'a été ajoutée**.
3. La priorité dynamique d'Active est fondée sur une estimation de dégâts et d'impacts ; aucune preuve n'autorise à remplacer les préférences Work par cette politique. Mesurer les effets avant adaptation.
4. Le `SpellDataCache` d'Active avertit que son lecteur D2O est dépassé pour Dofus 3.7.x dans ce chemin. Faire la distinction entre modèle de données déclaré et disponibilité réelle.

## Tests hors jeu et conditions de validation

`SpellParityDescriptionTests` vérifie tous les champs différenciés par `SameSpell`, les textes importés opposés, leur ordre de priorité, l'identité sans écart, et l'absence de mutation. Ces tests ne valident aucun type de sort inconnu, paquet réseau ni succès d'un combat.

Le validateur des limites réelles doit rester conservateur en l'absence de données de sorts. Les changements de ciblage avancé et d'intervalles serveur sont à traiter dans un lot moteur séparé. Le correctif concurrent du `LastChanceAttack` doit être validé par ses tests propres puis lors de la recette intégrée.
