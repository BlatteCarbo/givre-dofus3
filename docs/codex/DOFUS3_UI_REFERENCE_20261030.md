# Référence UI Dofus 3 : atelier de forgemagie

Recherche locale du 30 octobre 2026. Lecture seule des fichiers du client ; aucun lancement du jeu, achat, téléchargement de dépendance ou changement de configuration. Seule cette note est créée.

## Conclusion utilisable

Le client installé contient bien les layouts Unity UI Toolkit de la forgemagie, et pas seulement des images ressemblantes : `smithMagic.uxml`, `smithMagicItemEffectLine.uxml` et `smithMagicItemHistory.uxml`. Les composants sérialisés confirment le panneau historique, la table de caractéristiques, les deux commandes de fusion et trois emplacements de runes par ligne.

Pour reproduire l'écran fourni : privilégier une table dense dans un panneau ardoise violet, un historique étroit à gauche, une action primaire vert lime dans l'en-tête et un inventaire indépendant à droite. Les recommandations de dimensions et de couleurs ci-dessous viennent de la capture ; elles ne constituent pas des valeurs USS originales récupérées.

## Sources et méthode

Identifiants locaux de preuve, propres à cette note :

| Preuve | Source | Ce qu'elle établit |
| --- | --- | --- |
| E1 | `%USERPROFILE%/Pictures/Screenshots/Capture d'écran 2026-10-30 225812.png` | Capture réellement affichée et examinée : 1067 × 618 px, fenêtre « Costumager », inventaire à droite. |
| E2 | `%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3/Dofus_Data/StreamingAssets/version` | `Version=3.7.12.12`, `BuildDate=2026-10-19T15:11:45+00:00`. C'est la version installée observée, pas une affirmation sur la dernière version publiée. |
| E3 | `Dofus_Data/StreamingAssets/aa/catalog.bin` | Noms adressables, noms UXML/USS, thèmes et noms de textures présents dans le catalogue binaire local. |
| E4 | `Dofus_Data/StreamingAssets/aa/StandaloneWindows64/uielements_assets_all.bundle` | Chemins internes des layouts et chaînes de composants effectivement présentes dans le bundle décompressé. |
| E5 | `Dofus_Data/StreamingAssets/aa/StandaloneWindows64/tss_assets_all.bundle` | Noms de thèmes TSS et classes/variables sémantiques de couleurs. |
| E6 | `Dofus_Data/StreamingAssets/aa/StandaloneWindows64/uidarkstone_assets_all.bundle` | Chemins internes de textures de panneaux, boutons, inventaire et slot de rune. |
| E7 | `Dofus_Data/StreamingAssets/aa/StandaloneWindows64/uiassets_assets_.bundle` | Ressources du thème Figma, distinctes du thème historique darkStone. |

Dans E3 à E7, les chemins commençant par `Dofus_Data/` sont relatifs à `%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3/`.

Le guide `%USERPROFILE%/.claude/skills/reverse-engineer-anything/SKILL.md` et sa section de récupération de connexion ont été lus. Les outils MCP REA n'étaient pas exposés dans cette session. Le CLI REA 6.8.0 déjà présent dans le cache npm a été utilisé directement, sans `npx` et sans installation :

```powershell
node %USERPROFILE%/AppData/Local/npm-cache/_npx/da7caeae9db9c589/node_modules/rea-agents/scripts/rea.mjs inspect-il2cpp-metadata %USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3/Dofus_Data/il2cpp_data/Metadata/global-metadata.dat --format json --filter-output evidence_id,subject,provider,confidence,normalized_result.metadata_version,normalized_result.counts,limitations
```

Résultat REA : `confidence=observed`, `metadata_version=39`, fournisseur `REA IL2CPP global-metadata reader`, version `1`.

- Evidence ID : `ev_1852466356c1a8076b91ae2577fee6949a6ecc88cfa144a227f0feb5e2fe6044`.
- SHA-256 de `global-metadata.dat` : `ba1efbad5a8ecca48775fc20293f17e9dece72cc6134c4e7ecdd8071e7c67db6`.
- Limite : pas de `binary_path` fourni, donc pas d'adresses natives, pas de types de champs/paramètres/retours récupérés, pas de comportement exécuté observé. Ce constat ne fournit pas les couleurs ni les dimensions UI.
- Le filtre affiche seulement une projection de la preuve ; aucun bundle de preuve REA complet n'a été écrit.

L'inventaire des fichiers et l'analyse des bundles sont des observations locales ordinaires, **pas** des Evidence REA. Les bundles UnityFS version 8 ont été décompressés uniquement en mémoire avec un décodeur LZ4 local ; chaque bloc décompressé a été vérifié contre sa taille annoncée. Une recherche de chaînes imprimables a ensuite relevé les chemins et composants. Cette méthode ne reconstitue pas un arbre UXML complet et ne résout pas les références croisées des objets Unity.

## Layouts réellement retrouvés

Dans E4 :

```text
Assets/UI/Crafting/smithMagic.uxml
Assets/UI/Crafting/smithMagicItemEffectLine.uxml
Assets/UI/Crafting/smithMagicItemHistory.uxml
Assets/UI/Crafting/runeMaker.uxml
Assets/UI/Crafting/runeMaker/runeMaker_Slot.uss
```

Les fichiers `.uxml` ci-dessus sont des **chemins internes de ressources sérialisées**, pas des fichiers texte autonomes disponibles sur disque.

### Écran principal

Chaînes retrouvées dans le groupe sérialisé `smithMagic` :

- Deux `WindowFigma` : `window`, titre de conception `[Forgemagie]`, et `storageWindow`, avec un template `storage`.
- Organisation : `ctr_header`, `ctr_crafter`, `ctr_merge`, `ctr_client`, `ctr_sidePanel`, `ctr_mainPanel`, `ctr_history`, `ctr_stats`, `ctr_tableHeaders`.
- Équipement : `slot_equipment`, `slot_rune`, `slot_signatureRune`.
- Action secondaire : `btn_mergeAll`, texte de conception `[fusionner tout]`.
- Action principale : `btn_merge`, texte de conception `[fusionner]`.
- Reliquat : identifiant original `lbl_relicats`, texte de conception `[Relicats: 1234]`.
- Historique : `btn_clearHistory`, texte de conception `[vider l'historique]`.
- En-têtes de table : `tch_min`, `tch_max`, textes `[Min.]`, `[Max.]`, `[Effet / Carac.]`, `[Valeur]`.
- Classes retrouvées : `backgroundColor_background_medium90`, `backgroundColor_background_dark100`, `textColor_white_white100`, `textColor_white_white80`, `textColor_white_white65`.

Les textes entre crochets sont des valeurs de conception présentes dans les assets. Ils ne prouvent ni la traduction finale ni les données du personnage. Le titre « Costumager » est, lui, visible dans E1.

### Ligne de caractéristique

Le groupe sérialisé `smithMagicItemEffectLine` contient :

```text
lbl_effectMin     lbl_effectMax
ctr_effect       lbl_effectName
ctr_delta        ctr_effectModif       lbl_effectModif
ctr_runeSlots    slotRune              slotRunePa       slotRuneRa
fromBagGa        fromBagPa             fromBagRa
background      backgroundBonus       backgroundMalus
```

Les slots portent `slot-size=small`. Les labels portent notamment `textShort_largeRegular` et `textColor_white_white80`. La présence des trois slots est une preuve structurelle forte pour conserver les colonnes rune simple / Pa / Ra plutôt qu'une liste d'actions séparée.

### Historique

`smithMagicItemHistory` contient `ctr_header`, `ctr_itemHistoryLines`, `smithMagicItemHistoryLine`, `tx_itemImage`, `lbl_itemName`, `tx_rune`, `lineDelta`, `customLine`, `fmLabel`. Son bouton `btn_clearItemHistory` a `main-style=tertiary` et le nom d'icône `trash`. La classe `textColor_highlight_secondary100` apparaît pour le nom d'objet.

## Styles et ressources retrouvés

E5 contient les familles `SmallFont`, `MediumFont`, `LargeFont` pour les thèmes `Standard`, `Belladone`, `Bonta`, `Brakmar`, `EmeraldMine`, `GoldAndSteel`, `Pandala`, `Sufokia`, `Tribute`, `Unicorn`, `Wabbit`. Exemples :

```text
Assets/UI/TSS/SmallTss/SmallFont_Standard.tss
Assets/UI/TSS/MediumTss/MediumFont_Standard.tss
Assets/UI/TSS/LargeTss/LargeFont_Standard.tss
```

Variables observées : `--background_medium100`, `--background_medium90`, `--highlight_primary100`, `--highlight_secondary100`, `--spellElements_wisdom`, `--spellElements_wisdomLight`. Classes associées observées : `textColor_*`, `backgroundColor_*`, `tintColor_*`.

**Valeurs numériques des couleurs non décodées.** Le thème actif de la capture n'est pas identifié. La présence du bundle darkStone ne prouve pas que la capture utilise darkStone ; E7 contient également des chemins `Assets/UI/Themes/Figma/...`.

Chemins internes concrets retrouvés dans E6 :

```text
Assets/UI/Themes/darkStone/common/bg_small_border_dark.png
Assets/UI/Themes/darkStone/common/bg_small_border_dark_square.png
Assets/UI/Themes/darkStone/common/bg_small_border_light.png
Assets/UI/Themes/darkStone/common/btn_close_normal.png
Assets/UI/Themes/darkStone/common/btn_greenLargeBorder_normal.png
Assets/UI/Themes/darkStone/common/btn_greenLargeBorder_disabled.png
Assets/UI/Themes/darkStone/common/btn_greenLargeBorder_over.png
Assets/UI/Themes/darkStone/common/btn_greenLargeBorder_pressed.png
Assets/UI/Themes/darkStone/texture/inventory_all.png
Assets/UI/Themes/darkStone/texture/inventory_equipment.png
Assets/UI/Themes/darkStone/texture/slot/icon_slot_rune_forgemagie.png
Assets/UI/Themes/darkStone/texture/slot/inventoryEmptySlot.png
```

E3 contient aussi les noms `tx_vitality.png`, `tx_intelligence.png`, `tx_agility.png`, `tx_wisdom.png`, `tx_prospecting.png`, `tx_initiative.png`, `icon_slot_rune_forgemagie.png`, `inventory_consumable.png`, `inventory_resources.png`, `inventory_quest.png`. Le conteneur exact et le rendu des six premières icônes ne sont pas résolus ici : utiliser ces noms comme pistes, pas comme fichiers prêts à importer.

Attention au faux ami : `aa/StandaloneWindows64/icons_assets_.bundle`, inspecté en mémoire, contient des chemins `Assets/Content/AppIcons/0.png`, `1.png`, etc. Ce n'est pas une preuve que ce bundle contient les icônes de statistiques. `Content/Picto/UI/icon_assets_all.bundle` existe également, sans correspondance de stat établie dans cette recherche.

Aucun PNG, SVG, atlas, fichier de police ou UXML autonome n'a été exporté. Les éléments « retrouvés » sont des identifiants, des chemins internes et des chaînes de propriétés, pas une livraison d'images réutilisables.

## Recommandations issues de la capture E1

### Composition

Mesures visuelles approximatives dans l'image 1067 × 618, et non dimensions natives du jeu :

- Fenêtre atelier : x ≈ 2 à 699, y ≈ 11 à 604 ; inventaire : x ≈ 720 à 1051. Espace entre les fenêtres ≈ 21 px.
- En-tête atelier jusqu'à y ≈ 114. Historique x ≈ 3 à 197 ; table x ≈ 207 à 684. Ratio historique/table ≈ 28/72 de la largeur utile.
- Statistiques : lignes de ≈ 29 px, séparées de ≈ 5 px ; colonnes stables min/max, effet avec petite icône, modification, rune/Pa/Ra. Valeurs alignées, libellés non présentés en cartes.
- Inventaire : recherche et filtres en haut, barre de catégories verticale à gauche de la grille, grille de cinq colonnes environ, cases carrées ≈ 51 px, pied avec jauge et monnaie.
- Conserver les deux actions « Fusionner tout » et « Fusionner » côte à côte ; seul « Fusionner » reçoit le fond lime. La sélection de ligne reçoit un violet plus clair, pas le même vert que l'action.

Pour une adaptation web, employer une grille à zones explicites et des colonnes numériques de largeur fixe ; garder le tableau défilant sous son en-tête. Sur une surface trop étroite, déplacer l'inventaire dans un onglet ou un panneau distinct plutôt que comprimer les colonnes de runes. Ces comportements adaptatifs sont des choix de reconstruction, non observés dans le client.

### Palette mesurée puis normalisée

Pixels échantillonnés avec `System.Drawing.Bitmap.GetPixel`, origine en haut à gauche de E1. L'image comporte texture, transparence et texte anticrénelé : un pixel ne définit pas une couleur de thème originale.

| Usage | Pixel observé | Valeur observée | Jeton de reconstruction conseillé |
| --- | --- | --- | --- |
| Bandeau ardoise violet | (20, 20) | `#3F3F58` | `--smith-header: #3F3F58` |
| Panneau secondaire | (100, 80) | `#33354C` | `--smith-panel: #33354C` |
| Fond de ligne | (500, 195) | `#2A2D4D` | `--smith-row: #2A2D4D` |
| Ligne sélectionnée | (500, 160) | `#44486E` | `--smith-row-selected: #44486E` |
| En-tête de table | (500, 127) | `#19192D` | `--smith-table-header: #19192D` |
| Fond des cases d'inventaire | (900, 300) | `#100F19` | `--smith-slot: #100F19` |
| Fond lime du bouton, hors texte | (430, 88) | `#89A200` | `--smith-primary: #89A200` |

Compléments suggérés, **estimés visuellement et non pipetés comme tokens originaux** : texte principal blanc cassé `#ECECF2`, texte secondaire `#A7A8BB`, gain/statistique favorable `#A8CC70`, perte `#D55261`, séparateur `#44445D`. Le vert des caractéristiques doit rester plus doux que le bouton primaire.

### Icônes

La capture montre des petits pictogrammes colorés immédiatement avant les effets : cœur rouge pour vitalité, marque orange pour intelligence/feu, marque verte pour agilité/air, croissant violet pour sagesse, œil turquoise pour portée, symbole bleu pour prospection. Ce sont des observations visuelles, pas une table officielle d'identifiants d'effets.

Pour la reconstruction : réserver ≈ 12 à 14 px au pictogramme d'effet et ≈ 20 à 24 px aux runes dans les lignes ; afficher les quantités au même emplacement sur chaque slot ; conserver les glyphes et chiffres lisibles sans dépendre de la couleur. Ne pas remplacer ces pictogrammes par des emoji dont le rendu varie selon le système.

Les chemins internes de E6 peuvent guider une future extraction ciblée. Tant que les images ne sont pas exportées et vérifiées, créer des icônes locales cohérentes ou employer les ressources déjà présentes dans l'application hôte, sans prétendre qu'il s'agit des sprites originaux de Dofus.

## Empreintes et limites de vérification

SHA-256 calculés sur les bundles compressés installés :

| Fichier dans `aa/StandaloneWindows64/` | Taille fichier | SHA-256 |
| --- | ---: | --- |
| `uielements_assets_all.bundle` | 16 664 583 octets | `19f4352865203d3b8ba77dcde731ab01a71d1a1ec5a171f47b5518b3f1bbd4ac` |
| `tss_assets_all.bundle` | 2 130 045 octets | `5832c4dc7f506046a93f7b760c4aca72ad9f0a2681c19546beefeba4226a0c57` |
| `uidarkstone_assets_all.bundle` | 2 329 291 octets | `3d27a26b45f44a5cf52aacbba832e6b835ffbfa010934ae6ff282427c3e581b1` |
| `uiassets_assets_.bundle` | 15 093 324 octets | `ac75954fd3782a1a5d28e27df5e76e3c198acc470e95c0c8f3d9f2771e2d580d` |

Vérifié : existence des conteneurs, version locale, décompression des blocs, chaînes de layouts et classes, inspection REA des métadonnées IL2CPP, affichage de la capture, coordonnées des couleurs mesurées. Non vérifié : arbre complet et styles calculés à l'exécution, thème actif, interactions, dimensions indépendantes du DPI, extraction des textures, correspondance des IDs d'objets avec les images et états survol/désactivé. Aucun test applicatif ne s'applique à cette note documentaire.
