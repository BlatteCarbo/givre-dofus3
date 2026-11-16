namespace GivreClone.Engine.Accounts;

/// <summary>D'ou vient l'authentification du compte : keydata Zaap local, ou jeton navigateur (WebView2).</summary>
public enum AuthSource { KeyData, Browser }

/// <summary>Mode de connexion du compte : client Dofus complet relayé (MITM) ou connexion directe sans Dofus.exe (Socket).</summary>
public enum ConnectionMode { Mitm = 0, Socket = 1 }

/// <summary>
/// Un compte pilotable. Le mot de passe n'est JAMAIS stocke en clair : seul le blob DPAPI (base64) est
/// persiste (voir AccountManager). ProxyEndpoint / NetworkInterface servent a l'isolation d'IP par compte
/// (compose avec GivreAdapter). LaunchArgs pour la composition avec GivreLauncher.
/// </summary>
public sealed class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Login { get; set; } = "";
    public string? DisplayName { get; set; }

    /// <summary>Blob DPAPI (base64) du mot de passe. Vide = aucun secret stocke.</summary>
    public string PasswordProtected { get; set; } = "";

    /// <summary>Source d'auth : KeyData (Zaap local, aucun secret a stocker) ou Browser (jetons WebView2).</summary>
    public AuthSource Source { get; set; } = AuthSource.KeyData;

    /// <summary>Blob DPAPI (base64) des jetons navigateur "apiKey\nrefreshToken" (comptes Browser). Vide sinon.</summary>
    public string TokensProtected { get; set; } = "";

    /// <summary>Blob DPAPI (base64) du certificat Shield "certId\nencodedCertificate" (comptes Browser sécurisés). Vide sinon.
    /// Ankama ne renvoie l'encodedCertificate qu'UNE fois (au mint) : sans persistance, tout re-lancement d'un compte
    /// devenu SECURED est refusé (CreateToken « Invalid security parameters » puis ValidateCode « ALREADYSECURED »).</summary>
    public string CertificateProtected { get; set; } = "";

    /// <summary>Ou arriver au lancement : 0 = choix serveur, 1 = choix perso, 2 = direct en jeu.</summary>
    public int AutoConnectType { get; set; }

    /// <summary>Mode MÉMORISÉ (celui du dernier lancement manuel) : utilisé par tous les lancements automatiques (planning,
    /// Lancer tout, équipe, relance). Défaut MITM -> les accounts.json existants restent valides.</summary>
    public ConnectionMode Mode { get; set; }

    /// <summary>Proxy assigne (Id d'un ProxyEntry de l'onglet Proxys), ou null = pas de proxy (lancement refuse).</summary>
    public string? ProxyId { get; set; }

    /// <summary>Proxy du SERVEUR DE JEU (Id d'un ProxyEntry), ou null = le même que <see cref="ProxyId"/>. Le proxy du compte
    /// sert au lancement, à l'authentification Ankama et au serveur de connexion ; celui-ci prend le relais à l'ouverture de
    /// la connexion au serveur de jeu (ex. proxy ROTATIF pour la connexion, puis proxy STATIQUE stable pour la session).</summary>
    public string? GameProxyId { get; set; }

    /// <summary>Démarre automatiquement le script du personnage (le dernier chargé dans l'éditeur Lua) dès qu'il est en jeu.</summary>
    public bool AutoStartScript { get; set; }

    /// <summary>Ancien : interface reseau liee (migre vers ProxyId/onglet Proxys). Conserve pour compat.</summary>
    public string? NetworkInterface { get; set; }

    /// <summary>Graine d'identité machine PAR COMPTE (hex aléatoire, 16 o) mélangée au HWID pour forger un
    /// device_identifier UNIQUE et STABLE par compte en mode Socket. Sans elle, le device_identifier serait
    /// identique pour tous les comptes de la machine et les serveurs mono-compte refuseraient structurellement le
    /// 2e compte (même « machine »). Générée à la 1re utilisation (AccountManager.GetOrCreateDeviceSeed) puis
    /// persistée ici. Vide dans les accounts.json existants -> repli déterministe sur Id le temps qu'elle soit
    /// générée+sauvée. N'affecte PAS le HWID de licence (machine-constant, cf Hwid.Compute).</summary>
    public string DeviceSeed { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>Bannissement Ankama constaté (refus d'identification BANNED en Socket) : instant UTC du constat, null = pas banni.
    /// Tant qu'il est actif, aucun lancement AUTOMATIQUE (planning, Lancer tout, équipe, relance) ; un lancement manuel reste possible.</summary>
    public DateTime? BannedAtUtc { get; set; }

    /// <summary>Fin du bannissement telle que renvoyée par le serveur (ban_end_date, texte brut) ; vide = sans date de fin connue.</summary>
    public string BanEnd { get; set; } = "";
}
