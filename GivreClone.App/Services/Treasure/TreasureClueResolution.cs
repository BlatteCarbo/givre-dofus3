using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services.Treasure;

public static class TreasureClueResolution
{
    public static async Task<(long? Map, string? Error)> ResolveAsync(string name,
        Task<IReadOnlyList<TreasureClue>> loading, Func<int, int, long?> mapAt, CancellationToken ct)
    {
        try
        {
            var clues = TreasureClues.Matches(await loading.ConfigureAwait(false), name);
            ct.ThrowIfCancellationRequested();
            if (clues.Count == 0) return (null, $"« {name} » introuvable dans DofusDB dans cette direction.");
            long? target = mapAt(clues[0].X, clues[0].Y);
            return target is not null ? (target, null)
                : (null, $"Indice « {name} » trouvé dans DofusDB en [{clues[0].X},{clues[0].Y}], mais carte absente du catalogue local.");
        }
        catch (HttpRequestException)
        {
            return (null, "DofusDB inaccessible : impossible de vérifier l'indice. Réessayez lorsque la connexion est rétablie.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, "DofusDB n'a pas répondu dans le délai prévu. Réessayez la recherche de l'indice.");
        }
        catch (FormatException)
        {
            return (null, "Réponse DofusDB invalide : aucune carte d'indice ne peut être confirmée.");
        }
    }
}
