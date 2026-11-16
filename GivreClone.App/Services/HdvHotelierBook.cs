using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GivreClone.App.Services;

/// <summary>Carnet des hôteliers d'HDV : pour une carte, l'élément interactif à utiliser et son identifiant d'instance de skill. Sur certaines cartes (ex. Astrub, patch 3.7.12.12) le serveur ne
/// déclare PAS l'hôtelier dans la liste des interactifs de la carte, alors que le client l'ouvre quand même : le bot ne le « voyait » donc pas (« hôtelier introuvable »). Le carnet est
/// alimenté (1) par des valeurs relevées dans de vraies captures, (2) automatiquement quand tu ouvres un HDV à la main avec un client MITM (InteractiveUseRequest suivi d'un HDV ouvert).</summary>
public static class HdvHotelierBook
{
    public sealed record Entry(int ElementId, int SkillInstanceUid);

    // Relevés de capture (21/10/2026, 3.7.12.12) : Astrub, HDV des ressources [4,-17] -> élément 515220, instance de skill 23002 (skill 355).
    private static readonly Dictionary<long, Entry> Seeds = new() { [191104004] = new Entry(515220, 23002) };

    private static readonly ConcurrentDictionary<long, Entry> Learned = new();
    private static bool _loaded;
    private static readonly object Gate = new();

    private static string FilePath => Path.Combine(MZP19LocalClone.DataRoot, "hdv-hoteliers.json");

    private static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (File.Exists(FilePath) && JsonSerializer.Deserialize<Dictionary<string, int[]>>(File.ReadAllText(FilePath)) is { } d)
                    foreach (var kv in d) if (long.TryParse(kv.Key, out var map) && kv.Value is { Length: 2 }) Learned[map] = new Entry(kv.Value[0], kv.Value[1]);
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { /* carnet illisible : on repart des relevés intégrés */ }
        }
    }

    /// <summary>Hôtelier connu pour cette carte (appris en priorité, sinon relevé intégré), ou null.</summary>
    public static Entry? Get(long mapId)
    {
        EnsureLoaded();
        return Learned.TryGetValue(mapId, out var e) ? e : Seeds.TryGetValue(mapId, out var s) ? s : null;
    }

    /// <summary>Mémorise (et enregistre) un hôtelier observé ; sans effet si déjà connu à l'identique.</summary>
    public static bool Learn(long mapId, int elementId, int skillInstanceUid)
    {
        if (mapId <= 0 || elementId <= 0 || skillInstanceUid <= 0) return false;
        EnsureLoaded();
        var e = new Entry(elementId, skillInstanceUid);
        if (Get(mapId) == e) return false;
        Learned[mapId] = e;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Learned.ToDictionary(kv => kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), kv => new[] { kv.Value.ElementId, kv.Value.SkillInstanceUid })));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* non persisté : valable pour cette session */ }
        return true;
    }
}
