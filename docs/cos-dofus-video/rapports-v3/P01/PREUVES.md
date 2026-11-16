# P01 — Sources, empreintes, jointures et limites

Date des lectures et commandes : **2026-11-04**. Racine du dépôt : **%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024**. Racine du client : **%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3**. Les numéros de lignes renvoient au contenu local lu le 04/11/2026. Aucune opération Git, session, combat ou compilation.

## 1. Inventaire des entrées et empreintes vérifiées

| Entrée effective | SHA-256 | Taille / portée |
| --- | --- | --- |
| Dofus_Data/StreamingAssets/version | **302ebf852253f99af779869ae5987a05e19e45e9113f52b30468ca1115a6753d** | 3.7.13.17 ; build 2026-11-03T14:09:52+00:00, selon N01/CLIENT_SOURCES.json:3-4,57-60 |
| Content/Data/data_assets_spellstatesdataroot.asset.bundle | **50db7a3a8fcf118f8cf2ac119b5be34530dfcfadea57d78aa9f770c5987db48a** | **41 013 octets**, 6 378 SpellStateData. Lecture P01 nouvelle |
| Content/Data/data_assets_spellsdataroot.asset.bundle | **96c830cd1add89cb0af1c613886a7f193fd38706d1cd27e51556bfad9b330e26** | **320 298 octets**, 17 006 SpellData. Même empreinte que N01/CLIENT_SOURCES.json:21-31 |
| Content/Data/data_assets_spelllevelsdataroot.asset.bundle | **525048d9a4406720ba79fb6a23826b9ac5e40d4a87dcd3c452e1a0ccc27b2a30** | **1 097 950 octets**, 34 318 SpellLevelData. Même empreinte que N01/CLIENT_SOURCES.json:33-43 |
| N01/SORTS_3.7.13.17.json | **f590d7fb2fa7a432e09eba2441cafbfb6b6d9ffebc7e95e45b4dad929aa2a3d2** | Sous-ensemble de 4 classes et 176 sorts ; lecture seule |
| N01/EFFECTS_3.7.13.17.csv | **cbd685e8852966961a7d155c17d7a21dd23ec8e45cac70aa31be06300f89384b** | Sous-ensemble N01, 2 040 effets normaux + critiques |
| N08/PREUVES.md | **f3c222dd1fc3da00d1e5fac282ccf5a52f719fc2629ccd80593bc0bb85901d9b** | Point de départ de l'audit statique |

Pour les chemins des trois bundles, le préfixe est Dofus_Data/StreamingAssets/Content/Data sous l'installation locale. Le SHA du fichier version est repris du manifeste N01 déjà exécuté ; les **six autres empreintes** de la table ont été obtenues dans P01 par la commande ci-dessous, code retour **0** :

    $files = @('docs/cos-dofus-video/rapports-v2/N01/SORTS_3.7.13.17.json','docs/cos-dofus-video/rapports-v2/N01/EFFECTS_3.7.13.17.csv','docs/cos-dofus-video/rapports-v2/N08/PREUVES.md','%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3/Dofus_Data/StreamingAssets/Content/Data/data_assets_spellstatesdataroot.asset.bundle','%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3/Dofus_Data/StreamingAssets/Content/Data/data_assets_spellsdataroot.asset.bundle','%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3/Dofus_Data/StreamingAssets/Content/Data/data_assets_spelllevelsdataroot.asset.bundle'); $files | ForEach-Object { $h = Get-FileHash -LiteralPath $_ -Algorithm SHA256; Write-Output ($h.Hash + ' ' + $_) }

Retour : les six empreintes ci-dessus, sans erreur. Identité native plus large déjà épinglée dans N01/PREUVES.md:14 : GameAssembly.dll SHA-256 **ad013415455d93bd865aef70fcf1b7c6f704c799f85e2ef301aa7fa610ef2591**, global-metadata.dat **ad2880162d7e0aee219fdc81e4a3817cc86cca4aba3df3ce86fd470c1bb9a6af**. Ces octets identifient la version, sans observation serveur.

## 2. Méthode offline réellement exécutée

- **Lecture de fichiers** via CoS Core : prompt V3 lignes 1-99, N08/PASSAGE_DEVELOPPEUR.md:1-9, N08/RAPPORT.md:1-23, N08/PREUVES.md:1-20, N01/RAPPORT.md:1-61, N01/PREUVES.md:1-61, N01/CLIENT_SOURCES.json:1-121, N07/RAPPORT.md:1-57, N01/extract_spells.py:1-257 et les sections de SpellResolver ci-dessous. Toutes ces lectures sont **lecture seule**.
- **Recherche de texte** exécutée dans la racine du dépôt : recherche ciblée de « insoignable », « unhealable », « heal.block », etc. dans docs / GivreClone.*. Sortie positive dans N08, M15, M27, docs/codex/HUZOUNET_NOTES.md:430 et la base historique N07. Une recherche plus resserrée de incurable / stateInsoignable dans les sources C# a retourné **0 occurrence** (code de sortie **1** de rg, absence de match dans ce périmètre). Ce résultat ne prouve aucune absence dans le client natif.
- **Découverte du bundle** : Get-ChildItem du répertoire Content/Data avec filtre *states* ; retour : **data_assets_spellstatesdataroot.asset.bundle, 41013 octets**, code retour **0**.
- **Décodage des bundles** : scripts Python **inline** exécutés sous PowerShell par la forme exacte « $script = @' [corps Python] '@ ; python -c $script », avec importlib.util pour réutiliser les fonctions **read_bundle** et **i18n_names** de N01/extract_spells.py, sans exécuter son main (qui aurait réécrit N01). L'interpréteur utilisé est le Python local et UnityPy **1.25.4** déjà installé. UnityPy a émis le warning documenté **UnityVersionFallbackWarning** sur les bundles, puis les analyses finales ont rendu **exit code 0**.
- **Contrôle de version** : le même lecteur travaille sur les sources exactes du manifeste N01. Le fallback UnityPy **6000.3.16f1** vient de la méthode N01/extract_spells.py:21-35 et ne constitue pas une preuve de version Unity effective.
- **Échec d'exploration sans mutation** : une recherche exploratoire trop large qui traitait à tort les valeurs de dés numériques 76 comme des IDs d'états a renvoyé **506 faux candidats hétérogènes** (actions 97,99, etc.). Elle a été **abandonnée**, puis remplacée par le filtre correct « actionId ∈ {950,951,952} ET value ∈ {76,5920} » : **253 lignes pertinentes**, distinctes des dés 76. Une première jointure des niveaux a échoué sur **KeyError 30742** (niveau sans SpellData) ; le script suivant marque explicitement cet orphelin. Une tentative de mise en forme a échoué sur SyntaxError de la commande inline, une autre sur UnicodeEncodeError de la console CP1252 ; la version finale force l'UTF-8, affiche les 134 noms et termine **code 0**. Aucune de ces étapes n'a écrit dans les bundles.

**Reproduction équivalente, à exécuter si besoin**, dans PowerShell depuis la racine du dépôt ; ce bloc condense la méthode déjà exécutée et n'écrit aucun fichier. Vérifier séparément avec Get-FileHash que les SHA des bundles concordent avec la section 1 avant d'attribuer les résultats à la version cible :

    $script = @'
    import importlib.util, collections, sys
    from pathlib import Path
    sys.stdout.reconfigure(encoding='utf-8')
    p=Path('docs/cos-dofus-video/rapports-v2/N01/extract_spells.py').resolve()
    spec=importlib.util.spec_from_file_location('reader',p)
    m=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    sys.path.insert(0,str(m.READER))
    directory=m.CLIENT/m.DATA_REL
    states,sm=m.read_bundle(directory,'spellstates')
    spells,sp=m.read_bundle(directory,'spells')
    levels,lm=m.read_bundle(directory,'spelllevels')
    matches=[]
    for level in levels:
        for branch,key in [('normal','effects'),('critical','criticalEffect')]:
            for index,e in enumerate(level.get(key) or []):
                if e.get('actionId') in (950,951,952) and e.get('value') in (76,5920):
                    matches.append((level['spellId'],level['grade'],level['id'],branch,index,e['effectUid'],e['actionId'],e['value']))
    print(len(states),len(spells),len(levels),len(matches))
    print(collections.Counter((r[6],r[7]) for r in matches))
    for sid in (76,5920):print(next(s for s in states if s['id']==sid))
    '@
    python -c $script

Sortie attendue **d'après les commandes P01 exécutées** : **6378 17006 34318 253** ; décompte (950,76)=231, (951,76)=20, (950,5920)=1, (951,5920)=1. Tous les effets décrits dans RESULTAT.md se vérifient ensuite par jointure **SpellData.id = SpellLevelData.spellId**, **SpellLevelData.id/grade**, **effects[] ou criticalEffect[]**, **effectUid**, **actionId**, **value**. Un script qui jointerait aveuglément tous les niveaux doit gérer le spellId 30742 non trouvé.

## 3. Extraits vérifiables des enregistrements sérialisés

Les bundles binaires n'ont pas de numéro de ligne textuel ; la traçabilité est **SHA du bundle → racine native → clé id → grade/branche → effectUid**. Les éléments ci-dessous ont été affichés depuis les bundles installés, via le lecteur N01 existant.

| Identité extraite | Valeurs sérialisées constatées |
| --- | --- |
| SpellStateData **76** | nameId=65291, descriptionId=1183851 ; localisation « Insoignable » ; **incurable=1**, icon=stateInsoignable, displayTurnRemaining=1, spellLevelId=0 |
| SpellStateData **5920** | nameId=1089557, descriptionId=1089558 ; localisation « CountsKills » ; **incurable=0**, icon=stateInsoignable, displayTurnRemaining=0 |
| SpellLevelData **41440** (spell **13187**, grade 1) | Normal uid **209838** : actionId=1109, diceNum=100, duration=0, triggers=I, order=0, targetMask=a ; uid **209839** : actionId=950, value=76, duration=4, order=1, targetMask=a ; **criticalEffect=[]**, criticalHitProbability=0. Grade 2, levelId 41441 : même schéma, uid 209840/209841 |
| SpellLevelData **6331** (spell **1173**, grade 1) | Normal uid **35490**, action 950/76, duration=1 ; critique uid **35491**, 950/76, duration=2 ; targetMask=A, triggers=I |
| SpellLevelData **5886** (spell **1084**, grade 1) | Normal : uid 28068/28069, actions 179/154, **aucune action 950**. Critique uid **137566** : action 950/76, duration=2, order=4 |
| SpellLevelData **22981** (spell **5097**, grade 1) | Normal uid **126453** action 950/76, durée 1, mask=a,A ; critique vide |
| SpellLevelData **22541** (spell **4736**, grade 1) | Normal uid **124553** action 950/76, durée 1, mask=A,O ; uid 124554 action 77 distincte |
| SpellLevelData **79170** (spell **29779**, grade 1) | Normal uid **385029** action 951/76, durée 0, mask=C ; critique vide |
| SpellLevelData **54775** (spell **20860**, grade 1) | Normal uid **261356** action 950/76, duration=-1, order=0 ; uid **261357** action 951/76, duration=-1, mask=a,A,E76, order=1 ; uid 393030 action 120, order=2 |
| SpellLevelData **44378** (spell **14587**, grade 1) | Normal uid **226187**, action 1109, diceNum=6 ; critique uid **226190**, action 1109, diceNum=8 ; mask=a |
| SpellLevelData **44369** (spell **14584**, grade 1) | Normal uid **410153**, action 2998, trigger=I ; uid **410155**, action 2998, trigger=TB ; critique uid **410156/410158** respectivement I/TB. Action 91 séparée, uid 313044/313045 |
| SpellLevelData **40910** (spell **12911**, grade 1) | Normal uid **208179**, action 93, dés 16–18 ; critique uid **208180**, action 93, dés 19–21 |
| SpellLevelData **41883** (spell **13431**, grade 1) | Normal uid **259838**, action 1020, diceNum=150, duration=1, mask=a,P,F3112,F3113,F3114,F5161,F5163,F5162 |
| SpellLevelData **44911** (spell **14825**, grade 1) | **statesCriterion=HS!76** ; aucune action 950/76, effets 752/128/1033 |
| SpellLevelData **80622** (spell **30461**, grade 1) | Normal uid **394977**, action 950/value 5920 ; uid **394980**, action 951/value 5920. **Aucune** preuve de blocage des soins par 5920 |
| SpellLevelData **20720** (spell **3545**, grade 1) | Normal uid **428026**, action 950/76, duration=2, **delay=8**, order=117 |

Le texte localisé est issu de Content/I18n/fr.bin (hash **40fb0c50526fdf084b2b8e16fd32f874a7ed633edc430f821f050b70dc7bb0ad** selon N01/CLIENT_SOURCES.json:62-66). Certains accents sont mal affichés dans des consoles CP1252 ; les **IDs numériques et drapeaux** demeurent la preuve d'identité. Ces extractions ne suffisent pas à interpréter complètement les masques **a/A/C, P, E76**, le critère **HS!76**, les triggers **TB**, la durée négative, les conditions de disponibilité ni la résolution moteur.

## 4. Lecture du code local, niveau de preuve distinct

| Fichier:ligne | Fait strictement démontré par lecture de code | Limite |
| --- | --- | --- |
| GivreClone.App/Services/Combat/SpellResolver.cs:**10-16** | SpellFighter porte Life, MaxLife, Shield, States, StatesKnown et AbsentStates | Pas de propriété native incurable ni d'oracle serveur |
| SpellResolver.cs:**62-66**, **72-90** | Le resolveur est une simulation immédiate ; trigger différent de I ou delay > 0 est Deferred ; la durée nécessite un simulateur de tour | TB et expiration serveur non définis |
| SpellResolver.cs:**143-150** | Heal calcule un gain plafonné par MaxLife−Life et émet HealingResolved ; **n'inspecte pas States/76** | Lacune de ce chemin démontrée par N08, pas preuve d'un bug en jeu |
| SpellResolver.cs:**344-368** | Les actions de dégâts dont 91–95 provoquent Heal sur le **lanceur** après Damage ; damage et healing sont deux événements distincts | Absorption par bouclier/critique/état nécessitent oracle |
| SpellResolver.cs:**370-388** | Actions 1109/2998-3002/2973/786 et autres via Heal ; 1109 utilise MaxLife cible ; action 90 transfert | Le code ne bloque pas 76 |
| SpellResolver.cs:**389-394** | 1020/1039/1040 créent un ShieldResolved et modifient Shield | Aucune règle native « Insoignable interdit bouclier » prouvée |
| SpellResolver.cs:**403-412** | 950 ajoute l'état, 951/952 le retirent dans le modèle local | Aucune preuve du moment serveur ou de dispell |
| SpellResolver.cs:**210-278** | Le résolveur évalue des masques conditionnels et marque Unknown sur absence de certaines observations | La logique locale n'est pas la spécification officielle des masques |
| GivreClone.App/Services/Combat/CombatTurnPlanner.cs:**451-460**, **499-515** | Le planificateur valorise soin/shield selon PV, exposition, réservations | Pas de lecture de l'état 76 dans cette valorisation |

## 5. Sources externes, corroboration seulement

- [Huzounet — Les états sur Dofus](https://huzounet.fr/guides/les-etats-sur-dofus), consulté le **2026-11-04**, rubrique « Insoignable » : formule l'interdiction de récupérer des PV via soin et vol de vie. Version de serveur exacte non prouvée par cette page.
- [Dofus pour les Noobs — États](https://www.dofuspourlesnoobs.com/eacutetats.html), consulté le **2026-11-04**, ligne « État Insoignable » : même périmètre conceptuel. Page de longue durée, non assimilable à une preuve live sur 3.7.13.17.
- [Dofus Fashionista — Mot de Reconstitution](https://dofusfashionista.gg/fr/spells_linked/Aniripsa/NDk5NjJNZohr/), consulté le **2026-11-04**, décrit le soin total suivi d'Insoignable. L'extraction locale **13187/41440** reste la source primaire des IDs et chiffres.

## 6. Précisions sur N01/N07 et registres inconnus

- N01/PREUVES.md:34-45 démontre le format et les jointures sur **quatre classes** ; la recherche exécutée dans N01/SORTS_3.7.13.17.json de lignes action 950/951/952 et value 76 a rendu **0 match**, code retour **0** du script. **Ne pas interpréter ce zéro comme absence de l'état dans le client complet.**
- N07/RAPPORT.md:3-12,52-56 démontre « 0 différence » uniquement pour les 176 sorts des quatre classes, entre un export tiers 3.7.12.12 et l'extraction installée 3.7.13.17. **Mot de Reconstitution 13187**, **Anti-soin 5097** et les sorts de monstres ne font pas partie du périmètre prouvé inchangé.
- **Unknown serveur :** identité fiable de l'acteur et de son propriétaire, branche critique réellement prise, acceptation du cast, masque et ligne de vue, présence/expiration/retirabilité de 76, effets de 76 sur soins directs, vol de vie, soins déclenchés, boucliers et MaxLife, ordre des effets dans le même cast, unités de delay=8, version de l'instance de jeu. Aucune trace autoritaire n'a été produite ni consultée pendant P01.
- **Fixture** : [fixtures_candidates.json](fixtures_candidates.json) porte des entrées de comparaison **synthétiques/non exécutées**, jamais des receipts ou résultats de tests. Les scénarios nécessitant du gameplay ne reçoivent **aucun oracle serveur inventé**.
