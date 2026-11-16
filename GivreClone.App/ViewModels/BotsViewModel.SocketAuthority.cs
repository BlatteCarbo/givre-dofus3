using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

public partial class BotsViewModel
{
    /// <summary>
    /// The operator explicitly requests this read-only projection. Counts are
    /// attributable to a current *local* source-validated Socket connection,
    /// not a confirmed cast/movement, server ACK or authenticated game actor.
    /// No packet is sent and no authority state is changed on refresh.
    /// </summary>
    public string SocketAuthorityStatusText
    {
        get
        {
            if (SelectedBot is not { Pid: > 0 } bot || bot.Row.IsDisconnected)
                return "Transport : personnage non connecté ; aucune autorité de commande certifiée.";
            var source = Stores.Launch.SocketSourceAuthorityOf((uint)bot.Pid);
            if (source is not { } snapshot)
            {
                var mitm = Stores.Launch.LocalMitmSourceAuthorityOf((uint)bot.Pid);
                if (mitm is not { } link)
                    return "Transport : aucune observation source-liée actuelle disponible. " +
                           "Les flux legacy/externes ne permettent pas de corrélation demande → ACK.";
                return $"MITM local (époque {link.RunEpoch}) : " +
                       $"{link.ServerFramesObserved} fragment(s) serveur " +
                       $"({link.ServerWireBytes} octets) sur le tunnel source exact. " +
                       "Emission moteur et ACK individuel non attribuables par ce canal.";
            }
            return $"Socket locale (époque {snapshot.RunEpoch}) : " +
                   $"{snapshot.EngineWritesCompleted} écriture(s) moteur après Flush, " +
                   $"{snapshot.ServerFramesObserved} fragment(s) reçus " +
                   $"({snapshot.ServerWireBytes} octets). " +
                   "ACK individuel/effet : NON ATTRIBUABLE. Commandes non autorisées par ce diagnostic.";
        }
    }

    [RelayCommand]
    private void InspectSocketAuthority()
        => OnPropertyChanged(nameof(SocketAuthorityStatusText));
}
