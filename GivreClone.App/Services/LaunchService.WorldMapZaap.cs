using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    // Zaaps du personnage. ZaapKnownListEvent (connexion) les pose sans coût. Une liste de type 0 ou 3
    // annonce ensuite le coût, sans retirer les zaaps déjà débloqués.
    private readonly ConcurrentDictionary<uint, CharacterZaapBook> _worldMapZaaps = new();

    private void RememberUnlockedZaaps(uint pid, IReadOnlyCollection<long> maps)
    {
        if (maps is null || maps.Count == 0) return;
        long characterId = CharacterId(pid);
        var book = TryWorldMapBook(pid, out var current) ? current.Copy() : new CharacterZaapBook();
        book.SeedUnlocked(characterId, maps);
        _worldMapZaaps[pid] = book;
    }

    private void RememberWorldMapZaaps(uint pid, int listType, long characterId, IReadOnlyList<TeleportReader.Destination> dests)
    {
        if (!WorldMapZaapTravel.IsCharacterZaapList(listType) || dests.Count == 0) return;
        var book = TryWorldMapBook(pid, out var current) ? current.Copy() : new CharacterZaapBook();
        book.LearnCosts(listType, characterId,
            dests.Select(d => (d.MapId, d.CostKnown && d.Cost >= 0 ? (long?)d.Cost : null)));
        _worldMapZaaps[pid] = book;
    }

    private bool TryWorldMapBook(uint pid, out CharacterZaapBook book)
    {
        if (_worldMapZaaps.TryGetValue(pid, out book!) && book.Matches(CharacterId(pid))) return true;
        book = null!;
        return false;
    }

    /// <summary>Zaaps connus pour la carte du monde, avec leur dernier coût annoncé (nul si le champ cost était absent).</summary>
    public IReadOnlyList<(long MapId, long? CostKamas)> WorldMapZaapQuotes(uint pid)
        => TryWorldMapBook(pid, out var book) ? book.Snapshot() : System.Array.Empty<(long, long?)>();

    /// <summary>Coût affichable : « N kamas » ou « inconnu ».</summary>
    public string WorldMapZaapCostText(uint pid, long mapId)
        => WorldMapZaapTravel.FormatCost(AssessWorldMapZaap(pid, mapId).CostKamas);

    public WorldMapZaapOffer AssessWorldMapZaap(uint pid, long mapId)
    {
        if (!TryWorldMapBook(pid, out var book) || !book.TryGet(mapId, out var cost))
            return WorldMapZaapTravel.Evaluate(mapId, false, null, Kamas(pid));
        return WorldMapZaapTravel.Evaluate(mapId, true, cost, Kamas(pid));
    }

    /// <summary>
    /// Envoie TeleportRequest { map_id, source_type = 5 } sans ouvrir de zaap ni de havre-sac.
    /// true = paquet injecté, pas l'arrivée. L'appelant ne réussit que si la carte observée devient mapId.
    /// </summary>
    public bool TrySendWorldMapZaap(uint pid, long mapId, bool allowUnknownCost = false)
    {
        var offer = AssessWorldMapZaap(pid, mapId);
        bool listedWithoutCost = allowUnknownCost && offer.Known && offer.CostKamas is null;
        if (!offer.CanSend && !listedWithoutCost)
        {
            TransportLog.Line($"[zaap] pid {pid} : {offer.Reason}");
            return false;
        }
        var relay = RelayForInject();
        if (relay is null)
        {
            TransportLog.Line($"[zaap] pid {pid} : zaap depuis la carte impossible (pas de relais)");
            return false;
        }
        string? code = _catalog?.CodeForName("TeleportRequest");
        if (code is null)
        {
            TransportLog.Line($"[zaap] pid {pid} : code TeleportRequest non mappé (session ?)");
            return false;
        }
        byte[] pkt = MoveForge.BuildTeleportFramed(code, mapId, WorldMapZaapTravel.SourceType);
        if (pkt.Length == 0)
        {
            TransportLog.Line($"[zaap] pid {pid} : TeleportRequest NON forgé ({MoveForge.LastForgeError})");
            return false;
        }
        long quote = offer.CostKamas ?? -1;
        bool ok = SendSmithSpend(pid, "travel", quote, 0, 0, 0, mapId, () => relay.Inject(pid, pkt), out _);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[zaap] pid {pid} : TeleportRequest map_id={mapId} source_type={WorldMapZaapTravel.SourceType} coût {WorldMapZaapTravel.FormatCost(offer.CostKamas)} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }
}
