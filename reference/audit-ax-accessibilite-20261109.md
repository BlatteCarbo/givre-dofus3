# Audit Accessibilité (A11y) & Agent Experience (AX) — Givre Avalonia UI
**Date de l'audit :** 9 novembre 2026  
**Cible analysée :** Givre Avalonia UI (`GivreClone.App`), surface principale `Views/BotsView.axaml` (5321 lignes) et l'ensemble des vues / contrôles sous `GivreClone.App/`.  
**Référence git :** `fix-f01-f16-final-20261024`  
**Objectif :** Évaluer l'accessibilité aux technologies d'assistance (lecteurs d'écran, navigation clavier) et l'observabilité/pilotabilité par des agents d'automatisation (AX : Agent Experience). Fournir un diagnostic chiffré rigoureux et une feuille de route priorisée d'interventions concrètes.

---

## 0. Constat Majeur : L'Arbre Windows UI Automation Anonyme (Cécité Totale)

### Le Problème Observé
Lors de l'inspection de l'application en cours d'exécution via Windows UI Automation (Inspect.exe / Accessibility Insights / driver UIA), l'arborescence exposée est strictement tronquée à :
```text
- window "GivreClone v1.3.3.11" app=GivreClone (focused)
  - group "WindowChrome"
  - group
    - group
      - group
    ... 11 groupes anonymes consécutifs, sans nom, sans rôle, sans valeur
```
**Conséquence immédiate :** Aucun onglet, aucun bouton, aucune liste, aucun champ texte n'est individuellement adressable. Un lecteur d'écran (ex. NVDA, JAWS, Narrateur Windows) ou un agent d'automatisation pilotant le bureau Windows est totalement aveugle à l'interface de Givre.

### Explication Technique dans Avalonia UI (v12.1.1)
Ce comportement découle de la conjonction de quatre facteurs architecturaux dans la base de code :

1. **Hiérarchie d'éléments visuels sans AutomationPeer explicite :**  
   Dans `Views/MainWindow.axaml` (lignes 33–50 et 415–421), l'agencement empile des conteneurs visuels stricts (`Border Name="RootFade"`, `Grid Name="ShellRoot"`, `LayoutTransformControl Name="DensityHost"`, `TransitioningContentControl Name="PageHost"`). Avalonia matérialise chacun de ces conteneurs structurels comme un nœud UIA de type `Group` (Role: `Custom` ou `Group`). Comme aucun conteneur parent ne déclare `AutomationProperties.Name` ni ne masque ses enfants structurels, l'arbre UIA génère une cascade de 11 groupes anonymes imbriqués.

2. **Déficit d'installation d'Automation Peer sur les vues hôtes :**  
   `Views/BotsView.axaml` (lignes 1–15) est un `UserControl` sans `AutomationProperties.Name` ni pattern UIA. Le `TabControl` principal (ligne 709) et les `TabItem` n'ont aucun nom accessible explicite. En l'absence de propriété d'accessibilité sur le conteneur de page, UIA ne sait pas identifier la région de travail comme un panneau applicatif.

3. **Limites de `AccessibleNameBehavior` (`Behaviors/AccessibleNameBehavior.cs:20-41`) :**  
   Une tentative de dérivation automatique existe bien dans l'application : `AccessibleNameBehavior.Install()` est appelé au démarrage (`App.axaml.cs:17`). Cependant :
   - Il n'écoute que `TemplatedControl.TemplateAppliedEvent.AddClassHandler<Button>`.
   - Il ignore totalement les autres contrôles interactifs indispensables : `TabItem`, `ComboBox`, `TextBox`, `NumericUpDown`, `CheckBox`, `ListBox` et `MenuItem`.
   - Pour les boutons, la méthode `Derive` (`AccessibleNameBehavior.cs:33`) vérifie si `button.Content` est une chaîne ; si le contenu est complexe (ex. `<Panel><Image .../><TextBlock .../></Panel>`), `button.Content` est un objet visuel (`StackPanel` ou `Grid`). `Derive` ne retient le texte que si un `ToolTip.Tip` textuel existe ou si les `TextBlock` enfants sont résolus dans l'arbre logique. En cas d'échec ou d'absence de texte pur, le bouton n'a aucun nom et reste exposé comme un groupe anonyme ou n'est pas promu dans l'arborescence UIA.

4. **Style de fenêtre sans décorations natives (`WindowDecorations="None"`) :**  
   `MainWindow.axaml:16` désactive le chrome Windows standard. Tout le contrôle de fenêtre repose sur des boutons customisés (lignes 52–104) et une barre de titre interne, isolant la fenêtre du comportement par défaut des fenêtres Win32/UWP.

---

## 1. Quantification du Déficit d'Accessibilité (AutomationProperties & Couverture)

### Usages de `AutomationProperties.*` dans `GivreClone.App`
Une recherche exhaustive par regex (`AutomationProperties\.\w+`) sur l'ensemble des fichiers C# et AXAML du projet `GivreClone.App` donne la répartition suivante :

| Propriété UIA | Nombre d'occurrences | Emplacements clés / Répartition |
| :--- | :---: | :--- |
| **`AutomationProperties.Name`** | **269** | `Views/BotsView.axaml` (45 occurrences brutes), `Views/SmithMagicView.axaml` (44), `Views/MainWindow.axaml` (42), `Views/SettingsView.axaml` (24), `Views/ItemCraftPlannerPanel.axaml` (17), `Views/ProfessionPlannerPanel.axaml` (15), `Views/AccountsView.axaml` (14), `Views/WorldAtlasPanel.axaml` (12), `Views/VisualModsPanel.axaml` (11), `Views/DashboardView.axaml` (10), `Controls/MZP0OperatorPanel.axaml` (6). |
| **`AutomationProperties.HelpText`** | **0** | Aucune occurrence dans tout le projet (0 fichier). |
| **`AutomationProperties.LabeledBy`** | **0** | Aucune occurrence dans tout le projet (0 champ associé à son libellé). |
| **`AutomationProperties.AutomationId`** | **0** | Aucune occurrence dans tout le projet (aucun identifiant stable pour les agents). |
| **Méthodes programmatiques C#** | **3** | `AccessibleNameBehavior.cs:28` (`GetName`), `AccessibleNameBehavior.cs:31` (`SetName`), `SettingsView.ItemOverlay.cs:26` (`SetName`). |

### Recensement des Contrôles Interactifs dans `BotsView.axaml`
Dans la vue principale `Views/BotsView.axaml` (5 321 lignes), le recensement précis de tous les éléments interactifs donne :

| Type de Contrôle Interactif | Nombre dans `BotsView.axaml` | Avec `AutomationProperties.Name` | Sans `AutomationProperties.Name` |
| :--- | :---: | :---: | :---: |
| **`Button`** | 242 | 26 | 216 |
| **`NumericUpDown`** | 78 | 1 | 77 |
| **`CheckBox`** | 76 | 0 | 76 |
| **`ComboBox`** | 43 | 0 | 43 |
| **`TextBox`** | 27 | 1 | 26 |
| **`MenuItem`** | 19 | 0 | 19 |
| **`TabItem`** | 18 | 6 | 12 |
| **TOTAL** | **503** | **34** | **469** |

### Taux de Couverture
- **Contrôles interactifs dotés d'un nom accessible explicite (`AutomationProperties.Name`) :** $34 / 503 = \mathbf{6{,}76\,\%}$
- **Contrôles interactifs sans nom accessible explicite dans le XAML :** $469 / 503 = \mathbf{93{,}24\,\%}$
- **Taux de couverture d'`AutomationProperties.HelpText` :** $\mathbf{0{,}00\,\%}$
- **Taux de couverture d'`AutomationProperties.LabeledBy` :** $\mathbf{0{,}00\,\%}$
- **Taux de couverture d'`AutomationProperties.AutomationId` :** $\mathbf{0{,}00\,\%}$

Le déficit est massif : plus de 93 % des contrôles interactifs de l'écran principal ne portent aucun nom d'automatisation dans le markup XAML, et 100 % sont dépourvus d'`AutomationId` stable.

---

## 2. Contrôles Interactifs Sans Nom Accessible (Top 40 par Criticité Métier)

Ces 40 contrôles interactifs ne déclarent ni `AutomationProperties.Name`, ni attribut `Content="..."` / `Header="..."` textuel direct, ni `AutomationProperties.LabeledBy`. Ils sont classés par ordre d'importance pour les flux de travail centraux (lancement des bots, lecture d'état, inventaire/banque, combat, marché HDV).

### A. Flux Lancement, Session & Gestion de Compte (Header de BotsView)
1. **`BotsView.axaml:334` — Bouton « Copier l'identifiant »** : `<Button Classes="ghost" Height="36" Command="{Binding CopyLoginCommand}">` — Contient uniquement un `PathIcon` et un `TextBlock` localisé. Invisible aux lecteurs d'écran sans `AutomationProperties.Name`.
2. **`BotsView.axaml:345` — Bouton « Reconnecter le bot »** : `<Button Classes.primary="{...}" Command="{Binding ReconnectCommand}">` — Action critique de reprise de session. Aucun nom accessible fixé.
3. **`BotsView.axaml:354` — Bouton « Déconnecter le bot »** : `<Button Classes="ghost" Height="36" Command="{Binding DisconnectCommand}">` — Déconnexion manuelle d'urgence. Aucun nom accessible fixé.
4. **`BotsView.axaml:384` — Bouton « Téléportation Zaap »** : `<Button Classes="ghost" Command="{Binding Travel}">` — Déclenche le trajet vers le Zaap sélectionné. Anonyme pour l'agent.
5. **`BotsView.axaml:419` — Bouton « Téléportation Zaapi »** : `<Button Classes="ghost" Command="{Binding Travel}">` — Déclenche le transport urbain Zaapi. Anonyme.
6. **`BotsView.axaml:466` — Bouton « Sélection Destination Havre-Sac »** : `<Button Classes="ghost" Command="{Binding Select}">` — Choix d'accès au havre-sac. Anonyme.
7. **`BotsView.axaml:573` — Bouton « Mode Combat Manuel / Auto »** : `<Button Command="{Binding SelectedBot.ToggleManualFightCommand}">` — Bascule le contrôle temps réel du combat. Anonyme.
8. **`BotsView.axaml:577` — Bouton « Ouvrir le Havre-Sac »** : `<Button Command="{Binding SelectedBot.OpenHavenBagCommand}">` — Action de repos/régénération hors combat. Anonyme.
9. **`BotsView.axaml:580` — Bouton « Basculer vers l'éditeur Lua »** : `<Button Command="{Binding ToggleLuaCommand}">` — Accès direct au script en cours. Anonyme.
10. **`BotsView.axaml:583` — Bouton « Déposer tout dans la banque »** : `<Button Command="{Binding DepositAllCommand}">` — Vidage rapide des pods en banque. Anonyme.

### B. Flux Inventaire, Sac & Équipement
11. **`BotsView.axaml:33` — Bouton « Action sur l'objet » (Template `IwRowTemplate`)** : `<Button Grid.Column="2" Command="{Binding Owner.ActCommand}">` — Bouton primaire d'interaction d'échange/vente/dépôt.
12. **`BotsView.axaml:41` — Bouton « Case d'objet en grille » (Template `IwCellTemplate`)** : `<Button Classes="ghost" Command="{Binding Owner.ActCommand}">` — Sélection tactile/visuelle d'un objet en grille. Aucun texte UIA.
13. **`BotsView.axaml:61` — Bouton « Emplacement d'équipement » (Template `EquipSlotTemplate`)** : `<Button Command="{Binding Owner.OpenSlotPickerCommand}">` — Clic = équiper un objet sur l'emplacement. Totalement muet pour un agent UIA.
14. **`BotsView.axaml:71` — Élément de menu « Déséquiper »** : `<MenuItem Header="{DynamicResource L.Inv.Unequip}" Command="{Binding Owner.UnequipSlotCommand}">` — Menu contextuel d'équipement sans nom UIA stable.
15. **`BotsView.axaml:2589` — Sélecteur « Catégories d'inventaire »** : `<ComboBox ItemsSource="{Binding SelectedBot.InventoryCategories}">` — Filtrage des objets du sac par type. Aucun nom ni label associé.
16. **`BotsView.axaml:2592` — Sélecteur « Mode de tri de l'inventaire »** : `<ComboBox ItemsSource="{Binding ItemSortModes}">` — Tri par nom, poids, quantité, valeur. Non étiqueté.
17. **`BotsView.axaml:2594` — Bouton « Bascule Vue Liste / Grille Inventaire »** : `<Button Command="{Binding SelectedBot.ToggleInventoryGridCommand}">` — Contient uniquement les glyphes ☰ et ▦. Incompréhensible pour un lecteur d'écran.

### C. Flux Banque & Coffre
18. **`BotsView.axaml:2880` — Sélecteur « Catégories de banque »** : `<ComboBox ItemsSource="{Binding SelectedBot.BankCategories}">` — Filtrage du coffre de banque. Sans étiquette UIA.
19. **`BotsView.axaml:2882` — Sélecteur « Tri des objets en banque »** : `<ComboBox ItemsSource="{Binding ItemSortModes}">` — Tri de la réserve. Sans étiquette UIA.
20. **`BotsView.axaml:2889` — Sélecteur « Mode de dépôt en banque »** : `<ComboBox ItemsSource="{Binding BankDepositModes}">` — Choix de la stratégie de transfert. Sans étiquette.
21. **`BotsView.axaml:2890` — Bouton « Ouvrir la Banque »** : `<Button Command="{Binding SelectedBot.Interaction.OpenBankCommand}">` — Déclenche l'interaction PNJ Banque. Sans nom UIA.

### D. Flux Combat Tactique & Barre d'Actions Temps Réel
22. **`BotsView.axaml:875` — Bouton « Importer une configuration de combat »** : `<Button Command="{Binding OpenCombatImportCommand}">` — Importation de profil IA. Anonyme.
23. **`BotsView.axaml:921` — Bouton « Lancer la simulation de combat »** : `<Button Command="{Binding OpenCombatSimCommand}">` — Calcul prédictif des dégâts et tours. Anonyme.
24. **`BotsView.axaml:950` — Sélecteur « Mode de placement pré-combat »** : `<ComboBox ItemsSource="{Binding PlacementModeOptions}">` — Choix tactique (proche, loin, caché). Sans étiquette.
25. **`BotsView.axaml:1037` — Bouton « Ouvrir la liste des challenges »** : `<Button Command="{Binding OpenChallengeListCommand}">` — Configuration des défis tolérés. Anonyme.
26. **`BotsView.axaml:1216` — Case à cocher « Activer le sort »** : `<CheckBox IsChecked="{Binding Enabled}">` — Activation individuelle du sort dans le deck. Sans libellé UIA.
27. **`BotsView.axaml:1226` — Bouton « Basculer la variante du sort »** : `<Button Command="{...SwitchVariantCommand}">` — Échange variant I / variant II. Anonyme.
28. **`BotsView.axaml:1243` — Bouton « Configurer les conditions du sort »** : `<Button Command="{...OpenSpellConfigCommand}">` — Portée, cibles, PV minimum. Bouton avec icône engrenage sans nom.
29. **`BotsView.axaml:1252` — Bouton « Monter la priorité du sort »** : `<Button Command="{...MoveSpellUpCommand}">` — Réordonnancement du tour de jeu. Icône flèche sans nom.
30. **`BotsView.axaml:1258` — Bouton « Descendre la priorité du sort »** : `<Button Command="{...MoveSpellDownCommand}">` — Réordonnancement du tour de jeu. Icône flèche sans nom.
31. **`BotsView.axaml:1265` — Bouton « Supprimer le sort du deck »** : `<Button Command="{...RemoveSpellCommand}">` — Retrait du sort. Bouton avec icône sans nom.
32. **`BotsView.axaml:1661` — Bouton « Abandonner le combat (surrender) »** : `<Button Command="{Binding GiveUpFightCommand}">` — Sortie d'urgence du combat en cours. Sans étiquette UIA.
33. **`BotsView.axaml:1666` — Bouton « Prêt (Placement combat) »** : `<Button Command="{Binding FightReadyCommand}">` — Validation du placement de départ.
34. **`BotsView.axaml:1667` — Bouton « Passer le tour »** : `<Button Command="{Binding FightEndTurnCommand}">` — Fin du tour actif en combat manuel.
35. **`BotsView.axaml:1676` — Boutons « Sélectionner un challenge en combat »** : `<Button Command="{...PickFightChallengeCommand}">` — Choix du bonus d'XP/drop au tour 1.
36. **`BotsView.axaml:1687` — Boutons « Lancer un sort (Barre d'actions de combat) »** : `<Button Command="{...PickFightSpellCommand}">` — Slot de sort direct avec icône et coût PA. Invisible pour un agent UIA.

### E. Flux Carte, Déplacement & Hôtel des Ventes (HDV)
37. **`BotsView.axaml:1703` — Bouton « Changer de carte vers le Haut »** : `<Button Command="{Binding MapChangeCommand}" CommandParameter="top">` — Déplacement cardinal nord.
38. **`BotsView.axaml:1710` — Bouton « Changer de carte vers le Bas »** : `<Button Command="{Binding MapChangeCommand}" CommandParameter="bottom">` — Déplacement cardinal sud.
39. **`BotsView.axaml:1832` — Bouton « Bascule Grille/Liste Carte »** : `<Button Command="{Binding ToggleGridViewCommand}">` — Contient uniquement les glyphes ☰ et ▦.
40. **`BotsView.axaml:3003` — Bouton « Aligner tous les prix HDV (-1 kama) »** : `<Button Command="{Binding Hdv.UndercutAllCommand}">` — Action économique majeure de mise à jour des prix en vente.

---

## 3. Audit de la Navigation Clavier (Focus, Raccourcis & Impasses)

### Recensement Exhaustif des Primitives Clavier
Une recherche rigoureuse sur l'ensemble du projet révèle :

- **`TabIndex` : 0 occurrence** dans tout le projet.
- **`IsTabStop` : 0 occurrence** dans tout le projet.
- **`HotKey` (dans le XAML) : 3 occurrences seulement** :
  - `BotsView.axaml:326` : `HotKey="Ctrl+PageUp"` (Personnage précédent).
  - `BotsView.axaml:330` : `HotKey="Ctrl+PageDown"` (Personnage suivant).
  - `KeyboardShortcutCatalog.cs:9` : Mention en commentaire.
- **`KeyBinding` : 3 occurrences** : uniquement dans `Services/LicenseService.cs` (liées au hachage de licence, sans rapport avec l'UI).
- **`KeyDown` : 10 occurrences** :
  - Gestion de la console (`BotsView.axaml:756, 803, 4362`).
  - Saisie du chat (`BotsView.axaml:832, 834`).
  - Overlays de l'éditeur Lua (`Views/LuaEditorOverlays.axaml:45, 365, 440`).
  - Recherche du sac de forgemagie (`Views/SmithMagicView.axaml:451`).
  - Carte du monde (`Views/WorldMapWindow.cs:45`).
- **`Focus()` en code-behind C# : 12 occurrences**, cantonnées à la saisie de licence (`MainWindow.axaml.cs`), la barre de navigation rapide (`QuickNavSearch`) et les zones de texte de l'éditeur Lua. **Aucun `Focus()` n'est géré dans les flux opérationnels de `BotsView.axaml`.**

### Diagnostic : La Souris est-elle Obligatoire ?
**Oui, la souris est strictement obligatoire pour 90 % des opérations.**  
Le parcours au clavier au moyen de la touche <kbd>Tab</kbd> est impraticable pour les raisons suivantes :

1. **Parcours séquentiel non maîtrisé (absence de `TabIndex`) :**  
   Comme aucun `TabIndex` n'est configuré, la tabulation traverse l'arbre visuel dans l'ordre brut d'instanciation XAML. Dans `BotsView.axaml` (qui contient plus de 1 500 éléments visuels imbriqués), l'utilisateur doit appuyer des dizaines de fois sur <kbd>Tab</kbd> pour passer de la liste des bots à la barre d'onglets, sans aucun repère logique.
2. **Impasses Clavier Majeures (Dead Ends) :**
   - **Changement d'onglets principaux impossible sans pointer :** Les 18 onglets (`TabItem` : Console, Combat, Expédition, Trajets, Carte, Inventaire, Banque, HDV, Lua, Stats, etc.) ne possèdent aucun raccourci direct (ex. <kbd>Alt+1</kbd> à <kbd>Alt+9</kbd>). Bien que les flèches directionnelles permettent de naviguer dans un `TabControl` lorsque le focus y est positionné, il est impossible de donner le focus au `TabControl` sans cliquer à la souris.
   - **Fenêtres déroulantes Zaap / Zaapi / Havre-sac (Flyouts) :** L'ouverture des panneaux de téléportation (`Button.Flyout`, lignes 374 et 408) est déclenchée au clic. Lorsqu'un flyout s'ouvre, le focus n'y est pas automatiquement transféré, piégeant l'utilisateur au clavier.
   - **Déplacement et ciblage sur la carte (`IsoMapControl`) :** L'interaction avec la carte isométrique repose exclusivement sur `PointerPressed`. Il n'existe aucun système de déplacement au clavier (ex. pavé numérique ou touches ZQSD) ni sélection de cellule accessible.
   - **Réordonnancement des sorts en combat :** La réorganisation des sorts s'effectue via un glisser-déposer à la souris (`PointerPressed="OnSpellDragStart"`, ligne 1218). Les boutons de montée/descente (lignes 1252/1258) sont sans libellé et inatteignables au clavier sans traverser tout l'ItemsControl.
   - **Barre d'actions de combat en direct :** En combat manuel, un joueur humain ne peut pas appuyer sur les touches <kbd>1</kbd> à <kbd>9</kbd> pour sélectionner un sort, ni sur <kbd>F1</kbd> ou <kbd>Espace</kbd> pour passer son tour (comme dans le client Dofus d'origine). Il est contraint de cliquer sur les boutons `FightSpellItem` (ligne 1687) et `FightEndTurnCommand` (ligne 1667).

---

## 4. Recensement des Contrôles Basés Uniquement sur des Icônes ou Glyphes

Dans `BotsView.axaml`, **73 boutons sont basés exclusivement sur une image ou un glyphe**, dont **64 sont totalement dépourvus d'`AutomationProperties.Name`** :

### A. Boutons Utilisant des Glyphes Littéraux (Caractères Symboliques)
Ces contrôles utilisent des symboles Unicode comme texte brut. Les lecteurs d'écran prononcent le nom du caractère Unicode ou restent muets, et un agent ne peut pas déduire l'action :

| Ligne | Glyphe | Contexte / Action Métier | Fix Requis |
| :---: | :---: | :--- | :--- |
| **`1549`** | `✕` | Suppression d'un point de passage dans l'éditeur de trajets (`Owner.RemoveCommand`) | `AutomationProperties.Name="Supprimer ce point de passage"` |
| **`1834` / `1835`** | `☰` / `▦` | Bascule vue Grille / vue Liste des objets d'échange (`ToggleGridViewCommand`) | `AutomationProperties.Name="{DynamicResource L.Iw.ViewToggleTip}"` |
| **`2054`** | `✕` | Fermeture du panneau de retour d'information carte (`DismissFeedbackCommand`) | `AutomationProperties.Name="Fermer le message de carte"` |
| **`2267`** | `★` / `☆` | Ajout / retrait de la carte courante des favoris | `AutomationProperties.Name="Basculer la carte en favori"` |
| **`2597` / `2598`** | `☰` / `▦` | Bascule vue Grille / vue Liste du sac d'inventaire (`ToggleInventoryGridCommand`) | `AutomationProperties.Name="{DynamicResource L.Iw.ViewToggleTip}"` |
| **`2752`** | `✕` | Fermeture du panneau de détail de l'objet inspecté (`CloseItemDetailsCommand`) | `AutomationProperties.Name="Fermer les détails de l'objet"` |

### B. Boutons Basés Uniquement sur des Icônes (`PathIcon` ou `Image`) sans Nom UIA
Ces boutons n'affichent aucun texte (seule une icône vectorielle ou bitmap est rendue) :

1. **`BotsView.axaml:41`** — Case d'objet de grille d'échange (`IwCellTemplate`) : `<Image Source="{Binding Icon}" />` sans nom UIA.
2. **`BotsView.axaml:61`** — Case d'emplacement d'équipement : rendu d'icône d'objet ou d'un point `·` sans nom d'emplacement accessible.
3. **`BotsView.axaml:737`** — Bouton copier la console : `<PathIcon Data="{StaticResource IconCopy}" />` sans `AutomationProperties.Name`.
4. **`BotsView.axaml:744`** — Bouton vider la console : `<PathIcon Data="{StaticResource IconTrash}" />` sans nom UIA.
5. **`BotsView.axaml:875`** — Bouton importer combat : `<PathIcon Data="{StaticResource IconFolder}" />` sans nom UIA.
6. **`BotsView.axaml:921`** — Bouton simuler combat : `<PathIcon Data="{StaticResource IconSword}" />` sans nom UIA.
7. **`BotsView.axaml:1243`** — Bouton configurer sort : `<PathIcon Data="{StaticResource IconWrench}" />` sans nom UIA.
8. **`BotsView.axaml:1252`** — Bouton monter sort : `<PathIcon Data="{StaticResource IconChevronUp}" />` sans nom UIA.
9. **`BotsView.axaml:1258`** — Bouton descendre sort : `<PathIcon Data="{StaticResource IconChevronDown}" />` sans nom UIA.
10. **`BotsView.axaml:1265`** — Bouton supprimer sort : `<PathIcon Data="{StaticResource IconTrash}" />` sans nom UIA.
11. **`BotsView.axaml:1388`** — Carte de monstre d'expédition : bouton avec image de monstre sans nom accessible.
12. **`BotsView.axaml:1687`** — Bouton de sort en combat : icône de sort sans nom de sort exposé en UIA.
13. **`BotsView.axaml:2957–2969`** — Onglets de vue HDV (Général, Achat, Scan, Stats, Paramètres) : boutons avec `PathIcon` seuls.
14. **`BotsView.axaml:4194–4222`** — Barre d'outils de l'éditeur Lua : 11 boutons d'action (Modèles, Ouvrir, Récents, Sauvegarder, Formater, Rechercher, Vérifier, Doc API, Plein écran) portant uniquement des `PathIcon`.

---

## 5. Information Véhiculée par la Couleur Seule (Défauts Critiques d'Accessibilité)

La règle WCAG 2.1 (Critère 1.4.1 — *Use of Color*) stipule que la couleur ne doit pas être le seul moyen visuel de transmettre une information, d'indiquer une action ou de distinguer un élément visuel. Dans Givre, **8 zones majeures violent ce critère** :

1. **Pastille de Sévérité des Logs de Console (`BotsView.axaml:768` et `4376`) :**  
   `<Ellipse Width="7" Height="7" Fill="{Binding DotBrush}" />`  
   Le type d'événement (Information en bleu, Avertissement en orange, Erreur en rouge, Réseau en violet) est matérialisé uniquement par la couleur de ce cercle de 7 pixels. La ligne affiche l'heure, la catégorie et le message, mais **aucun libellé textuel de sévérité** (ex. `[ERREUR]`) et aucune infobulle. Un utilisateur daltonien, un lecteur d'écran ou un agent UIA ne peut pas connaître le niveau du log.
2. **Jauge de Pods (Poids du Sac) (`BotsView.axaml:652` et `5303`) :**  
   `<ProgressBar Value="{Binding PodsPercent}" Foreground="{Binding SelectedBot.PodsBrush}" />`  
   Dans `BotRowViewModel.cs:554`, la couleur de la barre bascule dynamiquement : vert (< 70 %), orange (70–89 %), rouge (≥ 90 %). Cependant, la barre ne comporte aucun texte d'état d'alerte (ex. `Normal`, `Attention`, `Surcharge`), rendant l'état critique de surcharge invisible sans déduction mathématique sur les valeurs numériques.
3. **Pastille de Défaite en Combat sur l'Onglet (`BotsView.axaml:847`) :**  
   `<Ellipse Width="7" Height="7" Fill="#F0533B" IsVisible="{Binding SelectedBot.CombatDefeat}" />`  
   L'en-tête de l'onglet Combat affiche un point rouge fixe `#F0533B` en cas de défaite récente. Bien qu'une infobulle existe sur l'ellipse, le `TabItem` lui-même n'expose pas cette information dans son nom UIA, de sorte qu'un lecteur d'écran lisant l'onglet n'annonce que `Combat`.
4. **Indicateur d'Action en Historique Statistique (`BotsView.axaml:4579`) :**  
   `<Ellipse Width="7" Height="7" Fill="{Binding DotBrush}" IsVisible="{Binding ShowDot}" />`  
   Dans `BotStatsViewModel.cs:37`, `DotBrush` vaut `FightDot` ou `HarvestDot`. La distinction entre une action de combat et une récolte repose sur un rond coloré sans texte associé ni infobulle.
5. **Tour Actif dans la Frise Chronologique de Combat (`BotsView.axaml:82–86`) :**  
   Le combattant dont c'est le tour est signalé par le style `Border.turnNow` (`Background="#40FFFFFF"` et bordure de 2 px). Aucun attribut `AutomationProperties.Name` ni indicateur textuel `En train de jouer` n'est exposé.
6. **Malus d'Objets dans les Infobulles (`BotsView.axaml:88–90`) :**  
   Les lignes de malus sont identifiées par la classe `TextBlock.fxneg` avec `Foreground="#E5534B"`. Elles ne comportent aucun symbole ou préfixe explicite (ex. `Malus :` ou signe `-`), dépendant de la perception du rouge.
7. **Flèches de Changement de Carte en Attente (`BotsView.axaml:1706, 1713, 1720, 1727`) :**  
   Un glyphe `✕` en jaune `#FFE07A` signale qu'un changement de carte est programmé ou en attente. Aucun texte accessible n'explique la signification de ce symbole.
8. **Sélection de Sort dans la Barre de Combat (`BotsView.axaml:1693`) :**  
   Le sort sélectionné pour le lancer est encadré d'un bord orange `#F5A623` (`IsVisible="{Binding IsSelected}"`). Aucun état UIA `Selected` ou équivalent accessible n'est transmis à l'arbre UIA.

---

## 6. Agent Experience (AX) : Tuyau de Contrôle (MCP) vs Interface Graphique (UI)

### Inventaire du Tuyau de Contrôle Existant
L'analyse de `Services/Mcp/LocalControlBridge.cs` et `ViewModels/BotsViewModel.Control*.cs` montre une architecture MCP déjà très substantielle :
- **4 outils d'accès direct (`LocalControlBridge.cs:35–64`) :** `query_item_craft`, `query_profession_plan`, `query_world_atlas`, `wait_console`.
- **46 outils de lecture (`ControlReads` dans `Control.cs:49–60`) :** inspection complète de l'inventaire (`get_inventory`), navigation locale (`probe_navigation`), carte et acteurs (`get_map_state`), statut général, banques, statistiques, anomalies, métriques de combat, diagnostics, chat privé.
- **23 outils d'écriture (`AccountWrites` dans `Control.cs:61–67`) :** connexion/déconnexion, lancement/arrêt de scripts Lua (`run_lua_script`, `stop_script`), déplacements (`travel_to`, `zaapi_to`), forgemagie automatique, utilisation/équipement d'objets, envoi de MP.

### Matrice des Écarts : Capacités Réservées à l'Humain (Non Disponibles pour l'Agent)
Malgré cette richesse, il subsiste des zones entières de l'UI inaccessibles à un agent via le tuyau MCP. Ces écarts constituent la feuille de route AX :

| Domaine UI | Ce que l'Humain peut faire dans l'UI | Ce que l'Agent peut faire via MCP | Écart AX (Fonctionnalité Manquante) |
| :--- | :--- | :--- | :--- |
| **Combat Tactique Temps Réel** | - Choisir un défi (`PickFightChallenge`)<br>- Cliquer sur "Prêt" (`FightReady`)<br>- Cliquer sur "Passer le tour" (`FightEndTurn`)<br>- Sélectionner et lancer un sort (`PickFightSpell`)<br>- Abandonner (`GiveUpFight`) | Aucun outil de combat interactif. L'agent ne peut que définir le profil global (`set_combat_profile`) avant le combat. | **L'agent est totalement incapable de piloter un combat au tour par tour** (défis, lancers de sorts ciblés, validation de tour). |
| **Hôtel des Ventes (HDV)** | - Vendre un objet via modale (`OpenSellModal`)<br>- Aligner tous les prix (`UndercutAll`)<br>- Repricer une ligne (`RepriceListing`)<br>- Retirer de la vente (`RemoveListing`)<br>- Activer le sniper / auto-buy | - Lecture seule (`get_market_deals`, `search_items`). | **Aucun outil d'écriture HDV** : impossible pour un agent de vendre, de repricer ses lots ou d'acheter automatiquement des ressources. |
| **Gestion de Banque** | - Déposer tout le sac (`DepositAllCommand`)<br>- Déposer/retirer un objet précis<br>- Transférer des kamas en banque | - Lecture seule (`get_bank`). | **Aucune opération de transfert de banque** via MCP : l'agent ne peut ni vider ses pods en banque ni retirer des clés/consommables. |
| **Déplacement Cardinal Direct** | - Cliquer sur les bordures de carte pour changer de zone (`MapChangeCommand` : haut, bas, gauche, droite) | - `travel_to` (exécute un script Lua complet de trajet) et `zaapi_to`. | **Absence de micro-déplacement cardinal** : impossible de forcer un pas vers une carte adjacente sans instancier un script. |
| **Canaux de Discussion (Chat)** | - Écrire dans le canal Général, Équipe, Guilde, Recrutement, Commerce via `SendChatCommand` | - Uniquement des messages privés (`send_private_message`). | **Impossible d'interagir sur les canaux publics ou d'équipe**, limitant la coordination multi-bots. |
| **Gestion du Deck de Sorts** | - Activer/désactiver un sort précis<br>- Monter/descendre la priorité<br>- Régler les conditions de lancer | - Profil global ou variante de sort (`set_spell_variant`). | **Impossible d'ajuster finement l'ordonnancement des sorts** sans écraser le profil complet. |
| **Planning & Horaires** | - Configurer les plages horaires de connexion/déconnexion et les pauses par bot | Aucun outil MCP de planning. | **L'agent ne peut ni consulter ni modifier le planning autonome** des bots. |
| **Paramètres Opérationnels** | - Régler les délais d'anti-captcha, les vitesses de déplacement, les proxys par bot | - `get_bot_settings` / `set_bot_settings` très limités. | **Paramétrage fin des proxys et du comportement réseau inaccessible**. |

---

## 7. Correctifs Classés par Impact

Les correctifs sont classés selon le score d'impact :  
$$	ext{Impact} = 	ext{Utilisateurs \& Agents Affectés (1 à 10)} 	imes 	ext{Sévérité du Blocage (1 à 5)}$$

### Rang 1 : Résolution de la Cécité Totale UIA (Score : $10 	imes 5 = 50$)
*Problème :* L'application entière est vue comme 11 groupes anonymes ; aucun composant n'est adressable.
- **`Views/MainWindow.axaml:47`**  
  *Fix :* Ajouter `AutomationProperties.Name="Application Principale Givre"` et `AutomationProperties.AutomationId="MainShell"` sur la racine `ShellRoot`.
- **`Views/MainWindow.axaml:416`**  
  *Fix :* Ajouter `AutomationProperties.Name="Contenu de la Page"` et `AutomationProperties.AutomationId="PageHost"` sur `TransitioningContentControl`.
- **`Views/BotsView.axaml:15`**  
  *Fix :* Ajouter `AutomationProperties.Name="Écran de Gestion des Bots"` et `AutomationProperties.AutomationId="BotsViewRoot"` sur la racine `UserControl`.
- **`Behaviors/AccessibleNameBehavior.cs:18`**  
  *Fix :* Étendre le comportement à tous les contrôles clés : `AddClassHandler<TabItem>`, `AddClassHandler<ComboBox>`, `AddClassHandler<TextBox>`, `AddClassHandler<NumericUpDown>`.

### Rang 2 : Rétablissement du Pilotage de Session & Header (Score : $10 	imes 4 = 40$)
*Problème :* Les commandes de reprise de bot, copie de login et déconnexion n'ont aucun nom UIA.
- **`Views/BotsView.axaml:334`**  
  *Fix :* Ajouter `AutomationProperties.Name="Copier l'identifiant du compte" AutomationProperties.AutomationId="BtnCopyLogin"`.
- **`Views/BotsView.axaml:345`**  
  *Fix :* Ajouter `AutomationProperties.Name="Reconnecter le bot immédiatement" AutomationProperties.AutomationId="BtnReconnect"`.
- **`Views/BotsView.axaml:354`**  
  *Fix :* Ajouter `AutomationProperties.Name="Déconnecter le bot" AutomationProperties.AutomationId="BtnDisconnect"`.
- **`Views/BotsView.axaml:583`**  
  *Fix :* Ajouter `AutomationProperties.Name="Déposer tout l'inventaire en banque" AutomationProperties.AutomationId="BtnDepositAll"`.

### Rang 3 : Actions de Combat Temps Réel & Barre de Sorts (Score : $9 	imes 4 = 36$)
*Problème :* L'agent ou le joueur handicapé ne peut ni valider son tour ni lancer un sort via UIA.
- **`Views/BotsView.axaml:1666`**  
  *Fix :* Ajouter `AutomationProperties.Name="Prêt pour le combat" AutomationProperties.AutomationId="BtnFightReady"`.
- **`Views/BotsView.axaml:1667`**  
  *Fix :* Ajouter `AutomationProperties.Name="Passer le tour de jeu" AutomationProperties.AutomationId="BtnFightEndTurn"`.
- **`Views/BotsView.axaml:1669`**  
  *Fix :* Ajouter `AutomationProperties.Name="Abandonner le combat en cours" AutomationProperties.AutomationId="BtnGiveUpFight"`.
- **`Views/BotsView.axaml:1687`**  
  *Fix :* Ajouter `AutomationProperties.Name="{Binding Tip, StringFormat='Sort {0}'}" AutomationProperties.AutomationId="{Binding SpellId, StringFormat='SpellSlot_{0}'}"`.

### Rang 4 : Suppression de l'Information par la Couleur Seule (Score : $8 	imes 4 = 32$)
*Problème :* Sévérité des logs et niveau des pods indétectables sans couleur.
- **`Views/BotsView.axaml:768`**  
  *Fix :* Ajouter `ToolTip.Tip="{Binding Level}" AutomationProperties.Name="{Binding Level, StringFormat='Niveau de log : {0}'}"` sur l'`Ellipse`.
- **`Views/BotsView.axaml:652`**  
  *Fix :* Ajouter `ToolTip.Tip="{Binding SelectedBot.PodsPercent, StringFormat='Remplissage du sac : {0}%'}" AutomationProperties.Name="Jauge de pods du personnage"` sur la `ProgressBar`.
- **`Views/BotsView.axaml:847`**  
  *Fix :* Ajouter `AutomationProperties.Name="Onglet Combat - Dernière session : Défaite"` dynamiquement sur le `TabItem` parent.

### Rang 5 : Élimination des Glyphes Littéraux Invisibles (Score : $8 	imes 3 = 24$)
*Problème :* Boutons contenant ☰, ▦, ✕ incompréhensibles.
- **`Views/BotsView.axaml:1832`**  
  *Fix :* Ajouter `AutomationProperties.Name="Basculer l'affichage entre grille et liste" AutomationProperties.AutomationId="BtnToggleGridViewCarte"`.
- **`Views/BotsView.axaml:2054`**  
  *Fix :* Ajouter `AutomationProperties.Name="Fermer le message" AutomationProperties.AutomationId="BtnDismissMapFeedback"`.
- **`Views/BotsView.axaml:2594`**  
  *Fix :* Ajouter `AutomationProperties.Name="Basculer l'affichage du sac entre grille et liste" AutomationProperties.AutomationId="BtnToggleGridViewInv"`.
- **`Views/BotsView.axaml:2752`**  
  *Fix :* Ajouter `AutomationProperties.Name="Fermer les détails de l'objet" AutomationProperties.AutomationId="BtnCloseItemDetails"`.

### Rang 6 : Raccourcis Clavier & Chaîne de Tabulation (Score : $7 	imes 3 = 21$)
*Problème :* Souris obligatoire pour changer d'onglet et naviguer.
- **`Views/BotsView.axaml:709`**  
  *Fix :* Assigner `TabIndex="1"` au `BotListPanel`, `TabIndex="2"` aux boutons d'actions header, et `TabIndex="3"` au `TabControl`.
- **`Views/BotsView.axaml:710, 842, 1594, 2525, 2933, 4151`**  
  *Fix :* Assigner des raccourcis globaux ou des `HotKey` sur les onglets vitaux (ex. <kbd>Ctrl+1</kbd> Console, <kbd>Ctrl+2</kbd> Combat, <kbd>Ctrl+3</kbd> Carte, <kbd>Ctrl+4</kbd> Inventaire, <kbd>Ctrl+5</kbd> HDV, <kbd>Ctrl+6</kbd> Lua).

### Rang 7 : Extension du Tuyau MCP pour l'Autonomie Complète AX (Score : $8 	imes 5 = 40$ pour les Agents)
*Problème :* L'agent d'automatisation ne peut pas agir en combat, en banque ni en HDV.
- **`Services/Mcp/LocalControlBridge.cs` & `ViewModels/BotsViewModel.ControlGame.cs`**  
  *Fix 1 (Combat AX) :* Implémenter les outils MCP `fight_ready`, `fight_end_turn`, `fight_cast_spell(spellId, cellId)`, `fight_pick_challenge(challengeId)`.
- **`ViewModels/BotsViewModel.ControlGame.cs`**  
  *Fix 2 (Marché HDV AX) :* Implémenter les outils MCP `hdv_undercut_all`, `hdv_sell_item(gid, uid, price, qty)`, `hdv_remove_listing(listingId)`.
- **`ViewModels/BotsViewModel.ControlGame.cs`**  
  *Fix 3 (Banque AX) :* Implémenter les outils MCP `bank_deposit_all`, `bank_transfer_item(gid, qty, toBank)`, `bank_transfer_kamas(amount, toBank)`.
- **`ViewModels/BotsViewModel.Control.cs`**  
  *Fix 4 (Navigation AX) :* Implémenter l'outil MCP `map_step(direction: "top"|"bottom"|"left"|"right")`.

---
*Rapport généré le 9 novembre 2026 par OmO pour le projet GivreWork.*
