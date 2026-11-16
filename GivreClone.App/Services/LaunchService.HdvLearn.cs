using System;
using System.Collections.Concurrent;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

// Apprend les hôteliers d'HDV : une utilisation d'interactif (InteractiveUseRequest) suivie, dans les 4 s, de l'ouverture d'un HDV = « cet élément, avec ce skill, ouvre l'HDV de cette carte ».
public sealed partial class LaunchService
{
    private readonly ConcurrentDictionary<uint, (int Element, int Skill, long Tick)> _lastInteractiveUse = new();

    private void OnHdvLearnPacket(DecodedPacket p)
    {
        if (p.Name is null) return;
        try
        {
            if (p.ClientToServer && p.Name == "InteractiveUseRequest")
            {
                int skillF = LookField("InteractiveUseRequest", "skill_instance_uid") ?? -1, elemF = LookField("InteractiveUseRequest", "element_id") ?? -1;
                if (skillF < 0 || elemF < 0) return;
                int skill = (int)PayloadReader.Int(p.Payload, skillF, 0), elem = (int)PayloadReader.Int(p.Payload, elemF, 0);
                if (skill > 0 && elem > 0) _lastInteractiveUse[p.Pid] = (elem, skill, Environment.TickCount64);
            }
            else if (!p.ClientToServer && (p.Name == "ExchangeBidBuyerStartedEvent" || p.Name == "ExchangeBidSellerStartedEvent")
                     && _lastInteractiveUse.TryRemove(p.Pid, out var use) && Environment.TickCount64 - use.Tick < 4000
                     && CurrentMap(p.Pid) is long map && map > 0
                     && HdvHotelierBook.Learn(map, use.Element, use.Skill))
                TransportLog.Line($"[hdv] hôtelier appris : carte {map}, élément {use.Element}, skill {use.Skill} (enregistré pour le voyage automatique vers l'HDV)");
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException) { /* paquet illisible : ignoré */ }
    }
}
