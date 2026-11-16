# Index global des layouts UI Dofus 3 — 30 octobre 2026

## Résultat et frontière de preuve

**960 chemins complets distincts observés : 685 UXML, 242 USS, 33 TSS, répartis dans 50 familles de dossiers.** Le catalogue principal fournit en plus 51 noms distincts dont le chemin complet n’a pas été résolu. Tous ces chemins et noms sont listés ci-dessous, sans échantillonnage. Cent objets UXML représentatifs ont été inspectés par leurs chaînes sérialisées délimitées ; les autres contenus restent explicitement non inspectés.

Ce document complète `DOFUS3_UI_REFERENCE_20261030.md`, qui conserve les couleurs pipetées sur la capture et la référence de forgemagie. Le présent inventaire ne réduit pas l’UI de Dofus à la FM : stockage, HDV, banque, métiers, combat, quêtes, chasses, dialogues, social, options, HUD, cartes et comptes sont couverts.

« Exhaustif » signifie ici exhaustif des chemins UXML/USS/TSS ASCII récupérés par préfixes de longueur Unity dans les bundles identifiés et des noms récupérés dans le catalogue principal, **pas** de tous les écrans que le jeu pourrait produire à l’exécution. Des ressources intégrées ailleurs, construites en code ou adressées par un nom non récupéré restent possibles. Un chemin d’asset n’est pas une preuve qu’un écran est actif dans cette version.

Aucun jeu ni application cible n’a été lancé. Aucun fichier du jeu, sprite, police ou fichier de production n’a été modifié ou exporté. Seul ce document est ajouté pour cette extension de recherche.

## Sources et vérification de l’inventaire

Installation : `%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3/`. Version installée observée dans `Dofus_Data/StreamingAssets/version` : `3.7.12.12`, build `2026-10-19T15:11:45+00:00`.

Sous `Dofus_Data/StreamingAssets/aa/StandaloneWindows64/` :

| Conteneur | Chemins UI complets retrouvés | SHA-256 du fichier compressé |
| --- | ---: | --- |
| `uielements_assets_all.bundle` | 916 : 674 UXML, 242 USS | `19f4352865203d3b8ba77dcde731ab01a71d1a1ec5a171f47b5518b3f1bbd4ac` |
| `uiassets_assets_.bundle` | 11 UXML supplémentaires, dont buffs, lobbies et tooltips | `ac75954fd3782a1a5d28e27df5e76e3c198acc470e95c0c8f3d9f2771e2d580d` |
| `tss_assets_all.bundle` | 33 TSS | `5832c4dc7f506046a93f7b760c4aca72ad9f0a2681c19546beefeba4226a0c57` |

Autres bundles déjà lus en mémoire et sans chemin UXML/USS/TSS supplémentaire récupéré par cette méthode : `uidarkstone_assets_all.bundle`, `figs_assets_.bundle`, `icons_assets_.bundle` et `Content/Picto/UI/icon_assets_all.bundle`. Cela ne prétend pas qu’ils ne contiennent aucune donnée de style : darkStone contient notamment des textures.

Catalogue principal : `Dofus_Data/StreamingAssets/aa/catalog.bin`, 909 718 octets. Extraction de 1008 noms distincts terminés par `.uxml`, `.uss` ou `.tss` ; 957 ont un nom de fichier correspondant dans l’index complet, 51 restent non résolus. Une correspondance de nom n’est pas une résolution de référence ni une preuve d’identité quand plusieurs dossiers utilisent le même nom.

Méthode : réutilisation des bundles UnityFS version 8 décompressés uniquement en mémoire ; vérification de la taille annoncée de chaque bloc LZ4 ; validation des chemins par leur longueur sérialisée little-endian, et non par la présence d’un séparateur imprimable après l’extension. Le second point évite d’omettre un chemin suivi immédiatement d’un octet binaire qui ressemble à une lettre.

La table des 2570 objets du fichier sérialisé principal, format Unity 22, a été décodée pour obtenir les bornes des objets. Les entrées de conteneur ont ensuite été reliées à leurs path IDs : 914 des 916 chemins du bundle principal ont une référence locale valide ; deux chemins sont observés seulement comme références internes. Dans `uiassets_assets_.bundle`, les 11 objets UXML supplémentaires ont été résolus et inspectés. Les cent extraits ci-dessous proviennent donc d’objets délimités, pas de voisinages de chaînes choisis arbitrairement.

Cette inspection ne reconstitue pas le DOM/UXML original, la hiérarchie complète, le flex calculé, les positions, les liaisons de contrôleur ou les styles à l’exécution. Les chaînes de templates indiquent une réutilisation probable ; elles ne prouvent pas seules l’arbre final. Les 33 TSS ont été inventoriés, pas intégralement interprétés.

La recherche REA IL2CPP de la note précédente reste la preuve séparée de métadonnées : `ev_1852466356c1a8076b91ae2577fee6949a6ecc88cfa144a227f0feb5e2fe6044`, version 39. L’index des bundles ici est une observation locale ordinaire, pas une Evidence REA inventée.

## Traduction concrète vers les vues bot

Les noms de vues ci-dessous ont été relevés dans `GivreClone.App/Views/`. La correspondance est une recommandation de design ; elle n’affirme pas que les comportements du jeu existent déjà dans ces vues.

| Besoin bot | Structures observées à réutiliser | Application proposée |
| --- | --- | --- |
| Comptes et sessions | Auth : `gd_character`, `ctr_serverSelection`, `ctr_filter`, `btn_play` ; PlayerInfo : en-tête et onglets | `AccountsView`, `AccountOverlays`, `MyAccountView` : liste de comptes dense, fiche sélectionnée à droite, action de session stable en pied. |
| Liste de bots et tableau de bord | PersistentMenu : widgets pliables ; Ladder : recherche et colonnes ; Fight : compteurs | `BotsView`, `DashboardView` : tableau central avec statut, activité et compteurs ; pas une collection de grandes cartes identiques. |
| Inventaire et banque | `storage` réutilisé par `storageBank`, `storageExchange`, `equipmentStorageUi` ; filtres catégorie/tri et monnaie | Réutiliser la même grille et le même détail d’objet dans les outils économiques ; distinguer origine/destination et capacité, pas trois designs concurrents. |
| HDV achat | `AuctionHouseItemsList` : favoris, inventaire/banque/havre-sac, prix moyen ; `AuctionHouseItemDetailBuy` : détail, quantité, prix, achat | `MarketInsightsPanel`, `EcoToolsPanel` : recherche et résultats à gauche, détail chiffré à droite, prix et quantités alignés. |
| HDV vente | `auctionHouseSell` : tables vente/autres objets, durée, pile, prix ; popup : taxes et total | `HdvSellOverlay`, `HdvUidOperatorWindow` : table dense, formulaire latéral, récapitulatif taxe/total avant confirmation. |
| Historique économique | `salesHistory` : période de/à, vendus/invendus, total kamas | `MarketInsightsPanel`, `EcoToolsPanel` : filtres temporels en ligne et total synthétique ancré au tableau. |
| Métiers et fabrication | `jobUI` référence `jobCraftTab`/`jobProgressTab` ; `CraftUI` : recette, inventaire, résultat et paiement | `ProfessionPlannerPanel`, `ItemCraftPlannerPanel` : recette et manquants côte à côte ; quantité et coût dans le pied, résultat distinct. |
| Forgemagie | Statistiques compactes, historique, reliquat et runes simple/Pa/Ra | `SmithMagicView` : conserver la table de la référence FM, sans imposer son historique à toutes les autres vues. |
| Combat et simulation | `FightTimeline`, `EndTurnWidget`, résultat par onglets personnage/résumé/statistiques ; `buffSummary` sépare bonus/malus/états | `CombatOverlays`, `CombatSimOverlay` : centre réservé à la simulation, acteurs en liste, configuration à côté, résultat tabulé. |
| Quêtes et chasses | Journal de quêtes avec objectifs/récompenses ; chasse avec étape et essais restants | `TreasureView`, `NoCodeWizardOverlay` : étapes verticales, objectif courant bien visible, validation et progression groupées. |
| Équipes | `partyMemberItem` : chef, suivi, attente ; social : liste/détail ; groupes : types d’activité | `TeamsView` : membres en rangées, chef et rôles visibles, panneau de composition ; état textuel en plus de la couleur. |
| Carte et trajets | Cartography : recherche, filtres, zoom, secteurs ; Taxi : destination, coordonnées et coût | `WorldAtlasPanel`, `RoadRecorderOverlays` : carte principale, liste de trajets latérale, barre d’outils compacte. |
| Réglages, proxys et adaptateurs | `OptionBase` : onglets/contenu ; contrôles booléen, slider, thème ; sélecteur admin filtrable | `SettingsView`, `ProxysView`, `AdaptersView` : rangées homogènes, description secondaire, état et action alignés. |
| Planning | Calendar : jours, événements et filtres | `PlanningEditor`, `PlanningOverlays`, `PlanningDisconnectOverlay` : grille temporelle et légende stable ; édition dans un panneau, pas un écran de formulaires dispersés. |
| Éditeur Lua et assistant | Common : popup contenu/boutons ; Guidebook : recherche et progression | `LuaEditorOverlays`, `NoCodeWizardOverlay` : même chrome et même hiérarchie d’actions ; préserver la surface utile du code. |
| Navigation globale | `bannerMenu` : accès principaux/additionnels ; `actionBar` : pages ; widgets min/max | `MainWindow`, `ClassicShellBody` : navigation stable, contexte de compte visible et actions locales dans l’en-tête du module. |

## Direction visuelle commune, sans réduire tous les écrans à la FM

Les couleurs suivantes sont des mesures de la capture fournie, pas des valeurs USS calculées : bandeau `#3F3F58`, panneau `#33354C`, ligne `#2A2D4D`, sélection `#44486E`, en-tête de tableau `#19192D`, case sombre `#100F19`, bouton lime `#89A200`. Leur source et les coordonnées sont détaillées dans la note FM.

Les classes sémantiques retrouvées (`backgroundColor_background_medium90`, `textColor_white_white80`, `textColor_highlight_secondary100`) et les variables TSS (`--background_medium100`, `--highlight_primary100`, `--highlight_secondary100`) appuient l’usage de jetons partagés, mais leurs valeurs originales n’ont pas été décodées.

Reconstruction recommandée :

- Un chrome commun avec en-tête ardoise violet, surface de contenu plus sombre et bord fin ; conserver les exceptions utiles aux cartes, au code et à la simulation.
- Une action primaire lime par contexte de travail ; secondaire neutre, destructive distincte. La sélection d’une ligne reste violette et ne concurrence pas l’action primaire.
- Trois patrons réellement différents : liste/détail pour comptes et objets ; table/formulaire/récapitulatif pour économie ; grande surface centrale/inspecteur pour carte, code et combat.
- Typographie régulière pour les données, titres courts légèrement plus forts ; chiffres alignés et unités visibles. Les tailles ci-après sont des choix bot, pas des métriques natives : base 12–13 px, titre 15–17 px, rangée 28–32 px.
- Espacement bot proposé : 4 px entre éléments d’une même rangée, 8 px entre groupes, 12–16 px entre panneaux. Éviter les marges géantes et les cartes très arrondies qui diluent les tables.
- Icônes de fonction sobres, monochromes ou peu colorées ; réserver les petites couleurs vives aux caractéristiques, ressources et états. Pas d’emoji comme substituts aux sprites.
- Pour les états vide/chargement/erreur, garder le panneau, ses filtres et son titre en place. Les objets `ctr_noItem`, `lbl_noResult`, `ctr_error` et les descriptions d’options fournissent des pistes concrètes, pas un comportement asynchrone observé.
- Ne pas recopier les guirlandes et ornements partout. Les assets darkStone et Figma coexistent ; la capture ne démontre ni le thème actif ni une obligation de reproduire chaque décoration.

Aucun sprite ou fichier de police n’est livré par cet index. Les chemins de textures de la note précédente sont des pistes d’extraction ultérieure ; ne pas prétendre qu’une icône redessinée est le sprite original.

## Légende de l’index

- `I` : objet UXML résolu et chaînes de cet objet inspectées ; extrait représentatif ci-dessous. Ce statut n’est pas une reconstruction visuelle ou sémantique complète.
- `P` : chemin complet présent, contenu individuel non inspecté. Une référence de conteneur est validée pour les UXML/USS du bundle principal, sauf les deux `R`. Les TSS restent des chemins inventoriés sans résolution d’objet détaillée dans cette passe.
- `R` : chemin complet observé comme référence, mais pas d’objet de conteneur local résolu ; contenu non inspecté.
- Les `C` de la dernière section sont des noms de catalogue sans chemin complet résolu.

Tous les chemins sont internes aux bundles, pas des fichiers UXML/USS/TSS autonomes copiables sur disque. Pour les extraits, les identifiants sont listés sans prétendre établir leur ordre visuel. Un extrait sans identifiant de contrôle conserve des noms de templates/types pour éviter de transformer une coquille en formulaire inventé.

Empreinte SHA-256 de la liste triée des 960 chemins, UTF-8 avec un LF après chaque chemin : `386d2af43794d53efc793400cff93f6a4982b947ddc78de84b127a6bc83da4ac`.

## Comptes par famille

| Famille de dossier | UXML | USS | TSS | Objets inspectés |
| --- | ---: | ---: | ---: | ---: |
| `Assets/UI/Admin` | 2 | 1 | 0 | 1 |
| `Assets/UI/Alterations` | 4 | 2 | 0 | 1 |
| `Assets/UI/Appearance` | 5 | 0 | 0 | 2 |
| `Assets/UI/Auth` | 24 | 0 | 0 | 4 |
| `Assets/UI/Breach` | 8 | 0 | 0 | 1 |
| `Assets/UI/BugReport` | 1 | 0 | 0 | 1 |
| `Assets/UI/Calendar` | 9 | 0 | 0 | 1 |
| `Assets/UI/Cartography` | 14 | 0 | 0 | 2 |
| `Assets/UI/CharacterBuild` | 1 | 0 | 0 | 1 |
| `Assets/UI/Common` | 21 | 0 | 0 | 1 |
| `Assets/UI/Component` | 3 | 2 | 0 | 1 |
| `Assets/UI/Config` | 2 | 1 | 0 | 1 |
| `Assets/UI/Contextual` | 2 | 0 | 0 | 1 |
| `Assets/UI/Crafting` | 10 | 2 | 0 | 3 |
| `Assets/UI/Document` | 4 | 2 | 0 | 1 |
| `Assets/UI/Exchange` | 1 | 0 | 0 | 1 |
| `Assets/UI/FeedUi` | 3 | 0 | 0 | 1 |
| `Assets/UI/Fight` | 47 | 1 | 0 | 6 |
| `Assets/UI/Figma` | 0 | 6 | 0 | 0 |
| `Assets/UI/GameUICore` | 11 | 0 | 0 | 2 |
| `Assets/UI/Grimoire` | 46 | 3 | 0 | 5 |
| `Assets/UI/GroupSearch` | 15 | 2 | 0 | 3 |
| `Assets/UI/Guide` | 3 | 0 | 0 | 1 |
| `Assets/UI/Guidebook` | 10 | 5 | 0 | 1 |
| `Assets/UI/Havenbag` | 1 | 0 | 0 | 1 |
| `Assets/UI/House` | 2 | 1 | 0 | 1 |
| `Assets/UI/InfiniteDreams` | 19 | 0 | 0 | 3 |
| `Assets/UI/Inventory` | 32 | 6 | 0 | 5 |
| `Assets/UI/Ladder` | 16 | 0 | 0 | 2 |
| `Assets/UI/LoadingScreen` | 0 | 1 | 0 | 0 |
| `Assets/UI/MainMenu` | 1 | 0 | 0 | 0 |
| `Assets/UI/Map` | 1 | 0 | 0 | 0 |
| `Assets/UI/Mount` | 31 | 2 | 0 | 2 |
| `Assets/UI/NpcDialog` | 1 | 0 | 0 | 1 |
| `Assets/UI/Options` | 22 | 0 | 0 | 5 |
| `Assets/UI/Particles` | 2 | 0 | 0 | 2 |
| `Assets/UI/Party` | 13 | 0 | 0 | 2 |
| `Assets/UI/PersistentMenu` | 30 | 5 | 0 | 4 |
| `Assets/UI/PlayerInfo` | 7 | 0 | 0 | 1 |
| `Assets/UI/Quest` | 3 | 1 | 0 | 2 |
| `Assets/UI/Recycle` | 2 | 0 | 0 | 1 |
| `Assets/UI/RolePlay` | 11 | 1 | 0 | 3 |
| `Assets/UI/Social` | 113 | 10 | 0 | 3 |
| `Assets/UI/TSS` | 0 | 0 | 33 | 0 |
| `Assets/UI/Taxi` | 2 | 0 | 0 | 1 |
| `Assets/UI/TradeCenter` | 31 | 4 | 0 | 9 |
| `Assets/UI/Tutorial` | 1 | 1 | 0 | 1 |
| `Assets/UI/Utils` | 74 | 29 | 0 | 8 |
| `Assets/UI/Web` | 24 | 1 | 0 | 1 |
| `Assets/UIToolkit-Importing/USS` | 0 | 153 | 0 | 0 |

## Index complet et recommandations par famille

### Assets/UI/Admin — 3 chemins

Sélecteur filtré commande/catégorie/recherche ; réutiliser la densité pour AdaptersView et les outils opérateur, sans exposer de commande admin du jeu.

Objets représentatifs inspectés :

- `adminSelectItem.uxml` : `ctr_window`, `lbl_command`, `cb_category`, `gd_items`, `inp_search`.

```text
P Assets/UI/Admin/adminSelectItem.uss
I Assets/UI/Admin/adminSelectItem.uxml
P Assets/UI/Admin/adminSelectItemEntry.uxml
```

### Assets/UI/Alterations — 6 chemins

Liste filtrable par catégorie ; transposer aux effets temporaires et à la liste des états dans CombatOverlays.

Objets représentatifs inspectés :

- `alterations.uxml` : `ctr_previewAlterations`, `gd_alterations`, `search_alterationsSearch`, `ctr_cb_alterationParentCategory`, `ctr_cb_alterationCategory`.

```text
P Assets/UI/Alterations/alterationItem.uxml
P Assets/UI/Alterations/alterations.uss
I Assets/UI/Alterations/alterations.uxml
P Assets/UI/Alterations/previewedAlterationItem.uxml
P Assets/UI/Alterations/previewedAlterations.uss
P Assets/UI/Alterations/previewedAlterations.uxml
```

### Assets/UI/Appearance — 5 chemins

Onglets visage/corps/attitudes/couleurs et renommage ; transposer uniquement les sélecteurs et aperçus à VisualModsPanel.

Objets représentatifs inspectés :

- `AppearanceUI.uxml` : `ctr_colors`, `ctr_color1`, `ctr_color2`, `ctr_color3`, `ctr_color4`, `ctr_color5`, `ctr_color6`, `lbl_color1`, `lbl_color2`, `lbl_color3`, `lbl_color4`, `lbl_color5`, `lbl_color6`.
- `RenameUi.uxml` : `ctr_buttons`, `lbl_description`, `ctr_application`, `inp_inputField`, `btn_generateName`.

```text
P Assets/UI/Appearance/AppearanceSlotBig.uxml
P Assets/UI/Appearance/AppearanceSlotColor.uxml
P Assets/UI/Appearance/AppearanceSlotFace.uxml
I Assets/UI/Appearance/AppearanceUI.uxml
I Assets/UI/Appearance/RenameUi.uxml
```

### Assets/UI/Auth — 24 chemins

Sélection serveur/personnage avec état et action primaire Jouer ; appliquer à AccountsView, AccountOverlays et MyAccountView, sans copier un écran de connexion inutilisable.

Objets représentatifs inspectés :

- `ServerSelection.uxml` : `ctr_serverSelection`, `ctr_header`, `ctr_background`, `lbl_title`, `ctr_force`, `ctr_mode`, `ctr_listMode`, `ctr_carouselMode`, `btn_play`, `lbl_noServer`, `ctr_tabSection`, `ctr_search`, `ctr_freeAccount`, `ctr_forceAccount`, `ctr_warning`, `ctr_list`.
- `characterCreation.uxml` : `tx_illuBg`, `ctr_creationUi`, `btn_leaveModif`, `ctr_spells`, `ctr_mechanics`, `lbl_corps`, `ctr_corps`, `lbl_visage`, `ctr_colors`, `lbl_attitudes`, `ctr_attitudes`, `lbl_stuffs`, `ctr_stuffs`, `btn_rotateLeft`, `lbl_archetype`, `btn_showSpells`.
- `characterSelection.uxml` : `ctr_characterSelection`, `ctr_display`, `ctr_header`, `ctr_button`, `ctr_content`, `ctr_footer`, `ctr_headerText`, `ctr_columnHeader`, `gd_character`, `btn_createCharacter`, `tx_bonusEpic`, `ctr_headerTextTop`, `ctr_characterContent`, `btn_play`, `lbl_title`, `lbl_serverName`.
- `login.uxml` : `ctr_gradient`, `tx_dofusLogo`, `ctr_error`, `ctr_login`, `ctr_message`, `ctr_portSelection`, `lbl_message`, `btn_play`.

```text
P Assets/UI/Auth/Migration/popupErrorMigration.uxml
P Assets/UI/Auth/PreviousUserButton.uxml
P Assets/UI/Auth/ServerSelection/AnkamaFriendLine.uxml
P Assets/UI/Auth/ServerSelection/AnkamaFriendList.uxml
P Assets/UI/Auth/ServerSelection/FriendSearchResultItem.uxml
P Assets/UI/Auth/ServerSelection/ServerFilter.uxml
P Assets/UI/Auth/ServerSelection/ServerLine.uxml
I Assets/UI/Auth/ServerSelection/ServerSelection.uxml
P Assets/UI/Auth/ServerSelection/serverCard.uxml
I Assets/UI/Auth/characterCreation.uxml
P Assets/UI/Auth/characterCreation/InlineGridItem_gd_breedAllRoles_default.uxml
P Assets/UI/Auth/characterCreation/InlineGridItem_gd_breed_default.uxml
P Assets/UI/Auth/characterCreation/InlineGridItem_gd_heads_default.uxml
P Assets/UI/Auth/characterHeader.uxml
P Assets/UI/Auth/characterSelection/InlineGridItem_gd_character_default.uxml
P Assets/UI/Auth/characterSelection/characterLine.uxml
I Assets/UI/Auth/characterSelection/characterSelection.uxml
P Assets/UI/Auth/connectionBackground.uxml
P Assets/UI/Auth/giftMenu/InlineGridItem_gd_gifts_default.uxml
I Assets/UI/Auth/login.uxml
P Assets/UI/Auth/optionLoadingError.uxml
P Assets/UI/Auth/preGameMainMenu.uxml
P Assets/UI/Auth/pseudoChoice.uxml
P Assets/UI/Auth/secretPopup.uxml
```

### Assets/UI/Breach — 8 chemins

Suivi de boss, récompenses et budget ; emprunter progression et budgets à DashboardView et aux outils économiques, pas la mécanique de Songes.

Objets représentatifs inspectés :

- `breachShop.uxml` : `gd_rewards`, `ctr_budget`, `lbl_myBudget`.

```text
P Assets/UI/Breach/BreachBossSelection_boss.uxml
P Assets/UI/Breach/breachBossSelection.uxml
P Assets/UI/Breach/breachBossSelection_bonus.uxml
I Assets/UI/Breach/breachShop.uxml
P Assets/UI/Breach/breachShop_reward.uxml
P Assets/UI/Breach/breachTracking.uxml
P Assets/UI/Breach/breachTracking_bonus.uxml
P Assets/UI/Breach/breachTracking_boss.uxml
```

### Assets/UI/BugReport — 1 chemins

Coquille de rapport ; utile au diagnostic dans SettingsView. Ne pas inférer les champs internes absents des extraits.

Objets représentatifs inspectés :

- `BugReporter.uxml` : `BugReporter`, `is-modal`, `true`, `title`, `zendesk - le bug reporter`, `show-close-button`, `header-style`, `Primary`.

```text
I Assets/UI/BugReport/BugReporter.uxml
```

### Assets/UI/Calendar — 9 chemins

Filtres, grille de jours et événements ; appliquer à PlanningEditor et PlanningOverlays pour une semaine lisible avec légende stable.

Objets représentatifs inspectés :

- `calendarUI.uxml` : `ctr_content`, `ctr_filters`, `ctr_dayBonus`, `ctr_horoscope`, `gd_calendarFilters`, `ctr_specialDay`, `ctr_zodiac`, `ctr_protector`, `ctr_days`, `gd_events`, `lbl_calendarFilterTitle`, `tx_specialDay`, `tx_zodiac`, `tx_protector`, `lbl_dayBonusTitle`, `btn_prev`.

```text
P Assets/UI/Calendar/calendarEventPopup/AlmanaxEventDetails.uxml
P Assets/UI/Calendar/calendarEventPopup/DungeonRusherDetails.uxml
P Assets/UI/Calendar/calendarEventPopup/SeasonalEventDetails.uxml
P Assets/UI/Calendar/calendarEventPopup/WorldEventDetails.uxml
P Assets/UI/Calendar/calendarItem/calendarCard.uxml
P Assets/UI/Calendar/calendarItem/calendarDayCard.uxml
P Assets/UI/Calendar/calendarItem/calendarFilterItem.uxml
P Assets/UI/Calendar/calendarItem/calendarLine.uxml
I Assets/UI/Calendar/calendarUI.uxml
```

### Assets/UI/Cartography — 14 chemins

Carte centrale, filtres d’icônes, zoom et recherche séparée ; appliquer à WorldAtlasPanel et RoadRecorderOverlays.

Objets représentatifs inspectés :

- `CartographyMapUI.uxml` : `ctr_breach`, `ctr_filters`, `ctr_locTreeConquest`, `ctr_locTreeAnomaly`, `ctr_worldmapMenu`, `btn_openConfig`, `btn_player`, `btn_zoomIn`, `btn_zoomOut`, `gd_sectors`, `ctr_filterFlags`, `ctr_filterTransport`, `ctr_filterDungeon`, `ctr_filterQuest`, `ctr_filterRepeatableQuest`, `ctr_filterBidHouse`.
- `CartographySearchUI.uxml` : `ctr_noSearchResult`, `ctr_searchResult`, `lbl_noSearchResult`, `gd_searchResults`, `ctr_searchFooter`, `inp_search`, `lbl_searchResultsCount`, `btn_search`, `btn_searchFilter`.

```text
P Assets/UI/Cartography/CartographyBannerUI.uxml
P Assets/UI/Cartography/CartographyBaseUI.uxml
I Assets/UI/Cartography/CartographyMapUI.uxml
P Assets/UI/Cartography/CartographyTool.uxml
P Assets/UI/Cartography/CartographyTooltip.uxml
P Assets/UI/Cartography/cartographyUi/CartographySearchResultItem.uxml
I Assets/UI/Cartography/cartographyUi/CartographySearchUI.uxml
P Assets/UI/Cartography/cartographyUi/CartographyTooltipElement.uxml
P Assets/UI/Cartography/cartographyUi/InlineGridItem_gdSearchAll_default.uxml
P Assets/UI/Cartography/cartographyUi/InlineGridItem_gdZone_default.uxml
P Assets/UI/Cartography/cartographyUi/InlineGridItem_gd_flags_default.uxml
P Assets/UI/Cartography/cartographyUi/InlineGridItem_gd_sectors_default.uxml
P Assets/UI/Cartography/cartographyUi/InlineGridItem_gd_worldmaps_ctr_worldmapCat.uxml
P Assets/UI/Cartography/cartographyUi/TerritoryItem.uxml
```

### Assets/UI/CharacterBuild — 1 chemins

Liste de configurations, action principale et création de preset ; appliquer aux profils de bots et aux configurations de combat.

Objets représentatifs inspectés :

- `presetsManager.uxml` : `ctr_buildManager`, `ctr_labelButton`, `ctr_list`, `lbl_title`, `btn_actionLeft`, `btn_action`, `ctr_builds`, `btn_newBuild`, `tx_border`, `btn_left`, `btn_right`, `ctr_slots`.

```text
I Assets/UI/CharacterBuild/presetsManager.uxml
```

### Assets/UI/Common — 21 chemins

Contenu et barre de boutons de popup réutilisables ; unifier tous les overlays plutôt que recréer leur chrome par module.

Objets représentatifs inspectés :

- `popup.uxml` : `ctr_content`, `ctr_popupContent`, `ctr_buttons`, `lbl_content`.

```text
P Assets/UI/Common/FiltersCategory/MinMaxFilterCategory.uxml
P Assets/UI/Common/actionWarningPopup.uxml
P Assets/UI/Common/administrablePopup.uxml
P Assets/UI/Common/administrablePopup/InlineGridItem_gd_items_default.uxml
P Assets/UI/Common/colorPickerUi.uxml
P Assets/UI/Common/colorSquare.uxml
P Assets/UI/Common/entityPortrait.uxml
P Assets/UI/Common/gameMenu.uxml
P Assets/UI/Common/imagepopup.uxml
P Assets/UI/Common/inputComboBoxPopup.uxml
P Assets/UI/Common/inputPopup.uxml
P Assets/UI/Common/kisInfractionPopup.uxml
P Assets/UI/Common/kisPopup.uxml
P Assets/UI/Common/kisPreventSanctionPopup.uxml
P Assets/UI/Common/lightReportPopup.uxml
P Assets/UI/Common/lockedPopup.uxml
P Assets/UI/Common/passwordMenu.uxml
I Assets/UI/Common/popup.uxml
P Assets/UI/Common/quantityPopup.uxml
P Assets/UI/Common/queuePopup.uxml
P Assets/UI/Common/recipesUI.uxml
```

### Assets/UI/Component — 5 chemins

Slots multiples et contrôles réutilisables ; réemployer seulement quand les données sont réellement des objets ou des ressources.

Objets représentatifs inspectés :

- `MultipleSlot.uxml` : `ctr_multipleSlots`, `ctr_slotContent`, `ctr_realContent`, `ctr_button`, `btn_open`.

```text
P Assets/UI/Component/CheckboxUsage/achievementCheckbox.uss
P Assets/UI/Component/CheckboxUsage/achievementCheckbox.uxml
P Assets/UI/Component/CheckboxUsage/tagCheckbox.uss
P Assets/UI/Component/CheckboxUsage/tagCheckbox.uxml
I Assets/UI/Component/MultipleSlot.uxml
```

### Assets/UI/Config — 3 chemins

Choix de qualité et de typographie ; appliquer aux préférences visuelles de SettingsView et VisualModsPanel.

Objets représentatifs inspectés :

- `QualitySelection.uxml` : `ctr_container`, `gd_quality`, `gd_typo`, `ctr_footer`, `lbl_qualityInfo`, `btn_select`.

```text
I Assets/UI/Config/QualitySelection.uxml
P Assets/UI/Config/QualitySelectionItem.uxml
P Assets/UI/Config/qualitySelection.uss
```

### Assets/UI/Contextual — 2 chemins

Affichage de texte contextuel ; adapter aux notifications et états courts, pas aux journaux longs.

Objets représentatifs inspectés :

- `textContextual.uxml` : `textContextual`, `name`, `image`, `display-tooltip-when-elided`, `true`, `text`, `fzfzefzffzefzf`, `verdana-normal`.

```text
P Assets/UI/Contextual/styledTextContextual.uxml
I Assets/UI/Contextual/textContextual.uxml
```

### Assets/UI/Crafting — 12 chemins

Recette/résultat/inventaire, historique de forgemagie et résultat de concassage ; appliquer à ItemCraftPlannerPanel, SmithMagicView et EcoToolsPanel.

Objets représentatifs inspectés :

- `CraftUI.uxml` : `ctr_recipe`, `ctr_crafter`, `ctr_craftResult`, `ctr_inventory`, `ctr_crafterClientItemView`, `ctr_craftResultImg`, `ctr_crafterClientPayment`, `ctr_itemToCraft`, `tx_craftResultBackground`, `lbl_youCrafted`, `lbl_qtyCrafted`, `lbl_itemNameCrafted`, `tx_crafterAccepted`, `tx_clientAccepted`, `ctr_client`, `tx_craftResultImage`.
- `runeMaker.uxml` : `ctr_filters`, `ctr_main`, `ctr_footer`, `ctr_mainResult`, `ctr_footerResult`, `tx_info`, `btn_crush`, `btn_crushAgain`, `lbl_quantityObject`, `gd_crushedItems`, `lbl_crushedItemHeader`, `lbl_coefficentHeader`, `lbl_obtainedRunesHeader`, `lbl_itemsPrice`, `lbl_itemsPriceValue`, `lbl_runesPrice`.
- `smithMagic.uxml` : `ctr_header`, `ctr_crafter`, `ctr_merge`, `ctr_client`, `ctr_sidePanel`, `ctr_mainPanel`, `ctr_crafterInfos`, `ctr_buttons`, `ctr_clientInfos`, `ctr_history`, `ctr_bag`, `ctr_droparea`, `ctr_stats`, `lbl_crafterName`, `lbl_crafterJob`, `ctr_crafterJobLevel`.

```text
I Assets/UI/Crafting/CraftUI.uxml
P Assets/UI/Crafting/CrafterItem.uxml
P Assets/UI/Crafting/crafterList.uxml
I Assets/UI/Crafting/runeMaker.uxml
P Assets/UI/Crafting/runeMaker/InlineGridItem_gd_items_default.uxml
P Assets/UI/Crafting/runeMaker/InlineGridItem_gd_results_default_Slot.uss
P Assets/UI/Crafting/runeMaker/runeMakerItem.uxml
P Assets/UI/Crafting/runeMaker/runeMakerResultItem.uxml
P Assets/UI/Crafting/runeMaker/runeMaker_Slot.uss
I Assets/UI/Crafting/smithMagic.uxml
P Assets/UI/Crafting/smithMagicItemEffectLine.uxml
P Assets/UI/Crafting/smithMagicItemHistory.uxml
```

### Assets/UI/Document — 6 chemins

Titre, contenu, illustration et fermeture ; réutiliser pour l’aide intégrée et les explications d’un plan de fabrication.

Objets représentatifs inspectés :

- `scroll.uxml` : `tx_background`, `tx_illu`, `btn_close`, `btn_link`, `lbl_title`, `tx_deco`, `lbl_content`.

```text
P Assets/UI/Document/readingBook.uss
P Assets/UI/Document/readingBook.uxml
P Assets/UI/Document/readingBookPageItem.uxml
P Assets/UI/Document/scroll.uss
I Assets/UI/Document/scroll.uxml
P Assets/UI/Document/staticDocument.uxml
```

### Assets/UI/Exchange — 1 chemins

Deux côtés joueur courant/autre joueur ; transposer aux transferts entre comptes avec récapitulatif avant action, non à un dialogue décoratif.

Objets représentatifs inspectés :

- `exchangeUi.uxml` : `ctr_window_exchange`, `ctr_otherPlayerSide`, `ctr_currentPlayerSide`.

```text
I Assets/UI/Exchange/exchangeUi.uxml
```

### Assets/UI/FeedUi — 3 chemins

Objet, effets avant/après et validation ; modèle utile pour une prévisualisation de modification dans SmithMagicView.

Objets représentatifs inspectés :

- `feedUi.uxml` : `ctr_pet`, `lbl_infoDestroy`, `btn_cancel`, `btn_validate`, `tx_itemToFeed`, `lbl_level`, `lbl_name`, `ctr_upgrade`, `lbl_autoPilot`, `lbl_levelResult`, `lbl_levelResult_value`, `lbl_energyResult`, `lbl_energyResult_value`, `lbl_xpResult`, `lbl_xpResult_value`.

```text
P Assets/UI/FeedUi/effectFeedLine.uxml
I Assets/UI/FeedUi/feedUi.uxml
P Assets/UI/FeedUi/itemFeedLine.uxml
```

### Assets/UI/Fight — 48 chemins

Timeline, temps/tours, préparation, résultats par onglets et effets ; appliquer à CombatOverlays et CombatSimOverlay, conserver le terrain ou la simulation comme centre.

Objets représentatifs inspectés :

- `buffSummary.uxml` : `ctr_properties`, `ctr_bonus`, `ctr_malus`, `ctr_triggered`, `ctr_states`, `ctr_counters`, `ctr_others`, `lbl_noSummary`, `lbl_propertiesTitle`, `ctr_propertiesContent`, `lbl_bonusTitle`, `ctr_bonusContent`, `lbl_malusTitle`, `ctr_malusContent`, `lbl_triggeredTitle`, `ctr_triggeredContent`.
- `buffs.uxml` : `buffs`, `name`, `BuffsUI`, `buffsUIRoot`, `parent`, `VisualElement/UxmlSerializedData`.
- `EndTurnWidget.uxml` : `ctr_baseHeader`, `ctr_toggles`, `ctr_animation`, `ctr_counter`, `ctr_turnCounter`, `ctr_requestHelp`, `ctr_EndTurn`, `ctr_timeGauge`, `btn_leave`, `lbl_animationMessageSeconds`, `lbl_animationMessage`, `lbl_seconds`, `lbl_turnCount`, `lbl_endTurn`, `btn_EndTurn`, `ctr_timerBarValue`.
- `FighterInfoPlacementUI.uxml` : `ctr_preview`, `ctr_controls`, `lbl_move`, `ctr_buttons`, `btn_cancel`, `btn_reset`, `btn_validate`.
- `fightResult.uxml` : `tab_content`, `ctr_footer`, `ctr_limiter`, `tab_myCharacter`, `tab_resume`, `tab_statistics`, `btn_closeUI`, `lbl_xpLimitWarning`, `btn_changeXpLimit`, `btn_challengeLeft`, `ctr_challenge`, `btn_challengeRight`, `ctr_waves`, `lbl_turn`, `lbl_time`, `lbl_score`.
- `FightTimeline.uxml` : `ctr_timelineContent`, `ctr_counters`, `ctr_currentTurn`, `ctr_waves`, `lbl_currentTurn`, `ctr_waveCounters`, `ctr_currentWave`, `ctr_nextWave`, `lbl_currentWave`, `lbl_nextWave`.

```text
P Assets/UI/Fight/Buffs/buffSpellList.uxml
P Assets/UI/Fight/Buffs/buffSpellsBySource.uxml
P Assets/UI/Fight/Buffs/buffSpellsMonster.uxml
I Assets/UI/Fight/Buffs/buffSummary.uxml
I Assets/UI/Fight/Buffs/buffs.uxml
P Assets/UI/Fight/Buffs/buffsWindow.uxml
P Assets/UI/Fight/Buffs/spellBuffComponent.uxml
P Assets/UI/Fight/ChallengeDisplay/challengeChoiceDisplay.uxml
P Assets/UI/Fight/ChallengeDisplay/challengeGridSlot.uxml
P Assets/UI/Fight/ChallengeDisplay/challengeGroup.uxml
P Assets/UI/Fight/ChallengeDisplay/challengeSelectionDisplay.uxml
P Assets/UI/Fight/ChallengeDisplay/challengeSettingsDisplay.uxml
P Assets/UI/Fight/ChallengeDisplay/challenge_group.uss
P Assets/UI/Fight/ChallengeDisplay/successGridSlot.uxml
I Assets/UI/Fight/EndTurnWidget.uxml
P Assets/UI/Fight/FightResult/FightResultStatsItem.uxml
P Assets/UI/Fight/FightResult/fightResultCharacterTab.uxml
P Assets/UI/Fight/FightResult/fightResultMonsterLine.uxml
P Assets/UI/Fight/FightResult/fightResultResumeTab.uxml
P Assets/UI/Fight/FightResult/fightResultSection.uxml
P Assets/UI/Fight/FightResult/fightResultStatsItemLine.uxml
P Assets/UI/Fight/FightResult/fightResultStatsTab.uxml
P Assets/UI/Fight/FightResult/fighterGridSlot.uxml
P Assets/UI/Fight/FightResult/fighterResultLine.uxml
I Assets/UI/Fight/FighterInfoPlacementUI.uxml
P Assets/UI/Fight/Ping/PingWheel.uxml
P Assets/UI/Fight/Score/FightScoreUi.uxml
P Assets/UI/Fight/SlaveFight/slaveFightDragWindow.uxml
P Assets/UI/Fight/SlaveFight/slaveFightUi.uxml
P Assets/UI/Fight/SlaveFight/slaveFightUiLine.uxml
P Assets/UI/Fight/SpectatorUI/fightItem.uxml
P Assets/UI/Fight/SpectatorUI/fightTeamItem.uxml
P Assets/UI/Fight/SpellPicker/spellPicker.uxml
P Assets/UI/Fight/SpellPicker/spellPickerElement.uxml
P Assets/UI/Fight/SpellPicker/spellPickerObjectElement.uxml
P Assets/UI/Fight/WorldBoss/WorldBossUi.uxml
I Assets/UI/Fight/fightResult.uxml
P Assets/UI/Fight/fightResultSimple.uxml
P Assets/UI/Fight/infiniteDream/InfiniteDreamEndFightUI.uxml
P Assets/UI/Fight/newTurnStartUI.uxml
P Assets/UI/Fight/playerTurnStartUI.uxml
P Assets/UI/Fight/rewardsUi/rewardsUi.uxml
P Assets/UI/Fight/rewardsUi/rewardsUiMinimized.uxml
P Assets/UI/Fight/rewardsUi/rewards_Item_Default.uxml
P Assets/UI/Fight/spectatorUi.uxml
I Assets/UI/Fight/timeline/FightTimeline.uxml
P Assets/UI/Fight/timeline/timelineFighter.uxml
P Assets/UI/Fight/turnAnnouncerFx.uxml
```

### Assets/UI/Figma — 6 chemins

Feuilles USS de composants, pas un écran ; vocabulaire de boutons, fenêtres et états pour le thème global.

Aucun contenu individuel inspecté dans cette famille ; recommandation fondée sur les noms ou sur les familles voisines citées.

```text
P Assets/UI/Figma/USS/Custom/ColorSquareLine.uss
P Assets/UI/Figma/USS/Custom/itemSlot-extension.uss
P Assets/UI/Figma/USS/Custom/itemTile-extension.uss
P Assets/UI/Figma/USS/globalStyleFontSize.uss
P Assets/UI/Figma/USS/itemSlot-figma.uss
P Assets/UI/Figma/USS/itemTile-figma.uss
```

### Assets/UI/GameUICore — 11 chemins

Menu principal, informations de carte et commandes courtes ; appliquer au chrome MainWindow et aux indicateurs de DashboardView.

Objets représentatifs inspectés :

- `mainMenu.uxml` : `ctr_content`, `ctr_mainPart`, `ctr_footer`, `ctr_changePart`, `ctr_options`, `ctr_subPart`, `btn_close`, `lbl_buildNumber`, `btn_options`, `btn_quit`, `ctr_sub_txt`, `btn_sub`, `tx_shield`, `lbl_sub_text`.
- `mapInfo.uxml` : `ctr_infos`, `ctr_buttons`, `lbl_mapName`, `ctr_infiniteDreamInfo`, `ctr_mapCoordsLevel`, `ctr_breachBonuses`, `ctr_alliance`, `tx_areaModifier`, `btn_minimize`, `ctr_buttonsToHide`, `btn_spectate`, `btn_havenBagSettings`, `ctr_guildHallSettings`, `btn_myHouseSettings`, `btn_leave`, `btn_leaveWorldmap`.

```text
P Assets/UI/GameUICore/CreatePresetPopup.uxml
P Assets/UI/GameUICore/LatencyUI.uxml
P Assets/UI/GameUICore/ReportUI.uxml
P Assets/UI/GameUICore/SmileyUI.uxml
P Assets/UI/GameUICore/SmileyUIEmoteTab.uxml
P Assets/UI/GameUICore/SmileyUISmileyTab.uxml
P Assets/UI/GameUICore/UISmiley/InlineGridItem_gd_smileyCategories_default.uxml
P Assets/UI/GameUICore/channelColorizedItem.uxml
I Assets/UI/GameUICore/mainMenu.uxml
I Assets/UI/GameUICore/mapInfo.uxml
P Assets/UI/GameUICore/payZone.uxml
```

### Assets/UI/Grimoire — 49 chemins

Métiers, sorts, bestiaire, quêtes et succès ; appliquer à ProfessionPlannerPanel, LevelCapOverlay et aux détails de combat ou de ressources.

Objets représentatifs inspectés :

- `bestiaryTab.uxml` : `ctr_bestiaryTab`, `ctr_levelParent`, `ctr_monsterTypeParent`, `ctr_superAreaParent`, `ctr_areaParent`, `ctr_territoryParent`, `ctr_resetCurrentSubarea`, `ctr_monsterList`, `inp_search`, `btn_searchFilter`, `btn_resetCurrentSubarea`, `ctr_monsterDetails`, `lbl_searchResultCount`, `lbl_noResult`, `gd_list`, `ctr_monsterInfo`.
- `jobProgressTab.uxml` : `ctr_tableColumnHeaders`, `ctr_publicCraft`, `btn_foldAll`.
- `jobUI.uxml` : `jobUI`, `jobCraftTab`, `jobProgressTab`, `name`, `jobTab`, `JobUI`, `title`, `img-url`.
- `quests.uxml` : `lbl_emptyList`, `tx_empty`, `ctr_content`, `ctr_rewards`, `btn_searchFilter`, `ctr_minMax`, `lbl_questTitle`, `btn_hide`, `btn_follow`, `lbl_rewards`, `ctr_kamasXp`, `lbl_objectives`, `gd_objectives`, `ctr_kamas`, `ctr_xp`, `ctr_step`.
- `spellList.uxml` : `ctr_spellList`, `ctr_main`, `ctr_grid`, `ctr_footer`, `ctr_filter`, `gd_spells`, `inp_search`, `ctr_filters`, `ctr_spellContainer`, `ctr_leftSpellContainer`, `ctr_verticalDivider`, `ctr_rightSpellContainer`.

```text
P Assets/UI/Grimoire/achievements/achievementItem.uxml
P Assets/UI/Grimoire/achievements/achievementSummaryItem.uxml
P Assets/UI/Grimoire/achievements/achievements.uxml
P Assets/UI/Grimoire/achievements/endingAchievementsItem.uxml
P Assets/UI/Grimoire/achievements/objectiveField.uxml
I Assets/UI/Grimoire/bestiaryTab/bestiaryTab.uxml
P Assets/UI/Grimoire/bestiaryTab/bestiaryTabItem.uxml
P Assets/UI/Grimoire/collectionTab/SuccessItem.uxml
P Assets/UI/Grimoire/collectionTab/collectionItem.uss
P Assets/UI/Grimoire/collectionTab/collectionItem.uxml
P Assets/UI/Grimoire/collectionTab/collectionTab.uss
P Assets/UI/Grimoire/collectionTab/collectionTab.uxml
P Assets/UI/Grimoire/collectionTab/successItem.uss
P Assets/UI/Grimoire/commonSpellsList.uxml
P Assets/UI/Grimoire/companions/companionDetail.uxml
P Assets/UI/Grimoire/companions/companionElement.uxml
P Assets/UI/Grimoire/companions/companionUi.uxml
P Assets/UI/Grimoire/encyclopediaBase.uxml
P Assets/UI/Grimoire/encyclopediaList.uxml
P Assets/UI/Grimoire/encyclopediaList/encyclopediaItem.uxml
P Assets/UI/Grimoire/expeditions/Item_big_expedition.uxml
P Assets/UI/Grimoire/expeditions/Item_small_expedition.uxml
P Assets/UI/Grimoire/expeditions/expeditions.uxml
P Assets/UI/Grimoire/forgettableSpells.uxml
P Assets/UI/Grimoire/forgettableSpells/ForgettableSpellSetListUi.uxml
P Assets/UI/Grimoire/forgettableSpells/InlineGridItem_gd_commonSpells_default.uxml
P Assets/UI/Grimoire/forgettableSpells/InlineGridItem_gd_forgettableSpellSet.uxml
P Assets/UI/Grimoire/forgettableSpells/InlineGridItem_gd_forgettableSpells_default.uxml
P Assets/UI/Grimoire/forgettableSpells/commonSpell.uxml
P Assets/UI/Grimoire/forgettableSpells/forgettableSpellList.uxml
P Assets/UI/Grimoire/forgettableSpells/forgettableSpellRemovePopup.uxml
P Assets/UI/Grimoire/forgettableSpells/modster.uxml
P Assets/UI/Grimoire/jobProgressItem.uxml
I Assets/UI/Grimoire/jobProgressTab.uxml
P Assets/UI/Grimoire/jobRecipeItem.uxml
P Assets/UI/Grimoire/jobTab/jobSkillsItem.uxml
P Assets/UI/Grimoire/jobTab/jobSkillsUI.uxml
I Assets/UI/Grimoire/jobUI.uxml
P Assets/UI/Grimoire/modsters.uxml
P Assets/UI/Grimoire/modstersList.uxml
P Assets/UI/Grimoire/playerSpell.uxml
P Assets/UI/Grimoire/quests/quest.uxml
P Assets/UI/Grimoire/quests/quest_objective.uxml
I Assets/UI/Grimoire/quests/quests.uxml
P Assets/UI/Grimoire/spellBase.uxml
P Assets/UI/Grimoire/spellButtonsEntry.uxml
I Assets/UI/Grimoire/spellList.uxml
P Assets/UI/Grimoire/spellListEntry.uxml
P Assets/UI/Grimoire/spellList_obtentionLevels.uxml
```

### Assets/UI/GroupSearch — 17 chemins

Onglets recrutement/demandes et création structurée ; transposer aux filtres et composition de TeamsView, pas aux annonces publiques.

Objets représentatifs inspectés :

- `lobbies.uxml` : `tab_join`, `tab_myAnnounce`, `tab_myApplications`.
- `lobbyCreation.uxml` : `lbl_title`, `btn_applicants`, `ctr_toastNotif`, `ctr_newLobby`, `ctr_editLobby`, `lbl_toast`, `tab_createDungeon`, `tab_createQuest`, `tab_createRaid`, `tab_createDream`, `lbl_activityName`, `lbl_tagInfo`, `ctr_creationTags`, `ctr_creationAchievements`, `cb_autoJoin`, `btn_reset`.
- `groupSearch.uxml` : `tab_dungeonMatchmaking`, `tab_announceTab`.

```text
P Assets/UI/GroupSearch/DungeonMatchmaking/dungeonLine.uxml
P Assets/UI/GroupSearch/DungeonMatchmaking/dungeonMatchmaking.uxml
P Assets/UI/GroupSearch/DungeonMatchmaking/dungeonMatchmakingModal.uxml
P Assets/UI/GroupSearch/DungeonMatchmaking/dungeonSuccessLine.uxml
P Assets/UI/GroupSearch/DungeonMatchmaking/dungeonTooltip.uxml
I Assets/UI/GroupSearch/Lobbies/lobbies.uxml
P Assets/UI/GroupSearch/Lobbies/lobby.uss
P Assets/UI/GroupSearch/Lobbies/lobbyApplicantLine.uss
P Assets/UI/GroupSearch/Lobbies/lobbyApplicantLine.uxml
P Assets/UI/GroupSearch/Lobbies/lobbyApplicantLineMini.uxml
P Assets/UI/GroupSearch/Lobbies/lobbyApplicants.uxml
P Assets/UI/GroupSearch/Lobbies/lobbyApplicantsTooltip.uxml
P Assets/UI/GroupSearch/Lobbies/lobbyApplications.uxml
P Assets/UI/GroupSearch/Lobbies/lobbyCard.uxml
I Assets/UI/GroupSearch/Lobbies/lobbyCreation.uxml
P Assets/UI/GroupSearch/Lobbies/lobbyLine.uxml
I Assets/UI/GroupSearch/groupSearch.uxml
```

### Assets/UI/Guide — 3 chemins

Tuiles d’objectifs et progression ; appliquer au NoCodeWizardOverlay et aux objectifs du tableau de bord.

Objets représentatifs inspectés :

- `DofusTile.uxml` : `ctr_dofusTile`, `ctr_background`, `ctr_level`, `ctr_border`, `lbl_level`, `tx_dofus`, `lbl_name`.

```text
P Assets/UI/Guide/Dofus/DofusProgressUi.uxml
I Assets/UI/Guide/Dofus/DofusTile.uxml
P Assets/UI/Guide/Dofus/ProgressObjectiveLine.uxml
```

### Assets/UI/Guidebook — 15 chemins

Recherche, résultats et pagination ; appliquer à l’aide et à la découverte des fonctions dans NoCodeWizardOverlay.

Objets représentatifs inspectés :

- `gameguideTab.uxml` : `inp_search`, `ctr_search_results`, `ctr_search_txts`, `ctr_search_btns`, `lbl_results`, `ctr_numbers`, `btn_prv`, `btn_nxt`, `lbl_search_idx`, `lbl_results_count`.

```text
P Assets/UI/Guidebook/GameSuggestion/GameSuggestionCard.uss
P Assets/UI/Guidebook/GameSuggestion/GameSuggestionCard.uxml
P Assets/UI/Guidebook/GameSuggestion/gameSuggestion.uss
P Assets/UI/Guidebook/GameSuggestion/gameSuggestion.uxml
P Assets/UI/Guidebook/TemporisTab/InlineGridItem_gd_step_ctr_big.uxml
P Assets/UI/Guidebook/TemporisTab/InlineGridItem_gd_step_ctr_emptyLine.uxml
P Assets/UI/Guidebook/TemporisTab/InlineGridItem_gd_step_ctr_small.uxml
P Assets/UI/Guidebook/TemporisTab/InlineGridItem_gd_step_ctr_top.uxml
P Assets/UI/Guidebook/TemporisTab/temporisTab.uss
P Assets/UI/Guidebook/TemporisTab/temporisTab.uxml
P Assets/UI/Guidebook/gameguideItemDescription.uxml
P Assets/UI/Guidebook/gameguideTab.uss
I Assets/UI/Guidebook/gameguideTab.uxml
P Assets/UI/Guidebook/guidebookBase.uss
P Assets/UI/Guidebook/guidebookBase.uxml
```

### Assets/UI/Havenbag — 1 chemins

Catégories d’objets, filtres et sauvegarde ; utile au patron de palette/aperçu de VisualModsPanel, sans inventer de vue havre-sac.

Objets représentatifs inspectés :

- `havenbagManager.uxml` : `btn_all`, `btn_ground`, `btn_tables`, `btn_objects`, `btn_searchFilter`, `btn_editClear`, `btn_editReset`, `btn_save`, `lbl_emptyBox`, `lbl_ObjectsAddedToTag`.

```text
I Assets/UI/Havenbag/havenbagManager.uxml
```

### Assets/UI/House — 3 chemins

Contenu, navigation entre entrées et validation ; patron de fiche de compte ou ressource, sans équivalent immobilier requis.

Objets représentatifs inspectés :

- `HouseUI.uxml` : `ctr_window`, `ctr_footer`, `ctr_content`, `ctr_switchHouse`, `btn_cancel`, `btn_validate`, `tx_house`, `ctr_text`, `btn_prevHouse`, `ctr_pills`, `btn_nextHouse`, `lbl_houseName`, `ctr_divider1`, `ctr_salePrice`, `ctr_localisation`, `ctr_mountNbr`.

```text
I Assets/UI/House/HouseUI.uxml
P Assets/UI/House/houseSalePopup.uxml
P Assets/UI/House/houseUi.uss
```

### Assets/UI/InfiniteDreams — 19 chemins

Onglets, liste de défis et boutons commencer/continuer ; emprunter uniquement le patron de session/reprise à BotsView.

Objets représentatifs inspectés :

- `DropList.uxml` : `ctr_content`, `lbl_noDrop`, `ctr_sort`, `gd_rewards`.
- `InfiniteDreamHUB.uxml` : `ctr_top`, `ctr_contentNewDream`, `ctr_bottom`, `ctr_primaryTabs`, `ctr_secondaryTabs`, `ctr_contentDream`, `ctr_contentTrials`, `btn_startBreach`, `tabs_top`, `ctr_left`, `ctr_right`, `gd_dreamTrials`, `btn_newDream`, `btn_continueDream`, `btn_basicDream`, `btn_trials`.
- `InfiniteDreamSpellBar.uxml` : `ctr_spells`, `slot-size`, `slot-type`, `slot1`, `slot2`, `slot3`, `slot4`, `slot5`, `lbl_count`.

```text
P Assets/UI/InfiniteDreams/Bestiary/infiniteDreamBestiary.uxml
P Assets/UI/InfiniteDreams/Bestiary/infiniteDreamBestiaryLine.uxml
P Assets/UI/InfiniteDreams/BreachDoorTooltip.uxml
I Assets/UI/InfiniteDreams/DropList.uxml
P Assets/UI/InfiniteDreams/DropListLine.uxml
P Assets/UI/InfiniteDreams/End/InfiniteDreamEnd.uxml
I Assets/UI/InfiniteDreams/InfiniteDreamHUB.uxml
P Assets/UI/InfiniteDreams/InfiniteDreamTracking.uxml
P Assets/UI/InfiniteDreams/InfiniteDreamsIntensityCard.uxml
P Assets/UI/InfiniteDreams/Loot/InfiniteDreamLootTable.uxml
P Assets/UI/InfiniteDreams/Loot/InfiniteDreamLootTableLine.uxml
P Assets/UI/InfiniteDreams/Map/BreachRoom.uxml
P Assets/UI/InfiniteDreams/Map/InfiniteDreamMap.uxml
P Assets/UI/InfiniteDreams/Popup/CreateNewPopup.uxml
P Assets/UI/InfiniteDreams/Roll/RerollPopup.uxml
P Assets/UI/InfiniteDreams/Shop/InfiniteDreamShop.uxml
P Assets/UI/InfiniteDreams/Shop/InfiniteDreamShopLine.uxml
I Assets/UI/InfiniteDreams/Spell/InfiniteDreamSpellBar.uxml
P Assets/UI/InfiniteDreams/Trial/InfiniteDreamTrialItem.uxml
```

### Assets/UI/Inventory — 38 chemins

Stockage commun partagé par banque/équipement/échanges ; appliquer aux stocks d’EcoToolsPanel et aux vues de détails HDV plutôt que fabriquer trois grilles différentes.

Objets représentatifs inspectés :

- `equipmentStorageUi.uxml` : `ctr_content`, `ctr_tabContent`, `ctr_equipedTag`, `lbl_ObjectsAddedToTag`.
- `guildChest.uxml` : `guildChest`, `storage`, `name`, `template`, `TemplateContainer/UxmlSerializedData`.
- `storage.uxml` : `btn_itemsFilter`, `lbl_emptyBox`, `btn_category`, `btn_sort`, `btn_craft`, `btn_sales`, `cb_sort`, `cb_category`, `lbl_ObjectsAddedToTag`, `lbl_kamas`.
- `storageBank.uxml` : `storageBank`, `storage`, `name`, `storageBankRoot`, `VisualElement/UxmlSerializedData`, `BankWindow`, `title`, `[Bank]`.
- `storageExchange.uxml` : `storageExchange`, `storage`, `name`, `storageBank`, `window`, `ExchangeStorageWindow`, `no-ornament-header`, `true`.

```text
P Assets/UI/Inventory/CharacterInventory/CharacterPresetUi.uxml
P Assets/UI/Inventory/CharacterInventory/PresetListUi.uxml
P Assets/UI/Inventory/CharacterInventory/PresetTile.uxml
P Assets/UI/Inventory/CosmeticItems/CosmeticItemLine.uxml
P Assets/UI/Inventory/CosmeticItems/CosmeticOrnamentLine.uxml
P Assets/UI/Inventory/CosmeticItems/CosmeticTitleLine.uxml
P Assets/UI/Inventory/GuildChest/contributorItem.uxml
P Assets/UI/Inventory/GuildChest/guildChestCategory.uxml
P Assets/UI/Inventory/GuildChest/updateStorageTab.uss
P Assets/UI/Inventory/GuildChest/updateStorageTab.uxml
P Assets/UI/Inventory/MakinaListUi.uxml
P Assets/UI/Inventory/PrismModuleUi.uxml
P Assets/UI/Inventory/RideWrapperListUi.uxml
P Assets/UI/Inventory/TagStorage/TagStorageEditUi.uxml
P Assets/UI/Inventory/cosmeticUi.uxml
I Assets/UI/Inventory/equipmentStorageUi.uxml
P Assets/UI/Inventory/equipmentUi.uss
I Assets/UI/Inventory/guildChest.uxml
P Assets/UI/Inventory/itemListUi.uss
P Assets/UI/Inventory/itemListUi.uxml
P Assets/UI/Inventory/itemSet_Slot.uxml
P Assets/UI/Inventory/itemsSet.uxml
P Assets/UI/Inventory/itemsSet_bonus.uxml
P Assets/UI/Inventory/livingObject.uxml
P Assets/UI/Inventory/livingObjectLook.uss
P Assets/UI/Inventory/livingObjectLook.uxml
P Assets/UI/Inventory/mimicry.uxml
P Assets/UI/Inventory/multiTabStorageVerticalTabButton.uss
P Assets/UI/Inventory/multiTabStorageVerticalTabButton.uxml
P Assets/UI/Inventory/smallCosmeticLine.uxml
P Assets/UI/Inventory/storableItemListUi.uxml
I Assets/UI/Inventory/storage.uxml
I Assets/UI/Inventory/storageBank.uxml
P Assets/UI/Inventory/storageExchange.uss
I Assets/UI/Inventory/storageExchange.uxml
P Assets/UI/Inventory/storageGuildChest.uxml
P Assets/UI/Inventory/updateFilterChest.uxml
P Assets/UI/Inventory/watchEquipmentUi.uxml
```

### Assets/UI/Ladder — 16 chemins

Recherche, en-têtes de colonnes et classement ; adapter aux comparaisons d’agents dans DashboardView et BotsView.

Objets représentatifs inspectés :

- `KoliLadderTab.uxml` : `ctr_search`, `ctr_topRanking`, `ctr_tableColumnHeaders`, `ctr_footer`, `lbl_noContent`, `inp_search`, `btn_rewards`, `tx_rankingBg`, `ctr_rankingSilver`, `ctr_rankingGold`, `ctr_rankingCopper`, `lbl_topPlayerNameSilver`, `lbl_topPlayerNameGold`, `lbl_topPlayerNameCopper`, `ctr_breeds`, `slot-type`.
- `LadderUI.uxml` : `tabs`, `ctr_content`.

```text
P Assets/UI/Ladder/ExperienceAndAchievementLadderLine.uxml
P Assets/UI/Ladder/ExperienceAndAchievementLadderTab.uxml
P Assets/UI/Ladder/InfiniteDreamLadderLine.uxml
P Assets/UI/Ladder/InfiniteDreamLadderTab.uxml
P Assets/UI/Ladder/KoliLadderLine.uxml
I Assets/UI/Ladder/KoliLadderTab.uxml
I Assets/UI/Ladder/LadderUI.uxml
P Assets/UI/Ladder/RaidLadderLine.uxml
P Assets/UI/Ladder/RaidLadderTab.uxml
P Assets/UI/Ladder/WorldEventLadderFilter.uxml
P Assets/UI/Ladder/WorldEventLadderLine.uxml
P Assets/UI/Ladder/WorldEventReward/LadderRewardsTable.uxml
P Assets/UI/Ladder/WorldEventReward/LadderRewardsTableLine.uxml
P Assets/UI/Ladder/WorldEventReward/WorldEventReward.uxml
P Assets/UI/Ladder/WorldEventReward/worldEventRewardItem.uxml
P Assets/UI/Ladder/WorldEventsLadderTab.uxml
```

### Assets/UI/LoadingScreen — 1 chemins

USS seulement dans cet inventaire ; appliquer une cohérence d’état de chargement sans prétendre connaître le layout.

Aucun contenu individuel inspecté dans cette famille ; recommandation fondée sur les noms ou sur les familles voisines citées.

```text
P Assets/UI/LoadingScreen/LoadingScreen.uss
```

### Assets/UI/MainMenu — 1 chemins

Un chemin de fragment référencé sans objet local résolu ; ne pas en déduire une structure. Utiliser GameUICore pour MainWindow.

Aucun contenu individuel inspecté dans cette famille ; recommandation fondée sur les noms ou sur les familles voisines citées.

```text
R Assets/UI/MainMenu/characterSheet/InlineGridItem_gd_carac_default.uxml
```

### Assets/UI/Map — 1 chemins

Un fragment de secteur référencé sans objet local résolu ; utiliser les objets Cartography effectivement inspectés pour WorldAtlasPanel.

Aucun contenu individuel inspecté dans cette famille ; recommandation fondée sur les noms ou sur les familles voisines citées.

```text
R Assets/UI/Map/cartographyUi/InlineGridItem_gd_sectors_default.uxml
```

### Assets/UI/Mount — 33 chemins

Panneaux gauche/droite, onglets étable/enclos, filtres et transfert ; patron utile aux transferts banque/inventaire, sans ajouter de module montures.

Objets représentatifs inspectés :

- `mountPaddock.uxml` : `ctr_global`, `ctr_left`, `ctr_right`, `ctr_equippedMount`, `tabg_filterTabGroup`, `btn_reset`, `ctr_equipped`, `tab_stable`, `tab_paddock`, `ctr_searchFilter`, `ctr_statsFilter`, `ctr_lvlFilter`, `ctr_familyFilter`, `ctr_genderFilter`, `ctr_TypeFilter`, `ctr_capacityFilter`.
- `rideTooltip.uxml` : `ctr_status`, `ctr_effect`, `ctr_genealogy`, `ctr_itemDetails`, `ctr_tooltipFooter`, `lbl_pinnedTitle`, `btn_pinnedClose`, `tx_itemImg`, `ctr_blockStatusTitle`, `ctr_blockStatus`, `ctr_blockTitle`, `ctr_blockStats`, `ctr_blockGenealogyTitle`, `ctr_blockGenealogy`, `ctr_blockItemDetailsTitle`, `ctr_blockitemDetails`.

```text
P Assets/UI/Mount/Breeding/BreedingUi.uxml
P Assets/UI/Mount/Breeding/CloningUi.uxml
P Assets/UI/Mount/Breeding/ExtractionRideLine.uxml
P Assets/UI/Mount/Breeding/ExtractionUi.uxml
P Assets/UI/Mount/Breeding/FillGaugeLine.uxml
P Assets/UI/Mount/Breeding/FillGaugeResultLine.uxml
P Assets/UI/Mount/Breeding/FillGaugeUi.uxml
P Assets/UI/Mount/Breeding/GeneticRideItem.uxml
P Assets/UI/Mount/Breeding/GeneticUi.uxml
P Assets/UI/Mount/Breeding/MatingUi.uxml
P Assets/UI/Mount/Breeding/PaddockUi.uxml
P Assets/UI/Mount/Breeding/RideSearchResultItem.uxml
P Assets/UI/Mount/Breeding/SmallRideLine.uxml
P Assets/UI/Mount/Breeding/gaugeTooltip.uxml
P Assets/UI/Mount/Breeding/matingResultLine.uxml
P Assets/UI/Mount/Breeding/rideParentLine.uxml
P Assets/UI/Mount/Breeding/rideParentUI.uxml
P Assets/UI/Mount/InlineGridItem_mountInfo_effects.uxml
P Assets/UI/Mount/InlineGridItem_mountPaddock_default.uxml
P Assets/UI/Mount/InlineGridItem_mountPaddock_inventory.uxml
P Assets/UI/Mount/mountAncestorItem.uxml
P Assets/UI/Mount/mountAncestors.uxml
P Assets/UI/Mount/mountPaddock.uss
I Assets/UI/Mount/mountPaddock.uxml
P Assets/UI/Mount/paddock/breedingLine.uxml
P Assets/UI/Mount/paddock/mountDetails.uxml
P Assets/UI/Mount/paddock/mountEmptyTiles.uxml
P Assets/UI/Mount/paddock/mountList.uxml
P Assets/UI/Mount/paddock/mountTile.uxml
P Assets/UI/Mount/paddock/mountTilesRow.uxml
P Assets/UI/Mount/paddockSellBuy.uss
P Assets/UI/Mount/paddockSellBuy.uxml
I Assets/UI/Mount/rideTooltip.uxml
```

### Assets/UI/NpcDialog — 1 chemins

Nom du PNJ, texte et zone de réponses ; adapter aux confirmations et au guidage NoCodeWizardOverlay, pas à la navigation globale.

Objets représentatifs inspectés :

- `npcDialog.uxml` : `ctr_main`, `ctr_dialogBuble`, `ctr_dialog`, `ctr_glow`, `ctr_background`, `ctr_ornament`, `ctr_npcAnswer`, `tx_glow`, `tx_background`, `tx_ornament`, `ctr_actions`, `ctr_content`, `ctr_backgroundReplies`, `tx_outline`, `lbl_text`, `ctr_header`.

```text
I Assets/UI/NpcDialog/npcDialog.uxml
```

### Assets/UI/Options — 22 chemins

Onglets et éléments booléen, slider, thème, raccourci ; appliquer à SettingsView, ProxysView et aux préférences de compte avec les mêmes rangées.

Objets représentatifs inspectés :

- `BoolOptionElement.uxml` : `ctr_boolOptionContent`, `ctr_option`, `ctr_optionDescription`, `ctr_button`, `lbl_labelDescription`, `btn_button`, `btn_description`.
- `LabelOptionElement.uxml` : `ctr_labelOptionElement`, `lbl_labelDescription`.
- `OptionBase.uxml` : `tabs`, `ctr_content`.
- `SliderOptionElement.uxml` : `ctr_sliderOptionElement`, `ctr_container`, `ctr_optionDescription`, `lbl_optionText`, `btn_description`, `lbl_labelDescription`.
- `ThemeSelectionOptionElement.uxml` : `lbl_title`, `ctr_toggleContainer`, `ctr_optionDescription`, `lbl_labelDescription`.

```text
I Assets/UI/Options/BoolOptionElement.uxml
P Assets/UI/Options/ButtonOptionElement.uxml
P Assets/UI/Options/ChatColorElement.uxml
P Assets/UI/Options/ChatColorSelectionOptionElement.uxml
P Assets/UI/Options/ColorChoiceOptionElement.uxml
P Assets/UI/Options/DividerOptionElement.uxml
P Assets/UI/Options/DuplicateShortcutPopup.uxml
P Assets/UI/Options/FolderSelectionOptionElement.uxml
I Assets/UI/Options/LabelOptionElement.uxml
P Assets/UI/Options/MultipleChoiceOptionElement.uxml
P Assets/UI/Options/NotificationOptionElement.uxml
I Assets/UI/Options/OptionBase.uxml
P Assets/UI/Options/OptionCategory.uxml
P Assets/UI/Options/OptionTab.uxml
P Assets/UI/Options/OptionToggle.uxml
P Assets/UI/Options/ShortcutErrorPopup.uxml
P Assets/UI/Options/ShortcutOptionElement.uxml
I Assets/UI/Options/SliderOptionElement.uxml
P Assets/UI/Options/SoundOptionElement.uxml
P Assets/UI/Options/TextButtonOptionElement.uxml
I Assets/UI/Options/ThemeSelectionOptionElement.uxml
P Assets/UI/Options/ThemeToggle.uxml
```

### Assets/UI/Particles — 2 chemins

Fragments décoratifs de bruit/étoile ; pas d’équivalent fonctionnel. Éviter d’en faire un motif de fond omniprésent.

Objets représentatifs inspectés :

- `NoiseParticle.uxml` : `NoiseParticle`, `name`, `noise`, `particle`, `VisualElement/UxmlSerializedData`.
- `StarParticle.uxml` : `StarParticle`, `name`, `Star`, `StarCircle`, `VisualElement/UxmlSerializedData`.

```text
I Assets/UI/Particles/NoiseParticle.uxml
I Assets/UI/Particles/StarParticle.uxml
```

### Assets/UI/Party — 13 chemins

Membres, état arène, couronne du chef et marque de suivi ; appliquer à TeamsView et à la sélection d’un chef de groupe.

Objets représentatifs inspectés :

- `partyDisplay.uxml` : `ctr_contentWindow`, `ctr_main`, `ctr_category`, `btn_arrowLeft`, `btn_arrowRight`.
- `partyMemberItem.uxml` : `tx_guest`, `tx_arenaStatus`, `tx_leaderCrown`, `ctr_iconToggles`, `tx_followArrow`, `tx_waiting`.

```text
P Assets/UI/Party/EndSeasonReward.uxml
P Assets/UI/Party/PvpArenaBase.uxml
P Assets/UI/Party/PvpArenaFights.uxml
P Assets/UI/Party/PvpArenaReward/ProgressColumnItemReward.uxml
P Assets/UI/Party/PvpArenaReward/ProgressItemReward.uxml
P Assets/UI/Party/PvpArenaReward/PvpArenaRewardsTab.uxml
P Assets/UI/Party/PvpLeagueInfoItem.uxml
P Assets/UI/Party/PvpModeItem.uxml
P Assets/UI/Party/SurrenderPopup.uxml
P Assets/UI/Party/joinParty.uxml
P Assets/UI/Party/joinPartyMember.uxml
I Assets/UI/Party/partyDisplay.uxml
I Assets/UI/Party/partyMemberItem.uxml
```

### Assets/UI/PersistentMenu — 35 chemins

Barre d’actions, menu extensible, chat et widgets pliables ; appliquer à MainWindow, ClassicShellBody et à la console de BotsView.

Objets représentatifs inspectés :

- `actionBar.uxml` : `ctr_lines`, `slot-size`, `gd_spellitemotes`, `ctr_lineTag`, `ctr_controller`, `ctr_pages`, `btn_type`, `btn_prev`, `btn_next`, `lbl_pageIndex`.
- `bannerMenu.uxml` : `ctr_moreBtn`, `gd_btnUis`, `slot-type`, `slot-size`, `slot-twinkable`, `gd_additionalBtns`.
- `chatview.uxml` : `listChat`, `btn_dialect`, `btn_news`, `ctr_toggleButtons`, `btn_addChannel`.
- `widgetManager.uxml` : `ctr_widgetMenu`, `ctr_maximized`, `btn_maximize`, `btn_settings`, `btn_minimize`.

```text
P Assets/UI/PersistentMenu/InternalNotification/big/bigInternalNotificationItem.uxml
P Assets/UI/PersistentMenu/InternalNotification/bigInternalNotification.uxml
P Assets/UI/PersistentMenu/InternalNotification/internalNotification.uxml
P Assets/UI/PersistentMenu/TitleTab/InlineGridItem_gd_orns_default.uxml
P Assets/UI/PersistentMenu/TitleTab/InlineGridItem_gd_titles_default.uxml
P Assets/UI/PersistentMenu/actionBar.uss
I Assets/UI/PersistentMenu/actionBar.uxml
P Assets/UI/PersistentMenu/alignmentTab/alignmentSpecialisation.uxml
P Assets/UI/PersistentMenu/alignmentTab/alignmentTab.uxml
I Assets/UI/PersistentMenu/bannerMenu.uxml
P Assets/UI/PersistentMenu/bannerMenu_Slot.uss
P Assets/UI/PersistentMenu/characterInformations.uss
P Assets/UI/PersistentMenu/characterInformations.uxml
P Assets/UI/PersistentMenu/characterSheet/AdvancedCharacteristicItem.uxml
P Assets/UI/PersistentMenu/characterSheet/CharacterSheet.uss
P Assets/UI/PersistentMenu/characterSheet/Components/CharacterStatsDetailed.uxml
P Assets/UI/PersistentMenu/characterSheet/Components/CompanionStatsDetailed.uxml
P Assets/UI/PersistentMenu/characterSheet/Components/EntityMainStats.uxml
P Assets/UI/PersistentMenu/characterSheet/Components/EntityStatsGraph.uxml
P Assets/UI/PersistentMenu/characterSheet/Components/EntityStatsTable.uxml
P Assets/UI/PersistentMenu/characterSheet/Components/EntityStatsTableItem.uxml
P Assets/UI/PersistentMenu/characterSheet/Components/EntityStatsTwoColumns.uxml
P Assets/UI/PersistentMenu/characterSheet/Components/MainStatItem.uxml
P Assets/UI/PersistentMenu/characterSheet/Components/MonsterBestiaryStatsDetailed.uxml
P Assets/UI/PersistentMenu/characterSheet/Components/TaxCollectorStatsDetailed.uxml
P Assets/UI/PersistentMenu/characterSheet/Components/statTwoColumnsItem.uxml
P Assets/UI/PersistentMenu/characterSheet/StatBoostItem.uxml
P Assets/UI/PersistentMenu/characterSheet/StatBoostMinimizedUi.uxml
P Assets/UI/PersistentMenu/characterSheet/StatBoostUi.uxml
P Assets/UI/PersistentMenu/characterSheet/characterSheetUi.uxml
P Assets/UI/PersistentMenu/characterSheet/socialButton.uxml
P Assets/UI/PersistentMenu/chat.uss
I Assets/UI/PersistentMenu/chatview.uxml
I Assets/UI/PersistentMenu/widgetManager.uxml
P Assets/UI/PersistentMenu/widgetProgressGauge.uxml
```

### Assets/UI/PlayerInfo — 7 chemins

Fiche personnage avec en-tête, onglets et équipement ; appliquer aux fiches de AccountsView et MyAccountView.

Objets représentatifs inspectés :

- `PlayerInfoUi.uxml` : `ctr_charac`, `tabg_tabs`, `ctr_content`, `ctr_footer`, `ctr_caracHeader`, `ctr_caracContent`, `tx_background`, `ctr_entity`, `ctr_arrow`, `ctr_tabContent`, `ctr_tag`, `btn_contact`, `ctr_characterInfo`, `ctr_entityMainStats`, `ctr_detailedStats`, `btn_arrowLeft`.

```text
P Assets/UI/PlayerInfo/PlayerInfoEquipment.uxml
P Assets/UI/PlayerInfo/PlayerInfoLadderLine.uxml
P Assets/UI/PlayerInfo/PlayerInfoMain.uxml
P Assets/UI/PlayerInfo/PlayerInfoRanking.uxml
I Assets/UI/PlayerInfo/PlayerInfoUi.uxml
P Assets/UI/PlayerInfo/backgroundSelectionUi.uxml
P Assets/UI/PlayerInfo/backgroundTile.uxml
```

### Assets/UI/Quest — 4 chemins

Suivi d’objectif compact distinct du journal complet ; appliquer aux étapes de TreasureView et NoCodeWizardOverlay.

Objets représentatifs inspectés :

- `questList.uxml` : `questList`, `name`, `QuestList`, `drag_border-top-color`, `#FF0000FF`, `drag_border-left-color`, `#00F327FF`, `drag_border-bottom-color`.
- `questList_objective.uxml` : `ctr_objective`, `lbl_objective`.

```text
I Assets/UI/Quest/questList.uxml
P Assets/UI/Quest/questList/questList.uss
I Assets/UI/Quest/questList/questList_objective.uxml
P Assets/UI/Quest/questList/questList_quest.uxml
```

### Assets/UI/Recycle — 2 chemins

Objet, bonus, total et action finale ; utile au récapitulatif économique d’EcoToolsPanel, sans ajouter une mécanique non demandée.

Objets représentatifs inspectés :

- `recycleUi.uxml` : `ctr_distribution`, `ctr_header`, `ctr_bonus`, `ctr_total`, `btn_recycle`, `lbl_distribution`, `lbl_itemToRecycle`, `tx_bonusHelp`, `lbl_bonus`, `ctr_controlZoneBonus`, `ctr_totalNuggets`, `ctr_estimatedValue`, `lbl_character`, `lbl_alliance`, `lbl_controlZoneBonus`, `tx_controlZoneHelp`.

```text
P Assets/UI/Recycle/recycleItem.uxml
I Assets/UI/Recycle/recycleUi.uxml
```

### Assets/UI/RolePlay — 12 chemins

Chasses, étapes, essais restants et validation ; appliquer à TreasureView et aux états de route dans RoadRecorderOverlays.

Objets représentatifs inspectés :

- `legendaryHunts.uxml` : `ctr_content`, `ctr_left`, `ctr_right`, `gd_hunts`, `lbl_noHunts`, `ctr_checkbox`, `lbl_noHuntSelected`, `ctr_rightPanel`, `ctr_huntContainer`, `ctr_button`, `ctr_info`, `ctr_picture`, `ctr_craft`, `btn_start`, `lbl_huntName`, `lbl_level`.
- `treasureHunt.uxml` : `lbl_portalName`, `ctr_stage`, `lbl_remainingTests`, `btn_valid`, `lbl_stageCaption`, `lbl_stage`, `ctr_location`, `lbl_subZone`, `lbl_start`.
- `dialogLine.uxml` : `ctr_dialogLine`, `ctr_background`, `ctr_content`, `ctr_colorBackground`, `ctr_icons`, `lbl_textReply`.

```text
P Assets/UI/RolePlay/FightMapLegend.uxml
P Assets/UI/RolePlay/FightMapPreview.uxml
P Assets/UI/RolePlay/LegendaryHunts/InlineGridItem_gd_hunts_default.uxml
I Assets/UI/RolePlay/LegendaryHunts/legendaryHunts.uxml
P Assets/UI/RolePlay/LegendaryHunts/legendaryHunts_Slot.uss
P Assets/UI/RolePlay/LevelUp/InlineGridItem_gd_rewards_LevelUp_default.uxml
P Assets/UI/RolePlay/LevelUp/LevelUp.uxml
P Assets/UI/RolePlay/TreasureHunt/Steps_default.uxml
I Assets/UI/RolePlay/TreasureHunt/treasureHunt.uxml
P Assets/UI/RolePlay/cinematic.uxml
I Assets/UI/RolePlay/dialogLine.uxml
P Assets/UI/RolePlay/hardcoreDeathPopup/hardcoreDeathPopup.uxml
```

### Assets/UI/Social — 123 chemins

Amis, guilde, alliance, recrutement et journaux ; appliquer le patron liste/détail et les états de présence à TeamsView et AccountsView.

Objets représentatifs inspectés :

- `friends.uxml` : `ctr_friends`, `ctr_header`, `ctr_content`, `ctr_tabs`, `ctr_status`, `ctr_ankama`, `ctr_inGame`, `ctr_blocked`, `tabs_group`, `ctr_name`, `lbl_ankamaFriendsUnavailable`, `ctr_ankamaContent`, `ctr_ankamaFooter`, `ctr_inGameContent`, `ctr_inGameFooter`, `ctr_blockedContent`.
- `guild.uxml` : `ctr_content`, `ctr_guild`, `ctr_guildTabs`, `btn_prezAndRecruit`, `ctr_guildInfo`, `ctr_guildKamas`, `ctr_guildMotd`, `btn_guildMembers`, `btn_missions`, `btn_raids`, `btn_logBook`, `btn_fights`, `btn_taxCollector`, `btn_directory`, `ctr_guildEmblem`, `ctr_guildNameAndLevel`.
- `guildBase.uxml` : `guildBase`, `name`, `socialBase`, `title`, `[UI.COMMON.GUILD]`, `header-style`, `Primary`, `GuildBaseUI`.

```text
P Assets/UI/Social/Alliance/AllianceAVA.uxml
P Assets/UI/Social/Alliance/AllianceAVAItem.uxml
P Assets/UI/Social/Alliance/AllianceAVALine.uxml
P Assets/UI/Social/Alliance/AllianceDirectory.uxml
P Assets/UI/Social/Alliance/AllianceFight.uxml
P Assets/UI/Social/Alliance/AllianceFightLine.uxml
P Assets/UI/Social/Alliance/AllianceLogbook/allianceLogbook.uxml
P Assets/UI/Social/Alliance/AllianceNuggetValidItem.uxml
P Assets/UI/Social/Alliance/AllianceNuggetValidPopup.uxml
P Assets/UI/Social/Alliance/CharacterSlot.uxml
P Assets/UI/Social/Alliance/TaxCollectorEquipment.uxml
P Assets/UI/Social/Alliance/alliance.uxml
P Assets/UI/Social/Alliance/allianceConquestItem.uxml
P Assets/UI/Social/Alliance/allianceConquests.uxml
P Assets/UI/Social/Alliance/allianceMemberItem.uxml
P Assets/UI/Social/Alliance/allianceMembers.uxml
P Assets/UI/Social/Alliance/allianceNuggets.uxml
P Assets/UI/Social/Alliance/nuggetsMemberItem.uxml
P Assets/UI/Social/Alliance/taxCollectorCharacLine.uxml
P Assets/UI/Social/Alliance/taxCollectorCharacLineDmgRes.uxml
P Assets/UI/Social/Alliance/taxCollectorPersonalization.uxml
P Assets/UI/Social/CreateRank.uxml
P Assets/UI/Social/Guild/GuildHouseManager.uxml
P Assets/UI/Social/Guild/GuildJoinPopup.uxml
P Assets/UI/Social/Guild/GuildLevelRewards/guildLevelRewardsItem.uxml
P Assets/UI/Social/Guild/GuildLevelRewards/guildLevelRewardsUI.uxml
P Assets/UI/Social/Guild/GuildLine.uxml
P Assets/UI/Social/Guild/GuildShop/guildArticle.uxml
P Assets/UI/Social/Guild/GuildShop/guildShop.uxml
P Assets/UI/Social/Guild/GuildShop/guildShopConfirmBuyPopup.uxml
P Assets/UI/Social/Guild/GuildUnsetUi.uxml
P Assets/UI/Social/Guild/RecruitmentTypeItem.uxml
P Assets/UI/Social/Guild/guildApply.uss
P Assets/UI/Social/Guild/guildApply.uxml
P Assets/UI/Social/Guild/guildContributionUi.uxml
P Assets/UI/Social/Guild/guildCustomization.uss
P Assets/UI/Social/Guild/guildCustomization.uxml
P Assets/UI/Social/Guild/guildCustomization/guildSpellItem.uxml
P Assets/UI/Social/Guild/guildCustomization/guildStatItem.uxml
P Assets/UI/Social/Guild/guildHouseItem.uxml
P Assets/UI/Social/Guild/guildHouses.uxml
P Assets/UI/Social/Guild/guildLogBook/InlineGridItem_gd_logbook_default.uxml
P Assets/UI/Social/Guild/guildLogBook/guildLogbook.uxml
P Assets/UI/Social/Guild/guildMemberItem.uxml
P Assets/UI/Social/Guild/guildMembers.uxml
P Assets/UI/Social/Guild/guildMissions.uxml
P Assets/UI/Social/Guild/guildMissions/guildMissionCard.uxml
P Assets/UI/Social/Guild/guildMissions/guildMissionOption/guildMissionRanksAndCategoriesOption.uxml
P Assets/UI/Social/Guild/guildMissions/guildMissionOption/guildMissionTierOption.uxml
P Assets/UI/Social/Guild/guildMissions/guildMissionSubscriberItem.uxml
P Assets/UI/Social/Guild/guildMissions/guildMissionSubscribers.uxml
P Assets/UI/Social/Guild/guildNoteEditingPopup.uxml
P Assets/UI/Social/Guild/guildPaddock.uss
P Assets/UI/Social/Guild/guildPaddock.uxml
P Assets/UI/Social/Guild/guildPaddock/mountItem.uxml
P Assets/UI/Social/Guild/guildPaddock/paddockItem.uxml
P Assets/UI/Social/Guild/guildPaddockInfo.uxml
P Assets/UI/Social/Guild/guildPaddockInfoItem.uxml
P Assets/UI/Social/Guild/guildPaddockItem.uxml
P Assets/UI/Social/Guild/guildRaids/guildRaidItem.uxml
P Assets/UI/Social/Guild/guildRaids/guildRaidMemberItem.uxml
P Assets/UI/Social/Guild/guildRaids/guildRaidPopup.uxml
P Assets/UI/Social/Guild/guildRaids/guildRaidRewards.uxml
P Assets/UI/Social/Guild/guildRaids/guildRaidScore.uxml
P Assets/UI/Social/Guild/guildRaids/guildRaidVariableItem.uxml
P Assets/UI/Social/Guild/guildRaids/guildRaids.uxml
P Assets/UI/Social/Guild/guildRaids/partyRaidMemberItem.uxml
P Assets/UI/Social/Guild/guildRaids/raidFinalScoreItem.uxml
P Assets/UI/Social/Guild/guildRaids/raidObjectiveItem.uxml
P Assets/UI/Social/Guild/guildRaids/raidTracking.uxml
P Assets/UI/Social/Guild/guildRaids/raidTrackingMembersGroupItem.uxml
P Assets/UI/Social/Guild/guildRaids/raidTrackingObjectiveItem.uxml
P Assets/UI/Social/Guild/guildRaids/raidTrackingObjectivesGroupItem.uxml
P Assets/UI/Social/Guild/guildWeeklyActivityRewards.uxml
P Assets/UI/Social/Guild/rankIconItem.uxml
P Assets/UI/Social/GuildDirectory.uss
P Assets/UI/Social/GuildDirectory.uxml
P Assets/UI/Social/GuildItem.uss
P Assets/UI/Social/GuildItem.uxml
P Assets/UI/Social/GuildMemberRights/guildMemberRights_Rank.uxml
P Assets/UI/Social/ModifyRank.uxml
P Assets/UI/Social/RemoveRank.uxml
P Assets/UI/Social/RightsAndRanks.uxml
P Assets/UI/Social/SocialApplications.uxml
P Assets/UI/Social/SocialApplicationsItem.uxml
P Assets/UI/Social/SocialGroupCard.uxml
P Assets/UI/Social/SocialGroupCreator.uxml
P Assets/UI/Social/SocialJoinApplyPopup.uxml
P Assets/UI/Social/SocialRecruitment.uxml
P Assets/UI/Social/addFriend.uss
P Assets/UI/Social/addFriend.uxml
P Assets/UI/Social/allianceBase.uxml
P Assets/UI/Social/allianceLine.uxml
P Assets/UI/Social/collectedTaxCollector.uss
P Assets/UI/Social/collectedTaxCollector.uxml
P Assets/UI/Social/collectedTaxCollector/collectedTaxCollectorItem.uxml
P Assets/UI/Social/friends.uss
I Assets/UI/Social/friends.uxml
P Assets/UI/Social/friends/DofusPlayerLine.uxml
P Assets/UI/Social/friends/InlineGridItem_gd_friends_blockedItemCtr.uxml
P Assets/UI/Social/friends/InlineGridItem_gd_friends_ctr_cat.uxml
P Assets/UI/Social/friends/InlineGridItem_gd_friends_ctr_contact.uxml
P Assets/UI/Social/friends/InlineGridItem_gd_friends_ctr_launcher_friend.uxml
P Assets/UI/Social/friends/InlineGridItem_gd_friends_ctr_message.uxml
P Assets/UI/Social/friends/InlineGridItem_gd_friends_ctr_sort.uxml
P Assets/UI/Social/friends/InlineGridItem_gd_friends_ctr_sort_blocked.uxml
P Assets/UI/Social/friends/InlineGridItem_gd_friends_ctr_sort_friend.uxml
P Assets/UI/Social/friends/InlineGridItem_gd_pendingInvitations_default.uxml
P Assets/UI/Social/friends/TaxCollectorLootDetail.uxml
P Assets/UI/Social/friends/TaxCollectorLootLine.uxml
P Assets/UI/Social/friends/socialPlayerLine.uxml
P Assets/UI/Social/guild.uss
I Assets/UI/Social/guild.uxml
I Assets/UI/Social/guildBase.uxml
P Assets/UI/Social/guildCreator_Slot.uss
P Assets/UI/Social/houseItem.uxml
P Assets/UI/Social/socialGroupDirectoryItem.uxml
P Assets/UI/Social/socialMemberItemSimplified.uxml
P Assets/UI/Social/socialMemberRights.uxml
P Assets/UI/Social/socialRankItem.uxml
P Assets/UI/Social/spouse.uxml
P Assets/UI/Social/topTaxCollectorItem.uxml
P Assets/UI/Social/topTaxCollectors.uxml
```

### Assets/UI/TSS — 33 chemins

33 thèmes de taille/palette ; inspiration pour les jetons globaux, pas 33 écrans. Valeurs calculées et thème actif non récupérés.

Aucun contenu individuel inspecté dans cette famille ; recommandation fondée sur les noms ou sur les familles voisines citées.

```text
P Assets/UI/TSS/LargeTss/LargeFont_Belladone.tss
P Assets/UI/TSS/LargeTss/LargeFont_Bonta.tss
P Assets/UI/TSS/LargeTss/LargeFont_Brakmar.tss
P Assets/UI/TSS/LargeTss/LargeFont_EmeraldMine.tss
P Assets/UI/TSS/LargeTss/LargeFont_GoldAndSteel.tss
P Assets/UI/TSS/LargeTss/LargeFont_Pandala.tss
P Assets/UI/TSS/LargeTss/LargeFont_Standard.tss
P Assets/UI/TSS/LargeTss/LargeFont_Sufokia.tss
P Assets/UI/TSS/LargeTss/LargeFont_Tribute.tss
P Assets/UI/TSS/LargeTss/LargeFont_Unicorn.tss
P Assets/UI/TSS/LargeTss/LargeFont_Wabbit.tss
P Assets/UI/TSS/MediumTss/MediumFont_Belladone.tss
P Assets/UI/TSS/MediumTss/MediumFont_Bonta.tss
P Assets/UI/TSS/MediumTss/MediumFont_Brakmar.tss
P Assets/UI/TSS/MediumTss/MediumFont_EmeraldMine.tss
P Assets/UI/TSS/MediumTss/MediumFont_GoldAndSteel.tss
P Assets/UI/TSS/MediumTss/MediumFont_Pandala.tss
P Assets/UI/TSS/MediumTss/MediumFont_Standard.tss
P Assets/UI/TSS/MediumTss/MediumFont_Sufokia.tss
P Assets/UI/TSS/MediumTss/MediumFont_Tribute.tss
P Assets/UI/TSS/MediumTss/MediumFont_Unicorn.tss
P Assets/UI/TSS/MediumTss/MediumFont_Wabbit.tss
P Assets/UI/TSS/SmallTss/SmallFont_Belladone.tss
P Assets/UI/TSS/SmallTss/SmallFont_Bonta.tss
P Assets/UI/TSS/SmallTss/SmallFont_Brakmar.tss
P Assets/UI/TSS/SmallTss/SmallFont_EmeraldMine.tss
P Assets/UI/TSS/SmallTss/SmallFont_GoldAndSteel.tss
P Assets/UI/TSS/SmallTss/SmallFont_Pandala.tss
P Assets/UI/TSS/SmallTss/SmallFont_Standard.tss
P Assets/UI/TSS/SmallTss/SmallFont_Sufokia.tss
P Assets/UI/TSS/SmallTss/SmallFont_Tribute.tss
P Assets/UI/TSS/SmallTss/SmallFont_Unicorn.tss
P Assets/UI/TSS/SmallTss/SmallFont_Wabbit.tss
```

### Assets/UI/Taxi — 2 chemins

Destination, niveau, coordonnées et coût ; appliquer à la liste de trajets et transports de WorldAtlasPanel.

Objets représentatifs inspectés :

- `zaapListEntry.uxml` : `lbl_zaapArea`, `lbl_level`, `lbl_zaapSubArea`, `lbl_guardianName`, `lbl_zaapCoord`, `lbl_zaapCost`.

```text
I Assets/UI/Taxi/zaapListEntry.uxml
P Assets/UI/Taxi/zaapiSelection.uxml
```

### Assets/UI/TradeCenter — 35 chemins

HDV achat/vente, historique, boutique PNJ et prix ; appliquer à HdvSellOverlay, HdvUidOperatorWindow, MarketInsightsPanel et EcoToolsPanel.

Objets représentatifs inspectés :

- `AuctionHouseItemDetailBuy.uxml` : `ctr_itemDetails`, `ctr_bgItemDetails`, `tx_item`, `ctr_loot`, `btn_buy`, `lbl_quantity`, `lbl_itemPrice`, `lbl_loot`.
- `AuctionHouseItemsList.uxml` : `ctr_content`, `ctr_favorites`, `ctr_itemList`, `ctr_noItem`, `ctr_favoritesList`, `gd_items`, `lbl_noItem`, `btn_filter`, `tx_item`, `lbl_itemName`, `ctr_levelAndCategory`, `ctr_inventoryQuantity`, `ctr_bankQuantity`, `ctr_havenBagQuantity`, `lbl_itemSet`, `lbl_level`.
- `auctionHouseBuy.uxml` : `ctr_buyTab`, `lbl_selectCategory`, `ctr_grid`, `btn_displayAllItems`, `btn_tabName`, `btn_tabLevel`, `btn_tabPrice`.
- `AuctionHouseSell_ItemLine.uxml` : `btn_item`, `tx_itemBg`, `lbl_itemName`, `lbl_storeTime`, `lbl_itemStack`, `tx_itemIcon`, `lbl_itemPrice`.
- `auctionHouseModifyMultiplePopup.uxml` : `ctr_itemList`, `ctr_text`, `lbl_totalItems`, `btn_tabName`, `btn_tabStack`, `btn_tabPrice`, `btn_tabTax`, `gd_itemList`, `lbl_totaloldPrice`, `lbl_totalPrice`, `lbl_totalTax`, `ctr_contentList`.
- `auctionHousePutOnSellPopup.uxml` : `ctr_putOnSell`, `ctr_itemInfo`, `ctr_tax`, `lbl_warning`, `tx_iconBg`, `lbl_itemName`, `ctr_price`, `ctr_totalPrice`, `tx_itemBg`, `lbl_itemQuantity`, `lbl_unityPriceTitle`, `lbl_priceTitle`, `lbl_taxTitle`, `lbl_unityPrice`, `lbl_price`, `lbl_tax`.
- `auctionHouseSell.uxml` : `ctr_tab`, `ctr_otherItem`, `ctr_otherItemHeader`, `lbl_selectCategory`, `ctr_header`, `lbl_quantityObject`, `ctr_footerKamas`, `lbl_currentlyOnSale`, `ctr_tableHeaderOtherItem`, `gd_otherItemList`, `ctr_search`, `ctr_tableHeader`, `gd_itemList`, `lbl_estimatedValue`, `ctr_estimatedValue`, `btn_tabOtherPrice`.
- `salesHistory.uxml` : `ctr_content`, `ctr_mainContent`, `ctr_tableHeader`, `ctr_noResult`, `ctr_details`, `inp_search`, `ctr_grow`, `ctr_fixed`, `lbl_noResult`, `ctr_checkboxes`, `ctr_totalKamas`, `lbl_from`, `inp_dateFrom`, `lbl_to`, `inp_dateTo`, `lbl_itemsSold`.
- `npcStore.uxml` : `ctr_header`, `ctr_tabGroup`, `btn_buy`, `btn_sell`, `ctr_filters`, `ctr_main`, `ctr_footer`, `ctr_footerBuy`, `ctr_footerSell`, `inp_search`, `ctr_noItems`, `ctr_totalValue`, `btn_sellAll`, `lbl_noItem`, `lbl_myKamas`, `lbl_total`.

```text
I Assets/UI/TradeCenter/AuctionHouse/Buy/AuctionHouseItemDetailBuy.uxml
I Assets/UI/TradeCenter/AuctionHouse/Buy/AuctionHouseItemsList.uxml
P Assets/UI/TradeCenter/AuctionHouse/Buy/EquipmentTypeItem.uxml
P Assets/UI/TradeCenter/AuctionHouse/Buy/ObjectItem_buy.uxml
P Assets/UI/TradeCenter/AuctionHouse/Buy/auctionHouseBuy.uss
I Assets/UI/TradeCenter/AuctionHouse/Buy/auctionHouseBuy.uxml
P Assets/UI/TradeCenter/AuctionHouse/Buy/auctionHouseBuyPopup.uxml
P Assets/UI/TradeCenter/AuctionHouse/Buy/auctionHouseEffectFilter.uxml
P Assets/UI/TradeCenter/AuctionHouse/Buy/effectFilter.uxml
P Assets/UI/TradeCenter/AuctionHouse/Buy/filtersUiComponent.uss
P Assets/UI/TradeCenter/AuctionHouse/Buy/searchResultItem.uxml
I Assets/UI/TradeCenter/AuctionHouse/Sell/Grid Line/AuctionHouseSell_ItemLine.uxml
P Assets/UI/TradeCenter/AuctionHouse/Sell/Grid Line/AuctionHouseSell_OtherItemLine.uxml
I Assets/UI/TradeCenter/AuctionHouse/Sell/Popup/auctionHouseModifyMultiplePopup.uxml
P Assets/UI/TradeCenter/AuctionHouse/Sell/Popup/auctionHouseModifyMultiplePopupLine.uxml
I Assets/UI/TradeCenter/AuctionHouse/Sell/Popup/auctionHousePutOnSellPopup.uxml
P Assets/UI/TradeCenter/AuctionHouse/Sell/Popup/auctionHouseSellPopup.uxml
P Assets/UI/TradeCenter/AuctionHouse/Sell/Popup/auctionHouseWithdrawPopup.uxml
P Assets/UI/TradeCenter/AuctionHouse/Sell/Popup/auctionHouseWithdrawPopupLine.uxml
I Assets/UI/TradeCenter/AuctionHouse/Sell/auctionHouseSell.uxml
P Assets/UI/TradeCenter/AuctionHouse/auctionHouse.uxml
P Assets/UI/TradeCenter/EstateAgency/estateAgency.uxml
P Assets/UI/TradeCenter/EstateAgency/estateAgencyResult.uxml
P Assets/UI/TradeCenter/InlineGridItem_stock.uxml
P Assets/UI/TradeCenter/SalesHistory/saleHistoryItem.uxml
I Assets/UI/TradeCenter/SalesHistory/salesHistory.uxml
P Assets/UI/TradeCenter/itemNpcStore.uss
P Assets/UI/TradeCenter/marketPrice.uxml
P Assets/UI/TradeCenter/npcStore/confirmBuyPopup.uxml
P Assets/UI/TradeCenter/npcStore/confirmSellNPCLine.uxml
P Assets/UI/TradeCenter/npcStore/confirmSellPopup.uxml
I Assets/UI/TradeCenter/npcStore/npcStore.uxml
P Assets/UI/TradeCenter/npcStore/npcStoreItem.uxml
P Assets/UI/TradeCenter/stock.uss
P Assets/UI/TradeCenter/stock.uxml
```

### Assets/UI/Tutorial — 2 chemins

Étape et sortie de tutoriel ; appliquer à NoCodeWizardOverlay en gardant la possibilité de quitter.

Objets représentatifs inspectés :

- `tutorialUi.uxml` : `ctr_quit`, `btn_leaveTutorial`, `lbl_stepName`.

```text
P Assets/UI/Tutorial/tutorialUi.uss
I Assets/UI/Tutorial/tutorialUi.uxml
```

### Assets/UI/Utils — 103 chemins

Tooltips, slots, listes, onglets et composants communs ; appliquer une seule fois dans les styles partagés de tous les modules.

Objets représentatifs inspectés :

- `DefaultButton.uxml` : `DefaultButton`, `name`, `largePopup`, `DofusButtonCustom/UxmlSerializedData`.
- `EmptySlot.uxml` : `slot_background`, `slot-item__background`, `slot_effect`, `slot-item__effect`.
- `HouseTooltipLine.uxml` : `lbl_text`, `ctr_price`, `ctr_guild`, `lbl_price`, `lbl_guildDash`, `lbl_guild`.
- `ScrollContainer.uxml` : `ScrollContainer`, `view-data-key`, `ScrollView`, `name`, `mode`, `VerticalAndHorizontal`, `horizontal-page-size`, `vertical-page-size`.
- `ChatTooltip.uxml` : `lbl_text`.
- `StatsTooltip.uxml` : `ctr_title`, `ctr_content`, `lbl_title`.
- `StatsTooltipStatLine.uxml` : `lbl_statName`, `lbl_statValue`.
- `fightMarkTooltip.uxml` : `tx_gradient`, `ctr_header`, `ctr_description`, `ctr_effects`, `ctr_footer`, `ctr_headerContent`, `lbl_description`, `ctr_input`, `ctr_spellTitle`, `lbl_markType`, `ctr_caster`, `lbl_cycleAction`, `lbl_cycleIndex`, `lbl_spellName`, `lbl_from`, `lbl_casterName`.

```text
P Assets/UI/Utils/Style/ColorPickerStyleSheet.uss
P Assets/UI/Utils/Style/Common.uss
P Assets/UI/Utils/Style/Config.uss
P Assets/UI/Utils/Style/Core.UILogic.Components.Block.uss
P Assets/UI/Utils/Style/Core.UILogic.Components.Button.uss
P Assets/UI/Utils/Style/Core.UILogic.Components.CollectionView.uss
P Assets/UI/Utils/Style/Core.UILogic.Components.Combobox.uss
P Assets/UI/Utils/Style/Core.UILogic.Components.ContextMenu.uss
P Assets/UI/Utils/Style/Core.UILogic.Components.Cursor.uss
P Assets/UI/Utils/Style/Core.UILogic.Components.DefaultButton.uss
P Assets/UI/Utils/Style/Core.UILogic.Components.ElidableMultilineLabel.uss
P Assets/UI/Utils/Style/Core.UILogic.Components.Fanion.uss
P Assets/UI/Utils/Style/Core.UILogic.Components.GridView.uss
P Assets/UI/Utils/Style/Core.UILogic.Components.InputField.uss
P Assets/UI/Utils/Style/Core.UILogic.Components.ProgressBar.uss
P Assets/UI/Utils/Style/Core.UILogic.Components.ScrollContainer.uss
P Assets/UI/Utils/Style/Core.UILogic.Components.Tabs.uss
P Assets/UI/Utils/Style/Core.UILogic.Components.Toggle.uss
P Assets/UI/Utils/Style/Core.UILogic.Components.ToggleButton.uss
P Assets/UI/Utils/Style/Core.UILogic.Components.Tooltip.uss
P Assets/UI/Utils/Style/Core.UILogic.Components.Tutorial.uss
P Assets/UI/Utils/Style/Core.UILogic.Components.Window.uss
P Assets/UI/Utils/Style/EquipmentComponent.uss
P Assets/UI/Utils/Style/GenericFilterCategory.uss
P Assets/UI/Utils/Style/LabelGridRenderer.uss
P Assets/UI/Utils/Style/SlotStyleSheet.uss
P Assets/UI/Utils/Style/SlotTestStyleSheet.uss
P Assets/UI/Utils/Style/tabs.uss
P Assets/UI/Utils/UXML/CharacterAdvancedStats.uxml
P Assets/UI/Utils/UXML/CharacterMainStats.uxml
P Assets/UI/Utils/UXML/CharacterSecondaryStats.uxml
P Assets/UI/Utils/UXML/ColorPicker.uxml
I Assets/UI/Utils/UXML/DefaultButton.uxml
I Assets/UI/Utils/UXML/EmptySlot.uxml
P Assets/UI/Utils/UXML/EntityDisplayComponent.uxml
P Assets/UI/Utils/UXML/EquipmentComponent.uxml
P Assets/UI/Utils/UXML/FiltersUiComponent.uxml
I Assets/UI/Utils/UXML/HouseTooltipLine.uxml
P Assets/UI/Utils/UXML/InventoryItemStyle.uss
P Assets/UI/Utils/UXML/LabelGridRenderer.uxml
P Assets/UI/Utils/UXML/MapGroupElement.uxml
P Assets/UI/Utils/UXML/MonsterTooltipLine.uxml
P Assets/UI/Utils/UXML/MultipleComboboxWithImageEntry.uxml
P Assets/UI/Utils/UXML/ProbTooltipStatLine.uxml
I Assets/UI/Utils/UXML/ScrollContainer.uxml
P Assets/UI/Utils/UXML/Tooltips/AlignmentCharacterTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/AlterationTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/BreachRewardTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/BuffTooltipPart.uxml
P Assets/UI/Utils/UXML/Tooltips/ChallengeTooltip.uxml
I Assets/UI/Utils/UXML/Tooltips/ChatTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/DelayedActionTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/EntityDebugTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/GuildActivityRewardsTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/GuildActivityTokenTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/HouseTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/InfiniteDreamPartnersTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/InteractiveElementTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/ItemTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/ItemTooltipBlock.uxml
P Assets/UI/Utils/UXML/Tooltips/JobPlayerCardTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/JobPlayerCardTooltipItem.uxml
P Assets/UI/Utils/UXML/Tooltips/MainStatDetailsTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/MarketPriceTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/MonsterCharacteristicsTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/MysteryBoxTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/OrnamentTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/OrnamentsCharacterTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/PaddockItemTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/PaddockMountTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/PetFoodTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/PingHelpTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/PlayerFighterTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/PlayerFighterTooltip/DamageElement.uxml
P Assets/UI/Utils/UXML/Tooltips/SellItemCriterionTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/ShortcutItem.uxml
P Assets/UI/Utils/UXML/Tooltips/ShortcutTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/SignTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/SimpleInterfaceTutoTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/SmileyTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/SpellPairTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/SpellTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/StatFloorsTooltip.uxml
I Assets/UI/Utils/UXML/Tooltips/StatsTooltip.uxml
I Assets/UI/Utils/UXML/Tooltips/StatsTooltipStatLine.uxml
P Assets/UI/Utils/UXML/Tooltips/TemporaryPingHelpTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/TextShortcutTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/TextShortcutTooltipLine.uxml
P Assets/UI/Utils/UXML/Tooltips/TextTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/TextWithTextureTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/TextWithTitleTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/TextureListTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/ThinkTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/WorldRpCharacterTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/WorldRpMonstersGroupTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/WorldRpPortalTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/buffHelpTooltip.uxml
I Assets/UI/Utils/UXML/Tooltips/fightMarkTooltip.uxml
P Assets/UI/Utils/UXML/Tooltips/fighterInfo.uxml
P Assets/UI/Utils/UXML/Tooltips/multilineTextTooltip.uxml
P Assets/UI/Utils/UXML/VideoPlayer.uxml
P Assets/UI/Utils/UXML/buffTooltip.uxml
P Assets/UI/Utils/UXML/effectLine.uxml
```

### Assets/UI/Web — 25 chemins

Navigation boutique, monnaie et contenu embarqué ; seuls les patrons d’onglets/synthèse sont utiles à MyAccountView. Aucun appel web ou achat effectué.

Objets représentatifs inspectés :

- `webBase.uxml` : `ctr_baseWindow`, `tx_line`, `ctr_buttons`, `ctr_moneyBar`, `ctr_tabContent`, `ctr_content`, `ctr_leftBase`, `ctr_money`, `ctr_warningConsume`, `ctr_links`, `ctr_shop`, `ctr_subscription`, `ctr_kamasTemporaryAmount`, `ctr_linkedOgrines`, `lbl_warningConsume`, `btn_seeOgrinePrice`.

```text
P Assets/UI/Web/BAK/bakTab.uxml
P Assets/UI/Web/BAK/offerListItem.uxml
P Assets/UI/Web/Gift/codeResultItem.uxml
P Assets/UI/Web/Gift/codeResultPopup.uxml
P Assets/UI/Web/Gift/giftListItem.uxml
P Assets/UI/Web/Gift/mysteryBoxPopup.uxml
P Assets/UI/Web/Gift/retrieveConfirmationPopup.uxml
P Assets/UI/Web/Gift/webCodesAndGifts.uxml
P Assets/UI/Web/ModalCover.uxml
P Assets/UI/Web/Popups/bakPopup.uxml
P Assets/UI/Web/Shop/ShopNew/ArticleCarousel.uxml
P Assets/UI/Web/Shop/ShopNew/ArticleCarouselItem.uxml
P Assets/UI/Web/Shop/ShopNew/ArticleComposition.uxml
P Assets/UI/Web/Shop/ShopNew/ArticleCompositionSlot.uxml
P Assets/UI/Web/Shop/ShopNew/ArticleTile.uxml
P Assets/UI/Web/Shop/ShopNew/CatalogCarousel.uxml
P Assets/UI/Web/Shop/ShopNew/CatalogCarouselItem.uxml
P Assets/UI/Web/Shop/ShopNew/DescriptionPopup.uxml
P Assets/UI/Web/Shop/ShopNew/MultipleChoiceReferences.uxml
P Assets/UI/Web/Shop/ShopNew/MultipleChoiceSlot.uxml
P Assets/UI/Web/Shop/ShopNew/Shop.uxml
P Assets/UI/Web/Shop/ShopNew/articleNotice.uxml
P Assets/UI/Web/Shop/ShopNew/shopPopupConfirmBuy.uss
P Assets/UI/Web/Shop/ShopNew/shopPopupConfirmBuy.uxml
I Assets/UI/Web/webBase.uxml
```

### Assets/UIToolkit-Importing/USS — 153 chemins

Styles importés nommés par vues et fragments ; conserver leur index pour retrouver les variantes, sans les présenter comme des layouts autonomes.

Aucun contenu individuel inspecté dans cette famille ; recommandation fondée sur les noms ou sur les familles voisines citées.

```text
P Assets/UIToolkit-Importing/USS/InlineGridItem_gdZone_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_achievements_ctr_achievement.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_achievements_ctr_rewards.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_breedAllRoles_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_breed_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_cardObjectives_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_categoryArticles_ctr_articleSlot.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_character_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_crafters_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_estates_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_fighters_ctr_fighter.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_filters_ctr_filterItem.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_filters_ctr_searchBar.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_forgettableSpells_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_friends_blockedItemCtr.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_friends_ctr_cat.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_friends_ctr_launcher_friend.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_giftList_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_gifts_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_heads_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_houses_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_itemEffects_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_itemList_ctr_item.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_itemList_ctr_itemDetails.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_itemList_ctr_itemLine.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_itemList_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_itemType_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_monsters_ctr_details.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_otherItemList_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_pendingInvitations_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_quests_ctr_objective.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_quests_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_shop_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_smileyCategories_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_specializations_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_spellSets_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_gd_steps_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_grid_default.uss
P Assets/UIToolkit-Importing/USS/InlineGridItem_grid_house_default.uss
P Assets/UIToolkit-Importing/USS/LevelUp.uss
P Assets/UIToolkit-Importing/USS/SubhintEditorUi.uss
P Assets/UIToolkit-Importing/USS/SubhintList.uss
P Assets/UIToolkit-Importing/USS/administrablePopup.uss
P Assets/UIToolkit-Importing/USS/alignmentGiftsTab.uss
P Assets/UIToolkit-Importing/USS/alignmentTab.uss
P Assets/UIToolkit-Importing/USS/alignmentWarEffortTab.uss
P Assets/UIToolkit-Importing/USS/alliance.uss
P Assets/UIToolkit-Importing/USS/allianceAreas.uss
P Assets/UIToolkit-Importing/USS/allianceMembers.uss
P Assets/UIToolkit-Importing/USS/auctionHouse.uss
P Assets/UIToolkit-Importing/USS/auctionHouseModifyMultiplePopup.uss
P Assets/UIToolkit-Importing/USS/auctionHouseSell.uss
P Assets/UIToolkit-Importing/USS/auctionHouseSellPopup.uss
P Assets/UIToolkit-Importing/USS/bakTab.uss
P Assets/UIToolkit-Importing/USS/bannerMap.uss
P Assets/UIToolkit-Importing/USS/basicItemCard.uss
P Assets/UIToolkit-Importing/USS/bestiaryTab.uss
P Assets/UIToolkit-Importing/USS/betaMenu.uss
P Assets/UIToolkit-Importing/USS/breachTracking.uss
P Assets/UIToolkit-Importing/USS/cartographyUi.uss
P Assets/UIToolkit-Importing/USS/challengeDisplay.uss
P Assets/UIToolkit-Importing/USS/channelColorizedItem.uss
P Assets/UIToolkit-Importing/USS/characterBuildsUi.uss
P Assets/UIToolkit-Importing/USS/checkboxPopup.uss
P Assets/UIToolkit-Importing/USS/cinematic.uss
P Assets/UIToolkit-Importing/USS/configShortcut.uss
P Assets/UIToolkit-Importing/USS/craftCoop.uss
P Assets/UIToolkit-Importing/USS/crafterForm.uss
P Assets/UIToolkit-Importing/USS/crafterList.uss
P Assets/UIToolkit-Importing/USS/delayedclosurepopup.uss
P Assets/UIToolkit-Importing/USS/encyclopediaBase.uss
P Assets/UIToolkit-Importing/USS/encyclopediaItem.uss
P Assets/UIToolkit-Importing/USS/endSeasonRewardPopup.uss
P Assets/UIToolkit-Importing/USS/endingAchievementsItem.uss
P Assets/UIToolkit-Importing/USS/exchangePlayer.uss
P Assets/UIToolkit-Importing/USS/exchangeUi.uss
P Assets/UIToolkit-Importing/USS/fightResultSimple.uss
P Assets/UIToolkit-Importing/USS/forgettableSpellGetScrollWarningPopUp.uss
P Assets/UIToolkit-Importing/USS/forgettableSpellSetDeletionPopUp.uss
P Assets/UIToolkit-Importing/USS/forgettableSpellSetPopUp.uss
P Assets/UIToolkit-Importing/USS/forgettableSpellSetsUi.uss
P Assets/UIToolkit-Importing/USS/forgettableSpellsIntroPopUp.uss
P Assets/UIToolkit-Importing/USS/gadugi.uss
P Assets/UIToolkit-Importing/USS/giftMenu.uss
P Assets/UIToolkit-Importing/USS/hardcoreDeath.uss
P Assets/UIToolkit-Importing/USS/hint.uss
P Assets/UIToolkit-Importing/USS/illustratedPopup.uss
P Assets/UIToolkit-Importing/USS/illustratedWithLinkPopup.uss
P Assets/UIToolkit-Importing/USS/inputComboBoxPopup.uss
P Assets/UIToolkit-Importing/USS/inputPopup.uss
P Assets/UIToolkit-Importing/USS/itemEffectsModifierUi.uss
P Assets/UIToolkit-Importing/USS/itemsSet.uss
P Assets/UIToolkit-Importing/USS/joinParty.uss
P Assets/UIToolkit-Importing/USS/kisInfractionPopup.uss
P Assets/UIToolkit-Importing/USS/kisPopUp.uss
P Assets/UIToolkit-Importing/USS/kisPreventAndSanctionPopup.uss
P Assets/UIToolkit-Importing/USS/largePopup.uss
P Assets/UIToolkit-Importing/USS/legendaryHunts.uss
P Assets/UIToolkit-Importing/USS/lockedPopup.uss
P Assets/UIToolkit-Importing/USS/login.uss
P Assets/UIToolkit-Importing/USS/mimicry.uss
P Assets/UIToolkit-Importing/USS/mysteryBoxPopup.uss
P Assets/UIToolkit-Importing/USS/normal.uss
P Assets/UIToolkit-Importing/USS/normal2.uss
P Assets/UIToolkit-Importing/USS/npcDialog.uss
P Assets/UIToolkit-Importing/USS/parchment.uss
P Assets/UIToolkit-Importing/USS/payZone.uss
P Assets/UIToolkit-Importing/USS/pollPopup.uss
P Assets/UIToolkit-Importing/USS/probability.uss
P Assets/UIToolkit-Importing/USS/pseudoChoice.uss
P Assets/UIToolkit-Importing/USS/quests.uss
P Assets/UIToolkit-Importing/USS/recycle.uss
P Assets/UIToolkit-Importing/USS/secureMode.uss
P Assets/UIToolkit-Importing/USS/shopPopupAttitude.uss
P Assets/UIToolkit-Importing/USS/shopPopupCompanion.uss
P Assets/UIToolkit-Importing/USS/shopPopupCompanionPack.uss
P Assets/UIToolkit-Importing/USS/shopPopupHarness.uss
P Assets/UIToolkit-Importing/USS/shopPopupHavenBag.uss
P Assets/UIToolkit-Importing/USS/shopPopupItemSet.uss
P Assets/UIToolkit-Importing/USS/shopPopupLivingObject.uss
P Assets/UIToolkit-Importing/USS/shopPopupMysteryBox.uss
P Assets/UIToolkit-Importing/USS/shopPopupOverlayWarning.uss
P Assets/UIToolkit-Importing/USS/shopPopupPack.uss
P Assets/UIToolkit-Importing/USS/small.uss
P Assets/UIToolkit-Importing/USS/small2.uss
P Assets/UIToolkit-Importing/USS/special.uss
P Assets/UIToolkit-Importing/USS/spectatorUi.uss
P Assets/UIToolkit-Importing/USS/spellBase.uss
P Assets/UIToolkit-Importing/USS/spellButtonsEntry.uss
P Assets/UIToolkit-Importing/USS/spellList.uss
P Assets/UIToolkit-Importing/USS/storage.uss
P Assets/UIToolkit-Importing/USS/textButtonPopup.uss
P Assets/UIToolkit-Importing/USS/themeInstaller.uss
P Assets/UIToolkit-Importing/USS/themeItem.uss
P Assets/UIToolkit-Importing/USS/title.uss
P Assets/UIToolkit-Importing/USS/titleSmall.uss
P Assets/UIToolkit-Importing/USS/tooltip_default.uss
P Assets/UIToolkit-Importing/USS/tooltip_item-smallScreen.uss
P Assets/UIToolkit-Importing/USS/tooltip_item.uss
P Assets/UIToolkit-Importing/USS/tooltip_monster.uss
P Assets/UIToolkit-Importing/USS/tooltip_monster_alternate.uss
P Assets/UIToolkit-Importing/USS/tooltip_npc.uss
P Assets/UIToolkit-Importing/USS/tooltip_spell-smallScreen.uss
P Assets/UIToolkit-Importing/USS/tooltip_spell.uss
P Assets/UIToolkit-Importing/USS/tooltip_title.uss
P Assets/UIToolkit-Importing/USS/topTaxCollectors.uss
P Assets/UIToolkit-Importing/USS/treasureHunt.uss
P Assets/UIToolkit-Importing/USS/uiTuto.uss
P Assets/UIToolkit-Importing/USS/unityOverrides.uss
P Assets/UIToolkit-Importing/USS/webBase.uss
P Assets/UIToolkit-Importing/USS/webCodesAndGifts.uss
P Assets/UIToolkit-Importing/USS/webShop.uss
P Assets/UIToolkit-Importing/USS/webVetRwds.uss
```

## Noms de catalogue non résolus — 51 entrées

Ces noms n’ont pas de chemin complet correspondant parmi les 960 chemins ci-dessus. Ils ne sont pas présentés comme des fichiers exportés ou comme des écrans inspectés. Les orthographes et variantes observées sont conservées, notamment `bestirayTabItem` et `CosmeticIOrnamentLine`.

```text
C AppearanceSlot.uxml
C Core.UI.Components.Block.uss
C Core.UI.Components.Button.uss
C Core.UI.Components.CollectionView.uss
C Core.UI.Components.Combobox.uss
C Core.UI.Components.ContextMenu.uss
C Core.UI.Components.Cursor.uss
C Core.UI.Components.DefaultButton.uss
C Core.UI.Components.ElidableMultilineLabel.uss
C Core.UI.Components.Fanion.uss
C Core.UI.Components.GridView.uss
C Core.UI.Components.InputField.uss
C Core.UI.Components.ProgressBar.uss
C Core.UI.Components.ScrollContainer.uss
C Core.UI.Components.Tabs.uss
C Core.UI.Components.Toggle.uss
C Core.UI.Components.ToggleButton.uss
C Core.UI.Components.Tooltip.uss
C Core.UI.Components.Tutorial.uss
C Core.UI.Components.Window.uss
C CosmeticIOrnamentLine.uxml
C InlineGridItem_gd_endingAchievements_ctr_card.uss
C InlineGridItem_gd_list_default.uss
C InlineGridItem_gd_mapElements_default.uxml
C SpellStateTooltip.uxml
C achievement.uss
C articleSlot.uss
C bestirayTabItem.uxml
C blocked.uxml
C buildManager.uxml
C card.uss
C cosmeticStorageUi.uxml
C ctr_cat.uss
C default.uxml
C details.uss
C fighter.uss
C filterItem.uss
C forgettableSpellsUi.uxml
C friend.uss
C friend.uxml
C houseSale.uss
C item.uss
C itemDetails.uss
C itemLine.uss
C login_mobile.uxml
C mountInfoTooltip.uxml
C mountTiles.uxml
C objective.uss
C rewards.uss
C searchBar.uss
C socialGroupLine.uxml
```

## Vérification et inconnues restantes

Les 960 chemins sont uniques et l’addition des comptes par famille et extension a été contrôlée en mémoire. Les 100 objets marqués I ont une référence locale valide dans leur bundle et un extrait tiré uniquement de leurs bornes sérialisées. Les 51 noms C ont été comparés aux noms de fichiers de tous les chemins complets récupérés.

Les 860 autres chemins ne sont pas présentés comme des layouts reconstruits. Les propriétés numériques, bindings, contrôleurs, sprites associés, dimensions DPI et comportements en jeu restent inconnus ici. Aucun test applicatif ni capture d’exécution n’a été lancé : la livraison est un index de ressources et une proposition de traduction visuelle, pas une modification de l’application.
