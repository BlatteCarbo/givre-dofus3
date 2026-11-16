using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services;
using GivreClone.App.Services.Treasure;

namespace GivreClone.App.ViewModels;

// Onglet Trésor, « Y aller » : carte de l'étape en cours depuis la carte du perso. Étape « direction » : N cartes vers le côté
// indiqué (TreasureTarget.Offset). Étape « indice » : recherche DofusDB (TreasureClueService, cache disque), indice le plus proche.
public partial class BotsViewModel
{
    private static async Task<(long? Map, long Here, string? Error)> ResolveTreasureStepMapAsync(uint pid, LaunchService.TreasureStep step, CancellationToken ct)
    {
        var launch = Stores.Launch;
        long here = launch.CurrentMap(pid) ?? 0;
        if (here <= 0 || launch.MapCoordOf(here) is not (int x, int y)) return (null, here, "Position du perso inconnue.");
        int? world = launch.WorldMapOf(here);
        if (step.Kind == "direction")
            return TreasureTarget.Offset(x, y, step.Direction, step.MapCount) is (int tx, int ty) ? (launch.MapIdAt(tx, ty, world), here, null) : (null, here, "Direction inconnue.");
        if (step.Kind == "poi")
        {
            if (launch.GameNames.PoiName(step.PoiLabelId) is not { Length: > 0 } name) return (null, here, "Nom de l'indice inconnu.");
            var result = await TreasureClueResolution.ResolveAsync(name,
                TreasureClueService.LoadAsync(x, y, step.Direction, ct),
                (tx, ty) => launch.MapIdAt(tx, ty, world), ct);
            return (result.Map, here, result.Error);
        }
        return (null, here, "Cette étape ne se résout pas par une carte (phorreur, combat, creuser).");
    }
}
