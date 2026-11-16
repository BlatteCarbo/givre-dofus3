using System.Linq;
using GivreClone.Engine.Proxies;
using GivreLauncher.Core.Proxy;

namespace GivreClone.App.Services;

/// <summary>Convertit un ProxyEntry (persiste) en ProxySettings live (SOCKS5/HTTP externe, ou proxy d'adaptateur).</summary>
public static class ProxyResolver
{
    /// <summary>ProxySettings pret a l'emploi, ou null si l'adaptateur reference n'existe plus (-&gt; lancement refuse).</summary>
    public static ProxySettings? Resolve(ProxyEntry entry, ProxyStore store)
    {
        // Partage GivreRelay : pont SOCKS5 local, seulement si le relais est connecté et le partage en ligne, sous quota.
        // Le droit d'usage est vérifié par le serveur (jeton de session, licence : 4001/4002), pas par le droit « proxy ».
        if (entry.FromRelay) return Relay.GivreRelay.SettingsFor(entry.RelayExitId!);

        if (entry.FromAdapter)
        {
            // Droit serveur "adapter" REQUIS pour router par une interface (= la feature de l'onglet Adapters).
            // Sans lui : on ne démarre PAS le proxy d'interface -> null -> lancement refusé (pas de proxy = pas de
            // fuite d'IP réelle). Ferme le contournement "utiliser l'adaptateur via le lancement d'un compte".
            if (!LicenseService.HasPermission(LicenseService.Perm.Adapter)) return null;
            var nic = NetworkInterfaces.List().FirstOrDefault(n => n.Id == entry.AdapterId);
            if (nic is null) return null;                       // interface disparue -> pas de proxy
            try
            {
                int port = InterfaceProxyManager.Ensure(nic);   // demarre le proxy local si besoin (port fixe ou auto)
                // AUDIT M6 : le proxy d'interface écoute désormais en LOOPBACK (voir InterfaceProxy.Start) ; le client
                // (WebAuth local) se connecte donc à 127.0.0.1, et l'egress vers Internet sort quand même par l'IP de
                // l'interface choisie (outbound.Bind côté proxy). On ne remet plus l'IP LAN au client -> plus de proxy ouvert.
                // ⚠ On transmet AUSSI l'IP d'interface (EgressBindIp) : le tunnel de JEU la bind DIRECTEMENT (bind-direct)
                // pour sortir par l'interface sans le saut proxy loopback. Sans elle, le bind-direct bindait sur 127.0.0.1
                // (Host) -> « réseau impossible à atteindre » (régression introduite par M6).
                return new ProxySettings(ProxyKind.HttpConnect, "127.0.0.1", port, EgressBindIp: nic.Ip.ToString());
            }
            catch { return null; }                              // port fixe deja pris, etc. -> pas de fuite
        }

        if (string.IsNullOrWhiteSpace(entry.Host) || entry.Port <= 0) return null;
        // Droit serveur "proxy" REQUIS pour router par un proxy externe SOCKS5/HTTP (= la feature de l'onglet Proxys).
        // Sans lui : pas de proxy -> lancement refusé (le message précis est affiché en amont dans AccountsViewModel).
        if (!LicenseService.HasPermission(LicenseService.Perm.Proxy)) return null;
        var kind = entry.Protocol == ProxyProtocol.Socks5 ? ProxyKind.Socks5 : ProxyKind.HttpConnect;
        var pass = store.GetPassword(entry.Id);
        return new ProxySettings(kind, entry.Host, entry.Port, entry.Username, pass);
    }
}
