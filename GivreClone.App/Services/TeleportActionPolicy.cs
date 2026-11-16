namespace GivreClone.App.Services;

/// <summary>
/// An advertised teleport destination is not an authenticated action contract.
/// Only the supported list types (zaap/zaapi/prism/haven bag) may be selected;
/// the anomaly tab is read-only until its request/ACK is captured and verified.
/// </summary>
public static class TeleportActionPolicy
{
    public static bool CanSend(int dialogType, int receivedListType, long dialogMap,
        long currentMap, long targetMap, bool listed)
        => dialogType is >= 0 and <= 3 &&
           dialogType == receivedListType &&
           dialogMap > 0 && currentMap == dialogMap &&
           targetMap > 0 && listed;
}
