using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    // Consultation uniquement : ne modifie ni les portes UI/Expédition ni le routage.
    // Les intérieurs peuvent sortir par MapAction (8), pas seulement par porte (32).
    internal IEnumerable<long> ResourceOriginExits(long mapId)
        => mapId is > 0 and <= int.MaxValue
            ? (_worldGraph?.Transitions((int)mapId) ?? Array.Empty<MapTransition>())
                .Where(t => (t.Type is 1 or 2 or 8 or 32) && t.Criterion is null)
                .Select(t => (long)t.Arrival).Where(id => id > 0 && id != mapId).Distinct().ToArray()
            : Array.Empty<long>();

    internal (int X, int Y, int? World)? ResourceOriginOf(long mapId)
        => Resources.ResourceMap.Origin(mapId, MapCoordOf, WorldMapOf, ResourceOriginExits);
}
