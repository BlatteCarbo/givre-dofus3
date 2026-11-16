namespace GivreClone.App.Services.Treasure;

/// <summary>Cible de l'étape de chasse en cours (panneau « Chasse » de la fiche du bot) : texte lisible et, pour une étape
/// « direction » (N cartes vers un côté), la case d'arrivée. Directions du jeu : 0 droite, 2 bas, 4 gauche, 6 haut.</summary>
public static class TreasureTarget
{
    public static (int X, int Y)? Offset(int x, int y, int direction, int count) => direction switch
    {
        0 => (x + count, y), 2 => (x, y + count), 4 => (x - count, y), 6 => (x, y - count), _ => null,
    };

    public static string Arrow(int direction) => direction switch { 0 => "→", 2 => "↓", 4 => "←", 6 => "↑", _ => "?" };

    /// <summary>Étape lisible : « Étape 2/5 · ↑ Indice : Rocher cassé », « ↓ 4 carte(s) », « ← Phorreur », « Combat final », « Creuser ».</summary>
    public static string Describe(string kind, int direction, int mapCount, string? poiName, int checkPoint, int totalCheckPoint)
    {
        string head = totalCheckPoint > 0 ? $"Étape {checkPoint + 1}/{totalCheckPoint} · " : "";
        return head + kind switch
        {
            "poi" => $"{Arrow(direction)} Indice : {(string.IsNullOrWhiteSpace(poiName) ? "inconnu" : poiName)}",
            "direction" => $"{Arrow(direction)} {mapCount} carte(s)",
            "hint" => $"{Arrow(direction)} Phorreur (suivre jusqu'à le voir)",
            "fight" => "Combat final : lancer le combat du coffre",
            "dig" => "Creuser ici",
            _ => "Étape inconnue",
        };
    }

    /// <summary>« Y aller » n'a de sens que pour une étape dont la carte d'arrivée se calcule (direction) ou se cherche (indice).</summary>
    public static bool CanGo(string kind) => kind is "poi" or "direction";
}
