using System.Text.Json;
using GivreClone.Engine.Secrets;

namespace GivreClone.Engine.Proxies;

/// <summary>Store persiste des proxys (SOCKS5/HTTP + proxys d'adaptateurs). Mot de passe chiffre DPAPI.</summary>
public sealed class ProxyStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly List<ProxyEntry> _proxies = new();

    public ProxyStore(string storePath) { _path = storePath; Load(); }

    public IReadOnlyList<ProxyEntry> Proxies { get { lock (_gate) return _proxies.ToArray(); } }

    public ProxyEntry? Get(string id)
    {
        lock (_gate) return _proxies.FirstOrDefault(p => p.Id == id);
    }

    public ProxyEntry AddExternal(string name, ProxyProtocol protocol, string host, int port, string? user = null, string? password = null)
    {
        var p = new ProxyEntry { Name = name, Protocol = protocol, Host = host, Port = port, Username = user };
        if (!string.IsNullOrEmpty(password)) p.PasswordProtected = Dpapi.Protect(password);
        lock (_gate) _proxies.Add(p);
        Save();
        return p;
    }

    public ProxyEntry AddFromAdapter(string name, string adapterId)
    {
        var p = new ProxyEntry { Name = name, Protocol = ProxyProtocol.Http, AdapterId = adapterId };
        lock (_gate) _proxies.Add(p);
        Save();
        return p;
    }

    /// <summary>Partage GivreRelay vu dans la liste du serveur : cree son entree (Id stable « relay-&lt;exit_id&gt; », donc
    /// les comptes qui l'utilisent la retrouvent d'une session a l'autre) ou met son nom a jour. Jamais de suppression ici :
    /// un partage disparu reste assigne (lancement refuse) tant que l'utilisateur ne le retire pas.</summary>
    public ProxyEntry UpsertRelay(string exitId, string label)
    {
        string id = "relay-" + exitId;
        bool changed = false;
        ProxyEntry? p;
        lock (_gate)
        {
            p = _proxies.FirstOrDefault(x => x.Id == id);
            if (p is null)
            {
                p = new ProxyEntry { Id = id, Name = label, Protocol = ProxyProtocol.Socks5, RelayExitId = exitId };
                _proxies.Add(p);
                changed = true;
            }
            else if (p.Name != label) { p.Name = label; changed = true; }
        }
        if (changed) Save();
        return p;
    }

    public void Update(ProxyEntry entry, string? newPassword = null)
    {
        lock (_gate)
        {
            var i = _proxies.FindIndex(p => p.Id == entry.Id);
            if (i < 0) { _proxies.Add(entry); }
            else _proxies[i] = entry;
            if (newPassword is not null) entry.PasswordProtected = string.IsNullOrEmpty(newPassword) ? "" : Dpapi.Protect(newPassword);
        }
        Save();
    }

    public void Remove(string id)
    {
        lock (_gate) _proxies.RemoveAll(p => p.Id == id);
        Save();
    }

    /// <summary>Mot de passe en clair (DPAPI decrypt), ou null. A n'appeler qu'au moment de se connecter.</summary>
    public string? GetPassword(string id)
    {
        ProxyEntry? p;
        lock (_gate) p = _proxies.FirstOrDefault(x => x.Id == id);
        return p is null ? null : Dpapi.Unprotect(p.PasswordProtected);
    }

    private void Load()
    {
        lock (_gate)
        {
            _proxies.Clear();
            if (!File.Exists(_path)) return;
            try
            {
                var list = JsonSerializer.Deserialize(File.ReadAllText(_path), EngineJsonContext.Default.ProxyList);
                if (list is not null) _proxies.AddRange(list);
            }
            catch { /* store corrompu -> vide */ }
        }
    }

    private void Save()
    {
        List<ProxyEntry> snapshot;
        lock (_gate) snapshot = _proxies.ToList();
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, EngineJsonContext.Default.ProxyList));
        File.Move(tmp, _path, overwrite: true);
    }
}
