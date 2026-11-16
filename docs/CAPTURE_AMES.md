# Capture d'âmes (archimonstres)

## Règles retenues
- **Pierre d'âme équipée obligatoire** : sans pierre équipée, le sort « Capture d'âmes » (id 413) est grisé. La pierre prend l'emplacement de l'arme.
- **Puissance** : la pierre doit pouvoir contenir le monstre le plus fort du groupe (niveau ≤ puissance). Puissances relevées dans les données du client (type 83, action 705) :
  Petite 50 · Moyenne 100 · Grande 150 · Énorme 190 · Gigantesque 1000 (objets 9686 à 9690).
- **Relance du sort : 3 tours.** **L'état de capture dure 2 tours** et le combat doit être **terminé dans ce délai**, sinon rien n'est capturé.
- Sources publiques : guide Guidactik (Unity), actualité Millenium (mise à jour du 08/02/2022 : pierre obligatoire, relance 6 → 3 tours). Les valeurs exactes (durée, relance) sont **à confirmer en jeu**.

## Ce que fait Givre
`GivreClone.App/Services/Combat/SoulCapture.cs` (pur, testé) décide, à chaque tour :
1. aucun archimonstre capturable dans le groupe → rien ;
2. niveau inconnu → attendre ; niveau hors de portée de toute pierre → rien ;
3. pierre équipée trop faible ou absente → **équiper la plus petite pierre suffisante du sac** (rien si aucune) ;
4. sort en relance ou PA insuffisants → attendre ;
5. dégâts par tour pas encore mesurés → attendre un tour (observation) ;
6. **lancer seulement si les dégâts attendus pendant l'état couvrent les PV restants avec 25 % de marge** : `dégâts/tour × (part du tour de lancer + 1) ≥ PV × 1,25`
   (le tour du lancer ne compte que pour les PA qui restent après le sort ; les dégâts/tour sont mesurés pendant le combat, en prenant le plus prudent des deux derniers tours).

## En script
Module Lua `soul` : `soul.stones()`, `soul.requiredStone(niveau)`, `soul.isCapturable(idMonstre)`, `soul.status()`, `soul.observe()` (début de chaque tour),
`soul.advise{capturable=true, window=2, margin=1.25}`, `soul.equipStone()`, `soul.tryCapture{...}` → `"equipped" | "cast" | "wait" | "none"` + raison.
Générateur : MCP `find_capturable_monsters` puis `make_soul_script(monsters=[ids], save=true)` → script prêt (FORCE_MONSTERS + `onTurn` : `soul.observe()`, `soul.tryCapture()`, `combat:playTurn()`).

## À vérifier en jeu (je n'ai pas pu)
1. Durée de l'état et relance réelles (le code suppose 2 et 3 ; constantes `StateTurns` / `CooldownTurns`).
2. Que le niveau des ennemis soit bien relevé à l'entrée en combat (`FighterLevel`) — sinon `soul.status().levelsKnown` est faux et le script attend sans jamais lancer.
3. Que « la pierre équipée » soit bien lue (objet équipé d'id 9686-9690, position ≠ sac) et que l'équipement en combat soit accepté.
4. Que `soul.isCapturable` (âme listée dans les données) corresponde aux archimonstres réellement capturables (le drapeau « capture interdite » n'est pas décodé).
5. La cible du sort : le script vise l'ennemi de plus haut niveau ; à ajuster si la capture exige un monstre précis.
Tant que ces points ne sont pas confirmés, lancer le script sous surveillance.

## Surbrillance et liens de trajet
- **Carte interactive** : les groupes qui contiennent un archimonstre ou un monstre à âme capturable sont mis en surbrillance (liste d'archimonstres du client + données du jeu).
- **Surbrillance dans le jeu lui-même** (contour des monstres à travers les murs) : non faite — c'est un module natif (rendu du client) que je ne peux ni écrire ni vérifier à l'aveugle pour 3.7.12.12.
- **Liens de trajet** : sur le téléphone, tout `[x,y]` dans la console est cliquable (« Envoyer le bot en [x,y] ? ») ; MCP `travel_to(bot, x, y | mapId)`.
