using System;
using System.Collections.Generic;

namespace GivreClone.App.Services.Relay;

/// <summary>Un agent GivreRelay, tel que le gateway le décrit (list_exits / exit_update).</summary>
public sealed record RelayExit(string Id, string Label, bool Online, string? Country, string Scope, string? PublicIp,
                               long UsedBytes, long QuotaBytes, string? QuotaReset)
{
    public bool AnkamaOnly => Scope != "all";
    public bool QuotaReached => QuotaBytes > 0 && UsedBytes >= QuotaBytes;
}

public enum RelayChangeKind { Added, Removed, Renamed, Scope, Quota, QuotaReached, WentOffline, BackOnline, OfflineAtStart }

public sealed record RelayChange(RelayChangeKind Kind, string Id, RelayExit? Now, RelayExit? Before);

/// <summary>
/// Ce qui a changé entre deux états des agents, pour prévenir l'utilisateur. Seule une liste COMPLÈTE (<c>exits</c>) fait
/// foi pour les ajouts et les suppressions ; un <c>exit_update</c> ne porte que l'état d'un agent déjà connu.
/// Premier état de la session (<paramref name="known"/> null) : seuls les agents absents de la liste des proxys (créés
/// pendant que l'app était fermée) sont « ajoutés », et les agents déjà hors ligne sont signalés pour annoncer leur retour.
/// </summary>
public static class RelayChanges
{
    /// <param name="inProxyList">exit_id des agents présents dans la liste des proxys (persistée d'une session à l'autre).</param>
    public static List<RelayChange> Diff(IReadOnlyDictionary<string, RelayExit>? known, IReadOnlyDictionary<string, RelayExit> now,
                                         bool listed, IReadOnlySet<string> inProxyList)
    {
        var changes = new List<RelayChange>();
        if (listed)
            foreach (var id in inProxyList)
                if (!now.ContainsKey(id)) changes.Add(new RelayChange(RelayChangeKind.Removed, id, null, known?.GetValueOrDefault(id)));

        foreach (var e in now.Values)
        {
            if (known is null || !known.TryGetValue(e.Id, out var old))
            {
                if (listed && (known is not null || !inProxyList.Contains(e.Id))) changes.Add(new RelayChange(RelayChangeKind.Added, e.Id, e, null));
                if (!e.Online) changes.Add(new RelayChange(RelayChangeKind.OfflineAtStart, e.Id, e, null));
                continue;
            }
            if (old.Label != e.Label) changes.Add(new RelayChange(RelayChangeKind.Renamed, e.Id, e, old));
            if (old.AnkamaOnly != e.AnkamaOnly) changes.Add(new RelayChange(RelayChangeKind.Scope, e.Id, e, old));
            if (old.QuotaBytes != e.QuotaBytes) changes.Add(new RelayChange(RelayChangeKind.Quota, e.Id, e, old));
            if (!old.QuotaReached && e.QuotaReached) changes.Add(new RelayChange(RelayChangeKind.QuotaReached, e.Id, e, old));
            if (old.Online && !e.Online) changes.Add(new RelayChange(RelayChangeKind.WentOffline, e.Id, e, old));
            else if (!old.Online && e.Online) changes.Add(new RelayChange(RelayChangeKind.BackOnline, e.Id, e, old));
        }
        return changes;
    }
}
