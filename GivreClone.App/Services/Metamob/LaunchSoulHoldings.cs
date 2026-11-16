using System.Collections.Generic;

namespace GivreClone.App.Services.Metamob;

public interface ISoulHoldingsSource
{
    SoulHoldingsSnapshot Read(uint pid);
}

/// <summary>Lit le sac (pierres équipées comprises) et le coffre déjà reçus. N'ouvre pas le coffre et n'envoie rien au jeu.</summary>
public sealed class LaunchSoulHoldings : ISoulHoldingsSource
{
    private readonly LaunchService _launch;

    public LaunchSoulHoldings(LaunchService launch) => _launch = launch;

    public SoulHoldingsSnapshot Read(uint pid)
    {
        var bagSeeded = _launch.Inventory.IsSeeded(pid);
        var chestSeeded = _launch.Bank.IsSeeded(pid);
        var bag = bagSeeded ? Stones(_launch.Inventory.Items(pid), pid) : (IReadOnlyList<SoulStoneObservation>)System.Array.Empty<SoulStoneObservation>();
        var chest = chestSeeded ? Stones(_launch.Bank.Items(pid), pid) : (IReadOnlyList<SoulStoneObservation>)System.Array.Empty<SoulStoneObservation>();
        return new SoulHoldingsSnapshot(bagSeeded, chestSeeded, bag, chest);
    }

    private List<SoulStoneObservation> Stones(IReadOnlyList<InventoryStore.Item> items, uint pid)
    {
        var list = new List<SoulStoneObservation>();
        foreach (var item in items)
        {
            if (item.Uid <= 0 || item.Qty <= 0 || !SoulStoneContents.IsSoulStone(item.Gid)) continue;
            list.Add(new SoulStoneObservation(item.Uid, item.Gid, item.Qty, _launch.ItemEffects(pid, item.Uid)));
        }
        return list;
    }
}
