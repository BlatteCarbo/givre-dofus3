namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    /// <summary>
    /// A local decoder lifetime check for bank/craft/HDV activity. Returning
    /// zero after Forget(pid) invalidates in-flight activity closures on
    /// disconnect/reconnect; this number is emphatically NOT proof of remote
    /// TCP identity, message acknowledgement, or a completed game action.
    /// </summary>
    public long CurrentLocalProductionDecoderEpoch(uint pid)
        => _decoder?.CurrentLocalEpoch(pid) ?? 0;
}
