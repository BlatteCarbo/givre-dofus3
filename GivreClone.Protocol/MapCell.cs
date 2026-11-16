namespace GivreClone.Protocol;

/// <summary>
/// Une cellule de map Dofus 3 (donnee statique de la carte, extraite du bundle Unity du client) : sa marchabilite
/// (Mov), sa ligne de vue (Los), son elevation (Floor) et son masque de CHANGEMENT DE CARTE (MapChangeData : bits
/// par direction ou l'on peut quitter la carte depuis cette cellule - 1=Est/Droite, 4=Sud/Bas, 16=Ouest/Gauche,
/// 64=Nord/Haut ; diagonales sur les bits impairs). CellId = numero de cellule 0-559. Sert au rendu relief de la
/// grille tactique, au pathfinding, et a l'identification des cellules de transition.
/// <para>NonWalkableDuringFight = case marchable HORS combat mais BLOQUEE en combat (champ Dofus 3
/// `nonWalkableDuringFight` `IsWalkableFight = Mov AndAlso Not NonWalkableDuringFight`). En combat la
/// marchabilite reelle = Mov &amp;&amp; !NonWalkableDuringFight ; sinon le serveur IGNORE en silence un move qui atterrit
/// dessus. Hors combat, on ignore ce flag (Mov seul).</para>
/// </summary>
public sealed record MapCell(int CellId, bool Mov, bool Los, int Floor, int MapChangeData = 0, int MoveZone = 0, int LinkedZone = 0, bool NonWalkableDuringFight = false);
