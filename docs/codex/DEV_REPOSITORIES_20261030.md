# Depots utiles au developpement natif GivreClone

Recherche du 30 octobre 2026, sur README, documentation et sources des auteurs.
Les branches consultees sont mouvantes : les versions ci-dessous decrivent le
code lu, pas une garantie sur le dernier paquet publie. Aucun outil installe,
aucun binaire telecharge, aucune action de jeu executee.

## Partir de ce qui existe

- `GivreClone.App/GivreClone.App.csproj` : .NET 10, Avalonia 12.1.1,
  CommunityToolkit.Mvvm 8.4.2, AvaloniaEdit 12.0.0, MoonSharp 2.0.0 ;
  NativeAOT est optionnel. Ne pas remplacer cette pile par React.
- `GivreClone.Tests/GivreClone.Tests.csproj` : xUnit 2.9.2 et tests MoonSharp.
  `GivreClone.Engine/LuaTooling/` contient deja lexer, linter, formatage,
  repli et catalogue ; `GivreClone.Tests/LuaTooling/LuaToolingPerfTests.cs`
  exerce 10 000 lignes. Un nouveau linter seul ferait doublon.
- `GivreMcp/GivreMcp.csproj` reference deja ModelContextProtocol 2.2.0 ;
  `.github/workflows/pure-tests.yml` contient tests purs et smoke MCP hors jeu.
  L'audit `QA_LUA_COVERAGE.md` distingue enregistrement et comportement reel :
  un analyseur statique ne remplace pas les tests de callbacks MoonSharp.
- `tools/ForgeUiStress/Program.cs` fournit deja une fenetre native, un profil
  `forge-ui-stress`, 10 000 evenements synthetiques, une assertion sur une seule
  ligne de session et une mesure des octets alloues. Le besoin immediat est
  d'exploiter ce banc, puis d'expliquer les allocations, pas d'ajouter un kit UI.
  L'incident et sa mesure sont decrits dans
  `docs/codex/UI_DOFUS3_REDESIGN_20261030.md`.

Ces constats viennent des fichiers locaux lus ; les usages proposes ci-dessous
sont des recommandations, pas des integrations deja validees.

## Selection : huit depots, par besoin

### 1. AvaloniaUI/Avalonia — tests natifs sans bureau

**Decision : maintenant comme reference ; integration dans un projet UI separe.**
Le [README](https://github.com/AvaloniaUI/Avalonia/blob/master/readme.md) confirme
la pile native .NET/XAML. La [documentation Headless XUnit](https://docs.avaloniaui.net/docs/testing/headless-xunit)
montre application de test, theme, `AvaloniaFact` et simulation de saisie.
Utile ici pour focus clavier, bindings, etats vide/charge et bornage de listes,
sans lancer de compte.

**Contrainte :** aligner les paquets Headless sur Avalonia 12.1.1. Le
[projet Headless.XUnit courant](https://github.com/AvaloniaUI/Avalonia/blob/master/src/Headless/Avalonia.Headless.XUnit/Avalonia.Headless.XUnit.csproj)
depend de xUnit v3, contrairement aux tests locaux v2 : ne pas les melanger
aveuglement. Headless ne prouve pas le comportement du bureau Windows.
**Licence :** [MIT](https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md).

### 2. VerifyTests/Verify.Avalonia — regression visuelle AXAML

**Decision : ensuite, apres stabilisation de la refonte et du projet Headless.**
Le [README](https://github.com/VerifyTests/Verify.Avalonia/blob/main/readme.md)
documente verification des vues Avalonia, rendu Skia et comparateurs d'images.
Il exige `UseHeadlessDrawing = false` et `UseSkia()`. Ici : detecter une
colonne masquee ou une barre d'action deplacee sur les vues FM et Lua.

**Contrainte :** le [projet courant](https://github.com/VerifyTests/Verify.Avalonia/blob/main/src/Verify.Avalonia/Verify.Avalonia.csproj)
cible net10.0 ; ses [versions centrales](https://github.com/VerifyTests/Verify.Avalonia/blob/main/src/Directory.Packages.props)
emploient Avalonia 12.1.4, non 12.1.1. Verifier un tag compatible et la decouverte
xUnit avant adoption. Figer taille, polices, DPI et environnement ; examiner
chaque nouvelle reference, sans augmenter la tolerance pour masquer un defaut.
**Licence :** [MIT](https://github.com/VerifyTests/Verify.Avalonia/blob/main/license.txt).

### 3. dotnet/diagnostics — trouver les allocations et blocages

**Decision : priorite maintenant si les outils sont deja disponibles ; sinon
evaluation ulterieure sans installation pendant cette recherche.**
Le [README](https://github.com/dotnet/diagnostics/blob/main/README.md) est celui
des outils de diagnostic .NET. Les docs de
[dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)
et [dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)
decrivent compteurs runtime et traces EventPipe, y compris lancement d'un
processus enfant pour capturer le demarrage.

Ici : separer debit d'allocation, tas GC et memoire du processus pendant la
charge FM, puis localiser les piles couteuses. Commencer sur le banc manage
Release .NET 10 ; ne pas extrapoler sa couverture au binaire NativeAOT.
Un attachement apres `STRESS_PASS` rate la rafale initiale : capturer des le
demarrage. `dnx` telecharge aussi un outil, ce n'est pas une option sans download.
**Licence :** [MIT](https://github.com/dotnet/diagnostics/blob/main/LICENSE.TXT).

### 4. dotnet/BenchmarkDotNet — mesurer les algorithmes isoles

**Decision : ensuite, apres identification du chemin couteux.**
Le [README](https://github.com/dotnet/BenchmarkDotNet/blob/master/README.md)
decrit les mesures reproductibles ; le
[MemoryDiagnoser](https://benchmarkdotnet.org/articles/configs/diagnosers.html)
ajoute allocations et GC. Les [runtime monikers du code courant](https://github.com/dotnet/BenchmarkDotNet/blob/master/src/BenchmarkDotNet.Annotations/Jobs/RuntimeMoniker.cs)
comprennent .NET 10 et NativeAOT 10.

Ici : comparer projection d'historique et lint/fold/format Lua sur entrees
fixes. Projet de benchmark separe, Release, machine peu chargee ; verifier
la version publiee choisie. Ne mesure ni la fluidite du dispatcher ni toute
la memoire native Skia ; conserver les assertions fonctionnelles existantes.
**Licence :** [MIT](https://github.com/dotnet/BenchmarkDotNet/blob/master/LICENSE.md).

### 5. FlaUI/FlaUI — automatisation Windows et accessibilite observable

**Decision : ensuite, smoke natif cible ; pas un nouveau serveur MCP.**
Le [README](https://github.com/FlaUI/FlaUI/blob/master/README.md) decrit la
recherche et l'interaction avec les elements Windows via UIA2/UIA3.
Le [projet courant](https://github.com/FlaUI/FlaUI/blob/master/src/FlaUI.Core/FlaUI.Core.csproj)
inclut net10.0-windows. Avalonia expose ses controles via
[AutomationPeer et AutomationProperties](https://docs.avaloniaui.net/docs/app-development/accessibility).

Ici : verifier nom accessible, navigation Tab, focus et commandes visibles
de la fenetre QA, plutot que cliquer a des coordonnees. Windows et session
interactive requis ; controles personnalises a inspecter avant de promettre
leur couverture. Un test UIA ne certifie pas toute l'accessibilite. Lancer
le runner hors processus evite d'ajouter FlaUI a l'application NativeAOT.
**Licence :** [MIT](https://github.com/FlaUI/FlaUI/blob/master/LICENSE.txt).

### 6. LuaLS/lua-language-server — analyse semantique des scripts

**Decision : ensuite, d'abord dans l'editeur externe ; integration AvaloniaEdit
seulement si le gain est demontre.**
Le [README](https://github.com/LuaLS/lua-language-server/blob/master/README.md)
documente references, types, diagnostics et bibliotheques de definitions.
Les [reglages officiels](https://luals.github.io/wiki/settings/) exposent
`runtime.version` et `workspace.library`.

Ici : retrouver les appels et erreurs de signatures entre scripts, au-dela du
linter local. Configurer Lua 5.2, conforme a la
[cible MoonSharp](https://github.com/moonsharp-devs/moonsharp/blob/master/README.md), et produire des stubs
depuis `docs/portal/lua-api-doc.json`, sans inventer une deuxieme API.
LuaLS reste un processus externe ; dialecte MoonSharp et callbacks C# demandent
validation. Garder xUnit/MoonSharp pour les tests d'execution et d'annulation :
LuaLS ne les remplace pas.
**Licence :** [MIT](https://github.com/LuaLS/lua-language-server/blob/master/LICENSE).

### 7. obra/superpowers — methode de diagnostic pour agents

**Decision : maintenant en lecture ciblee ; rejeter l'installation globale
du framework pour ce chantier.**
Le [README](https://github.com/obra/superpowers/blob/main/README.md) presente
planification, TDD et orchestration. Les vrais skills
[systematic-debugging](https://github.com/obra/superpowers/blob/main/skills/systematic-debugging/SKILL.md)
et [verification-before-completion](https://github.com/obra/superpowers/blob/main/skills/verification-before-completion/SKILL.md)
imposent reproduction, recherche de cause et preuves recentes.

Ici : faire suivre chaque regression FM/Lua d'un cas hors jeu et d'un resultat
verifiable. Pas de dependance runtime ni de contrainte .NET ; en revanche,
hooks, validations et orchestration du framework feraient doublon avec OmO.
Ne pas importer ses regles de workflow comme nouvelles autorisations.
**Licence :** [MIT](https://github.com/obra/superpowers/blob/main/LICENSE).

### 8. ibelick/ui-skills — quelques controles qualite, pas un outillage natif

**Decision : maintenant en grille selective ; rejeter l'installation pour
un gain MCP ou performance Avalonia.**
Le [README](https://github.com/ibelick/ui-skills/blob/main/README.md) expose CLI,
registre et MCP `list_skills`/`get_skill` : il distribue du texte de skills,
il n'inspecte pas une application Windows.

Contenu effectivement lu :
- [baseline-ui](https://github.com/ibelick/ui-skills/blob/main/skills/baseline-ui/SKILL.md)
  exige Tailwind, `motion/react` et primitives React. Rejeter ces prescriptions ;
  retenir erreurs proches de l'action et etats vides exploitables. Son
  rejet des degrades violets ne doit pas dicter toute la palette Dofus.
- [fixing-motion-performance](https://github.com/ibelick/ui-skills/blob/main/skills/fixing-motion-performance/SKILL.md)
  vise DOM/CSS, mesures et animations. Retenir limitation du travail repetitif
  et effets couteux ; pas de preuve concernant allocations .NET ou Skia.
- [fixing-accessibility](https://github.com/ibelick/ui-skills/blob/main/skills/fixing-accessibility/SKILL.md)
  vise HTML/ARIA. Retenir noms, clavier, focus et erreurs ; traduire en
  proprietes Avalonia et verification UIA, pas ajouter des attributs ARIA AXAML.

**Licence :** [MIT](https://github.com/ibelick/ui-skills/blob/main/LICENSE).

## Autres liens fournis : choix complementaires, hors selection prioritaire

- **heydamir/unslop** : [README](https://github.com/heydamir/unslop/blob/main/README.md),
  [licence CC BY 4.0](https://github.com/heydamir/unslop/blob/main/LICENSE).
  Audit et passes de redesign ; exemples web, video avec Playwright/ffmpeg.
  **Ecarter comme outil de test natif** ; conserver l'inspection avant/apres
  et la direction issue du produit, avec attribution si contenu reutilise.
- **nextlevelbuilder/ui-ux-pro-max-skill** :
  [skill fourni](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill/blob/main/.claude/skills/ui-ux-pro-max/SKILL.md),
  [MIT](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill/blob/main/LICENSE).
  Conseils de design et recherche Python ; le skill exclut explicitement
  backend et performance non visuelle. **Conserver pour revue visuelle**,
  pas pour diagnostiquer le GC, tester Lua ou valider AXAML.
- **Nutlope/hallmark** : [README](https://github.com/Nutlope/hallmark/blob/main/README.md),
  [MIT](https://github.com/Nutlope/hallmark/blob/main/LICENSE) ; photos bastionbots
  a leurs licences propres. HTML/CSS autonome, `sloplint --render` en navigateur,
  controles de preservation `--before`/`--kept` des liens, champs et handlers.
  **Rejeter son linter pour Avalonia** : pas de validation AXAML/bindings.
  Retenir la preuve de conservation des commandes et la comparaison avant/apres ;
  ici, les prouver par tests C#, arbre UIA et captures natives, pas par DOM.
- **pbakaus/impeccable** :
  [README](https://github.com/pbakaus/impeccable/blob/main/README.md),
  [Apache 2.0](https://github.com/pbakaus/impeccable/blob/main/LICENSE).
  Playbooks `critique`, `audit`, `harden`, `adapt` et `optimize` ; les ecrans
  d'application sont construits en code selon le README, ce qui ne prouve pas
  un backend Avalonia. **Conserver les grilles de revue**, pas les CLI/hooks :
  le controle rendu utilise une URL et Chrome/Edge, pas une fenetre AXAML.
  Le moteur autonome peut etre telecharge au premier usage par un hook ; le
  catalogue de directions est contacte meme avec telemetrie desactivee.
  Aucun besoin de ces mecanismes pour le banc natif existant ; aucune installation.
- **Panniantong/Agent-Reach** :
  [README anglais](https://github.com/Panniantong/Agent-Reach/blob/main/docs/README_en.md),
  [guide d'installation](https://github.com/Panniantong/Agent-Reach/blob/main/docs/install.md),
  [pyproject](https://github.com/Panniantong/Agent-Reach/blob/main/pyproject.toml),
  [MIT](https://github.com/Panniantong/Agent-Reach/blob/main/LICENSE).
  Version source 1.6.0, Python >=3.10 ; canaux web/GitHub/YouTube/V2EX/Bilibili
  par defaut. **Plus tard si besoin de sous-titres YouTube ou de veille
  communautaire inaccessible autrement** ; web/GitHub doublonnent les outils
  de recherche deja disponibles, sans tester .NET/Avalonia.
  Transcription vocale avec cle API ; sous-titres Bilibili avec connexion.
  Le guide documente Windows via uv et ZIP GitHub officiel, et precise que
  le paquet homonyme PyPI est un autre projet. Les canaux avec connexion
  peuvent copier automatiquement la session Chrome du site au premier usage :
  ni setup, ni canal connecte utilise ici. Compatibilite native non testee.

## Un workflow applicable tout de suite, sans nouvel outil

**Objectif : rendre reproductible la verification FM et isoler le probleme
avant toute nouvelle dependance.** Procedure proposee, non executee par cette
recherche documentaire ; coordonner son lancement avec le chantier UI en cours.

1. Dans ce worktree, avec SDK .NET 10, restauration deja faite et projets
   voisins requis accessibles, lancer uniquement le banc synthetique :

   ```powershell
   dotnet run --project tools/ForgeUiStress/ForgeUiStress.csproj -c Release --no-restore
   ```

2. Observer le signal reel `STRESS_PASS events=10000 rows=1`, ou relever
   `STRESS_FAIL` et sa pile. Le code s'abonne a `CollectionChanged` avant
   production et borne l'attente a dix secondes : ne pas remplacer cela par
   un delai fixe. La fenetre reste ouverte apres succes ; la fermer pour
   terminer le processus. Ce signal prouve le bornage teste, pas toute la fluidite.
3. Relever `elapsedMs` et `allocatedBytes`, puis verifier visuellement la fenetre
   initiale 1440x900 et la largeur minimale 960 : scroll borne, controles
   accessibles au clavier, pas de colonne masquee. Conserver captures et commande
   exacte avec le diff ; la mesure d'allocation n'est pas le working set.
4. Pour une modification Lua, reutiliser la suite ciblee existante, sans
   remplacer MoonSharp par un interpreteur Lua externe :

   ```powershell
   dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LuaTooling"
   ```

   Les dependances voisines du projet complet restent requises. Les tests purs
   de la CI sont un controle distinct, pas une preuve des interactions natives.
5. Si la charge reste couteuse, appliquer la methode de diagnostic du candidat 7 :
   une hypothese, une trace prise des le demarrage avec le candidat 3 quand
   disponible, un changement, puis le meme cas de charge. Ajouter ensuite
   BenchmarkDotNet pour le calcul isole et Verify/FlaUI pour les regressions
   observees. Aucun achat, lancement de script de jeu ou reprise de forge reel.

**Ordre recommande :** banc existant et preuves > diagnostic allocations >
tests Headless separes > snapshots et UIA > LuaLS si analyse inter-fichiers utile.
Les skills web restent des lectures ; aucun besoin d'installation n'est etabli.
