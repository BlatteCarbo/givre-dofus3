# Mod overlay FM en jeu (MITM) - 2026-11-07 soir

## Demande utilisateur
"je voulais un mod pour mitm FM, par exemple sur le bot quand on est en mitm on peut choisir
genre le jet qu'on veut sur un item, et il nous montre dans le jeu quoi choisir" (reference
explicite a github.com/BenHard1/forgemagie-dofus, un outil OCR+overlay en lecture seule).

## Constat avant de coder
GivreClone a DEJA un moteur de forgemagie automatique complet (ForgeEngine, LaunchService.Forge.cs,
RuneAdvisor, protocole reel ExchangeCraftResultEvent) - strictement superieur a l'OCR de
forgemagie-dofus. Et `SmithMagicViewModel.Advice.cs` avait deja un mode "Conseil" (ne pose jamais de
rune) avec un seam EXPLICITEMENT laisse non branche :
`public Action<ForgeAdviceRecord>? InGameAdviceBridge { get; set; }` - commente
"Seam for the later in-game bridge."

Le vendor `native/GivreCombatVisuals/vendor/dofus-enhanced/` est deja le code source C++ de
Dofus Enhanced (features d'apparence/animations), integre comme base native existante.

## Ce qui a ete code (branche le seam, rien de plus)
- `GivreClone.App/Services/ForgeAdvice/ForgeOverlayLayout.cs` (NOUVEAU, pur, teste) : calcule
  la position pixel d'une case Ba/Pa/Ra a partir d'une calibration (origine 1re ligne + hauteur de
  ligne = espacement colonnes, comme une grille carree).
- `ForgeOverlayCalibrationStore.cs` (NOUVEAU) : persiste la calibration dans
  `userdata/forge/overlay-calibration.json`.
- `Views/ForgeOverlayWindow.axaml(.cs)` (NOUVEAU) : fenetre Avalonia transparente, sans decoration,
  Topmost, rendue CLICK-THROUGH par `WS_EX_TRANSPARENT|WS_EX_LAYERED|WS_EX_TOOLWINDOW` (Win32,
  jamais vole de focus/clic au jeu). Suit la fenetre Dofus du pid MITM (`GameTagger.FindMainWindowForOverlay`,
  expose en public). Dessine un cadre vert + etiquette sur la case conseillee. Calibration manuelle
  en 4 clics (Ba/Pa/Ra de la 1re ligne, Ba de la 2e ligne) exactement comme forgemagie-dofus, mais
  une seule fois et sans OCR.
- `ForgeOverlayController.cs` (NOUVEAU) : branche `InGameAdviceBridge` sur la fenetre, boucle de
  suivi de fenetre (DispatcherTimer 500ms), traduit `ForgeAdviceRecord.RuneGid` -> index de ligne
  (via `_vm.Lines`) + tier (via `RuneItemsByGid()`).
- `SmithMagicViewModel.Editor.cs` : `public int Pid => _pid;` ajoute (etait prive).
- `GameTagger.cs` : `FindMainWindow` expose en public via `FindMainWindowForOverlay`.
- `Views/SmithMagicView.axaml(.cs)` : 2 boutons ajoutes dans le bloc mode Conseil -
  "Calibrer l'affichage en jeu" et toggle "Afficher en jeu (MITM)".

## Garantie "lecture seule" (meme promesse que forgemagie-dofus)
Le mod ne pose JAMAIS de rune a la place du joueur : `ForgeAdviceController.Observe()` ne fait que
projeter l'etat en `ForgeAdviceRecord`, jamais d'appel a `ForgeAsync`/`CraftPutItemAsync`. L'overlay
est purement visuel et click-through.

## Tests
`GivreClone.Tests/ForgeOverlayLayoutTests.cs` (NOUVEAU, 5 tests, pur) : position Ba a l'origine,
decalage colonnes Pa/Ra, decalage ligne suivante, tier/index invalides -> null, mise a l'echelle DPI.
Build Release exit 0. Suite complete relancee pour verifier l'absence de regression (en cours au
moment de cette note).

## Reste a valider en live (non fait cette session)
- Calibration reelle sur un client MITM ouvert (clic sur les 4 reperes), persistance du JSON.
- Verification visuelle que le cadre suit bien la fenetre pendant un deplacement/resize.
- Verification que le mode Conseil publie bien des `ForgeAdviceRecord` a jour pendant une vraie FM.
