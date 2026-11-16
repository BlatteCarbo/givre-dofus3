using System.Collections.Concurrent;
using System.Net;

namespace GivreClone.App.Services;

/// <summary>
/// Source unique des proxys d'interface (isolation d'IP) partagee entre l'onglet Proxys et l'onglet Comptes :
/// UN proxy HTTP par interface (bind sur l'IP de l'interface). L'onglet Proxys les allume/eteint ; le lancement
/// d'un compte s'assure que celui de son interface tourne. Evite de demarrer deux proxys pour la meme interface.
/// </summary>
public static class InterfaceProxyManager
{
    private static readonly ConcurrentDictionary<string, Entry> _running = new();
    private sealed record Entry(InterfaceProxy Proxy, IPAddress Ip, int Port);

    /// <summary>
    /// Demarre le proxy de l'interface s'il ne tourne pas ; retourne son port local. Idempotent.
    /// Honore le port fixe eventuel (AdapterSettingsStore) sinon port automatique -> meme regle pour
    /// l'onglet Adaptateurs et pour le lancement d'un compte via cet adaptateur.
    /// </summary>
    public static int Ensure(NetAdapter nic)
    {
        if (_running.TryGetValue(nic.Id, out var e)) return e.Port;
        var s = AdapterSettingsStore.Get(nic.Id);
        int desired = s.FixedPort && s.Port is > 0 and <= 65535 ? s.Port : 0;
        var proxy = new InterfaceProxy(nic.Ip, () => nic.Name);
        int port = proxy.Start(desired);
        _running[nic.Id] = new Entry(proxy, nic.Ip, port);
        return port;
    }

    public static void Stop(string interfaceId)
    {
        if (_running.TryRemove(interfaceId, out var e)) e.Proxy.Stop();
    }

    public static bool IsRunning(string interfaceId) => _running.ContainsKey(interfaceId);
    public static int? Port(string interfaceId) => _running.TryGetValue(interfaceId, out var e) ? e.Port : null;
    public static IPAddress? Ip(string interfaceId) => _running.TryGetValue(interfaceId, out var e) ? e.Ip : null;
}
