using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GivreClone.Engine.Accounts;

/// <summary>
/// Store de comptes chiffre au repos. Les mots de passe sont proteges par DPAPI (CurrentUser) : le fichier
/// json ne contient qu'un blob opaque, illisible sur une autre machine/session. La couche d'identite
/// multi-compte absente de toutes les sources auditees.
/// </summary>
public sealed class AccountManager
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GivreClone.Accounts.v1");

    private readonly string _path;
    private readonly object _gate = new();
    private readonly object _ioGate = new();   // sérialise les écritures disque : onCertMinted persiste depuis un thread pool (Lancer tout) -> évite la course sur le fichier .tmp
    private readonly List<Account> _accounts = new();

    public AccountManager(string storePath)
    {
        _path = storePath;
        Load();
    }

    public IReadOnlyList<Account> Accounts { get { lock (_gate) return _accounts.ToArray(); } }

    public Account Add(string login, string? password = null, string? displayName = null, string? networkInterface = null)
    {
        var acc = new Account { Login = login, DisplayName = displayName, NetworkInterface = networkInterface };
        if (!string.IsNullOrEmpty(password)) acc.PasswordProtected = Protect(password);
        lock (_gate) _accounts.Add(acc);
        Save();
        return acc;
    }

    /// <summary>Persiste une modification faite sur un compte deja dans le store (ex : interface, proxy).</summary>
    public void Persist() => Save();

    /// <summary>Cree/ met a jour un compte NAVIGATEUR (jetons apiKey/refresh chiffres au repos).</summary>
    public Account AddBrowser(string login, string apiKey, string refreshToken, string? networkInterface = null)
    {
        var acc = new Account { Login = login, Source = AuthSource.Browser, NetworkInterface = networkInterface };
        acc.TokensProtected = Protect(apiKey + "\n" + refreshToken);
        lock (_gate) _accounts.Add(acc);
        Save();
        return acc;
    }

    public void SetBrowserTokens(string id, string apiKey, string refreshToken)
    {
        lock (_gate)
        {
            var a = _accounts.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException(id);
            a.Source = AuthSource.Browser;
            a.TokensProtected = Protect(apiKey + "\n" + refreshToken);
        }
        Save();
    }

    /// <summary>Dechiffre les jetons navigateur, ou (null,null) si absent. A n'appeler qu'au moment de s'authentifier.</summary>
    public (string? ApiKey, string? RefreshToken) GetBrowserTokens(string id)
    {
        Account? a;
        lock (_gate) a = _accounts.FirstOrDefault(x => x.Id == id);
        if (a is null || string.IsNullOrEmpty(a.TokensProtected)) return (null, null);
        var plain = Unprotect(a.TokensProtected);
        if (plain is null) return (null, null);
        var parts = plain.Split('\n');
        return (parts.Length > 0 ? parts[0] : null, parts.Length > 1 ? parts[1] : null);
    }

    /// <summary>Persiste le certificat Shield fraîchement forgé (chiffré DPAPI) pour le réutiliser aux prochains lancements
    /// et éviter un re-mint refusé (ALREADYSECURED). No-op si le compte a disparu.</summary>
    public void SetCertificate(string id, string certId, string encodedCertificate)
    {
        lock (_gate)
        {
            var a = _accounts.FirstOrDefault(x => x.Id == id);
            if (a is null) return;
            a.CertificateProtected = Protect(certId + "\n" + encodedCertificate);
        }
        Save();
    }

    /// <summary>Déchiffre le certificat Shield persisté (certId, encodedCertificate), ou (null,null) si absent.
    /// À n'appeler qu'au moment de s'authentifier.</summary>
    public (string? CertId, string? Encoded) GetCertificate(string id)
    {
        Account? a;
        lock (_gate) a = _accounts.FirstOrDefault(x => x.Id == id);
        if (a is null || string.IsNullOrEmpty(a.CertificateProtected)) return (null, null);
        var plain = Unprotect(a.CertificateProtected);
        if (plain is null) return (null, null);
        var parts = plain.Split('\n');
        return (parts.Length > 0 ? parts[0] : null, parts.Length > 1 ? parts[1] : null);
    }

    /// <summary>Oublie le certificat Shield persisté (ex : bouclier réinitialisé côté Ankama) -> re-mint au prochain lancement.</summary>
    public void ClearCertificate(string id)
    {
        lock (_gate)
        {
            var a = _accounts.FirstOrDefault(x => x.Id == id);
            if (a is null || string.IsNullOrEmpty(a.CertificateProtected)) return;
            a.CertificateProtected = "";
        }
        Save();
    }

    /// <summary>Graine d'identité machine PAR COMPTE du mode Socket : forge un device_identifier UNIQUE et STABLE
    /// par compte (sinon les serveurs mono-compte refusent le 2e compte, même « machine »). La génère (16 o
    /// aléatoires en hex, AOT-safe) et la PERSISTE à la 1re demande si absente -> compat avec les accounts.json
    /// existants (champ vide au chargement, seedé+sauvé au 1er lancement Socket, stable ensuite). Repli
    /// DÉTERMINISTE dérivé de l'Id si le compte a disparu du store : jamais identique entre comptes, même avant
    /// la 1re sauvegarde. N'affecte PAS le HWID de licence (Hwid.Compute reste machine-constant).</summary>
    public string GetOrCreateDeviceSeed(string id)
    {
        string seed;
        bool created = false;
        lock (_gate)
        {
            var a = _accounts.FirstOrDefault(x => x.Id == id);
            if (a is null) return FallbackDeviceSeed(id);
            if (string.IsNullOrEmpty(a.DeviceSeed))
            {
                a.DeviceSeed = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                created = true;
            }
            seed = a.DeviceSeed;
        }
        if (created) Save();   // hors _gate : Save() reprend _gate pour son snapshot
        return seed;
    }

    // Repli stable quand le compte n'est plus dans le store : dérivé de l'Id (jamais du HWID) -> distinct par compte.
    private static string FallbackDeviceSeed(string id) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("GivreClone.DeviceSeed.v1|" + id)));

    public void Remove(string id)
    {
        lock (_gate) _accounts.RemoveAll(a => a.Id == id);
        Save();
    }

    public void SetPassword(string id, string password)
    {
        lock (_gate)
        {
            var a = _accounts.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException(id);
            a.PasswordProtected = Protect(password);
        }
        Save();
    }

    /// <summary>Dechiffre le mot de passe (DPAPI), ou null si absent. A n'appeler qu'au moment de s'authentifier.</summary>
    public string? GetPassword(string id)
    {
        Account? a;
        lock (_gate) a = _accounts.FirstOrDefault(x => x.Id == id);
        if (a is null || string.IsNullOrEmpty(a.PasswordProtected)) return null;
        return Unprotect(a.PasswordProtected);
    }

    private void Load()
    {
        lock (_gate)
        {
            _accounts.Clear();
            if (!File.Exists(_path)) return;
            try
            {
                var list = JsonSerializer.Deserialize(File.ReadAllText(_path), EngineJsonContext.Default.AccountList);
                if (list is not null) _accounts.AddRange(list);
            }
            catch { /* store corrompu -> on repart vide plutot que de crasher */ }
        }
    }

    private void Save()
    {
        List<Account> snapshot;
        lock (_gate) snapshot = _accounts.ToList();
        var json = JsonSerializer.Serialize(snapshot, EngineJsonContext.Default.AccountList);
        // I/O sérialisée hors _gate (n'y bloque pas les lectures de jetons/cert) : deux Save() concurrents partageraient
        // le même chemin .tmp -> corruption/File.Move qui lève. onCertMinted écrit depuis le pool -> ce verrou est requis.
        lock (_ioGate)
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
    }

    private static string Protect(string plain)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Le chiffrement DPAPI des comptes requiert Windows.");
        var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(blob);
    }

    private static string? Unprotect(string protectedBase64)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var blob = Convert.FromBase64String(protectedBase64);
            var plain = ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch { return null; }
    }
}
