namespace GivreClone.Engine.Proxies;

public enum ProxyProtocol { Socks5, Http }

/// <summary>
/// Un proxy assignable a un compte : soit EXTERNE (SOCKS5/HTTP host:port, ex partage 4G du telephone,
/// proxy achete), soit issu d'un ADAPTATEUR local (AdapterId -&gt; l'onglet Adapters expose le proxy local d'une
/// interface). Le mot de passe est chiffre au repos (DPAPI). Host/Port sont ignores quand AdapterId est defini
/// (resolus a la volee vers l'endpoint du proxy d'interface).
/// </summary>
public sealed class ProxyEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public ProxyProtocol Protocol { get; set; } = ProxyProtocol.Socks5;

    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string? Username { get; set; }

    /// <summary>Blob DPAPI (base64) du mot de passe proxy. Vide = pas d'auth.</summary>
    public string PasswordProtected { get; set; } = "";

    /// <summary>Si defini : ce proxy vient d'un ADAPTATEUR (interface reseau), pas d'un host externe.</summary>
    public string? AdapterId { get; set; }

    public bool FromAdapter => !string.IsNullOrEmpty(AdapterId);

    /// <summary>Si defini : ce proxy est un PARTAGE GivreRelay (l'IP d'un ami, exit_id du gateway). Host/Port ignores :
    /// le trafic passe par le pont SOCKS5 local du relais. Entree creee/renommee par la liste du serveur.</summary>
    public string? RelayExitId { get; set; }

    public bool FromRelay => !string.IsNullOrEmpty(RelayExitId);

    /// <summary>Sentinelle "Sans proxy" (Id vide) : choix explicite de ne PAS assigner de proxy (IP reelle, aucune
    /// isolation). Permet de le selectionner dans la liste au lieu de forcer un proxy.</summary>
    public bool IsNone => Id.Length == 0;
    public static ProxyEntry None { get; } = new() { Id = "", Name = "Sans proxy" };
}
