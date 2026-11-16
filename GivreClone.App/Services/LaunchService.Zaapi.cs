using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    /// <summary>Registre partagé des Zaapis connus par ville/sous-zone.</summary>
    public ZaapiRegistry ZaapiRegistry { get; } = new();

    // Dernière liste zaapi (TeleportDestinationsEvent type 1). Distincte des zaaps du personnage :
    // une ville n'envoie pas les mêmes cartes qu'une autre, et cette liste ne doit pas écraser le zaap.
    private readonly ConcurrentDictionary<uint, ZaapiBook> _zaapiBooks = new();

    private void RememberZaapi(uint pid, long characterId, IReadOnlyList<TeleportReader.Destination> dests)
    {
        if (dests.Count == 0) return;
        _zaapiBooks[pid] = ZaapiBook.From(characterId, dests);
        ZaapiRegistry.RegisterTeleportDestinations(CurrentMap(pid) ?? 0, dests, id => DistrictName(id));
    }

    private bool TryZaapiBook(uint pid, out ZaapiBook book)
    {
        if (_zaapiBooks.TryGetValue(pid, out book!) && book.Matches(CharacterId(pid))) return true;
        book = null!;
        return false;
    }

    /// <summary>Vrai si un TeleportDestinationsEvent type 1 a été retenu pour ce personnage.</summary>
    public bool ZaapiListReceived(uint pid) => TryZaapiBook(pid, out var book) && book.Rows.Count > 0;

    /// <summary>Coût annoncé pour cette carte, ou false si elle n'est pas dans la dernière liste zaapi. cost null = coût absent du paquet.</summary>
    public bool TryZaapiCost(uint pid, long mapId, out long? cost)
    {
        cost = null;
        return TryZaapiBook(pid, out var book) && book.Listed(mapId, out cost);
    }

    /// <summary>
    /// Lignes du dernier zaapi : nom, quartier, coordonnées et onglet lus dans les données du jeu pour les cartes envoyées.
    /// Le coût est celui du paquet. Sans données de jeu, la carte et le coût restent, les libellés sont vides.
    /// </summary>
    public IReadOnlyList<ZaapiStop> ZaapiStops(uint pid)
    {
        if (!TryZaapiBook(pid, out var book) || book.Rows.Count == 0) return System.Array.Empty<ZaapiStop>();
        IReadOnlyList<ZaapiHint> hints = System.Array.Empty<ZaapiHint>();
        try
        {
            if (GameData?.MapHints(book.MapIds()) is { } rows)
                hints = rows.Select(r => new ZaapiHint(r.MapId, r.Name, r.CategoryId, r.Category, r.X, r.Y)).ToArray();
        }
        catch { hints = System.Array.Empty<ZaapiHint>(); }
        return ZaapiTravel.Present(book.Rows, hints, DistrictName, MapCoordPair);
    }

    private string? DistrictName(int subAreaId)
    {
        if (subAreaId <= 0) return null;
        try { return GameData?.SubArea(subAreaId)?.Name; }
        catch { return null; }
    }

    private (int X, int Y)? MapCoordPair(long mapId)
        => MapCoordOf(mapId) is (int x, int y) ? (x, y) : null;

    /// <summary>Destinations du zaapi OUVERT, avec le coût seulement si TeleportDestination.cost était présent.</summary>
    public IReadOnlyList<(long MapId, long? CostKamas)> ZaapiQuotes(uint pid)
    {
        if (TeleportView(pid) is not { Type: ZaapiTravel.ListType } view) return System.Array.Empty<(long, long?)>();
        var copy = new List<(long, long?)>(view.Destinations.Count);
        foreach (var dest in view.Destinations)
        {
            if (dest.MapId <= 0) continue;
            copy.Add((dest.MapId, dest.CostKnown && dest.Cost >= 0 ? dest.Cost : null));
        }
        return copy;
    }

    public ZaapiOffer AssessZaapi(uint pid, long mapId)
    {
        if (TeleportView(pid) is not { Type: ZaapiTravel.ListType } view)
            return ZaapiTravel.Evaluate(mapId, false, null, Kamas(pid));
        foreach (var dest in view.Destinations)
        {
            if (dest.MapId != mapId) continue;
            long? cost = dest.CostKnown && dest.Cost >= 0 ? dest.Cost : null;
            return ZaapiTravel.Evaluate(mapId, true, cost, Kamas(pid));
        }
        return ZaapiTravel.Evaluate(mapId, false, null, Kamas(pid));
    }

    /// <summary>
    /// TeleportRequest { map_id, source_type = 1, destination_type = 1 } pendant que le zaapi est ouvert.
    /// true = paquet injecté. L'arrivée se juge sur le changement de carte. Le coût injecté est celui de l'événement.
    /// </summary>
    public bool TrySendZaapi(uint pid, long mapId)
    {
        var offer = AssessZaapi(pid, mapId);
        if (!offer.CanSend)
        {
            TransportLog.Line($"[zaapi] pid {pid} : {offer.Reason}");
            return false;
        }
        var relay = RelayForInject();
        if (relay is null)
        {
            TransportLog.Line($"[zaapi] pid {pid} : envoi impossible (pas de relais)");
            return false;
        }
        string? code = _catalog?.CodeForName("TeleportRequest");
        if (code is null)
        {
            TransportLog.Line($"[zaapi] pid {pid} : code TeleportRequest non mappé (session ?)");
            return false;
        }
        byte[] pkt = MoveForge.BuildTeleportFramed(code, mapId, ZaapiTravel.SourceType, ZaapiTravel.DestinationType);
        if (pkt.Length == 0)
        {
            TransportLog.Line($"[zaapi] pid {pid} : TeleportRequest NON forgé ({MoveForge.LastForgeError})");
            return false;
        }
        long quote = offer.CostKamas ?? -1;
        bool ok = SendSmithSpend(pid, "travel", quote, 0, 0, 0, mapId, () => relay.Inject(pid, pkt), out _);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[zaapi] pid {pid} : TeleportRequest map_id={mapId} source_type={ZaapiTravel.SourceType} destination_type={ZaapiTravel.DestinationType} coût {WorldMapZaapTravel.FormatCost(offer.CostKamas)} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    // Cartes dont le world-graph porte le skill zaapi. Recalculé seulement quand le graphe change.
    private WorldGraph? _zaapiSkillGraph;
    private int[] _zaapiSkillMaps = Array.Empty<int>();

    /// <summary>Cartes du graphe dont une transition porte le skill zaapi (157). Vide tant que le graphe n'est pas chargé.</summary>
    public IReadOnlyList<int> ZaapiSkillMaps()
    {
        var graph = _worldGraph;
        if (!ReferenceEquals(graph, _zaapiSkillGraph))
        {
            _zaapiSkillGraph = graph;
            _zaapiSkillMaps = graph is null
                ? Array.Empty<int>()
                : graph.SourceMapsWithSkill(ZaapiTravel.SkillId).ToArray();
            if (graph is not null)
                ZaapiRegistry.RegisterFromWorldGraph(graph, m => SubAreaOf(m), s => DistrictName(s));
        }
        return _zaapiSkillMaps;
    }

    /// <summary>
    /// Zaapi le plus proche à pied : skill du graphe, plus les cartes où un zaapi a déjà été vu.
    /// Null si le graphe ne joint aucune de ces cartes en 80 sauts libres.
    /// </summary>
    public (long MapId, int Hops)? NearestZaapiHost(long here, IEnumerable<long>? also)
    {
        if (here <= 0 || here > int.MaxValue) return null;
        var graph = _worldGraph;
        if (graph is null) return null;
        var goals = new HashSet<int>();
        foreach (var id in ZaapiSkillMaps())
            if (id > 0) goals.Add(id);
        foreach (var id in ZaapiRegistry.GetAllZaapiMaps())
            if (id > 0 && id <= int.MaxValue) goals.Add((int)id);
        if (also is not null)
        {
            foreach (var id in also)
                if (id > 0 && id <= int.MaxValue) goals.Add((int)id);
        }
        var found = graph.NearestFree((int)here, goals);
        return found is { } hit ? (hit.MapId, hit.Hops) : null;
    }

    /// <summary>Vrai si deux cartes se situent dans la même ville.</summary>
    public bool AreInSameCity(long mapA, long mapB)
        => ZaapiRegistry.AreInSameCity(mapA, mapB, m => SubAreaOf(m), s => GameData?.SubArea(s)?.Area ?? DistrictName(s));

    /// <summary>
    /// Calcule un saut Zaapi avantageux si origin et targetMap sont dans la même ville
    /// et que la marche directe dépasse le seuil choisi (défaut 4 cartes).
    /// </summary>
    public ZaapiHop? PlanZaapiHop(uint pid, long originMap, long targetMap, int directWalkHops, int threshold = ZaapiRegistry.DefaultCityWalkThreshold)
    {
        return ZaapiRegistry.PlanZaapiHop(
            originMap,
            targetMap,
            directWalkHops,
            threshold,
            (from, to) => WorldPath(from, to, pid: pid),
            m => SubAreaOf(m),
            s => GameData?.SubArea(s)?.Area ?? DistrictName(s),
            Kamas(pid));
    }
}
