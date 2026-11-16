using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using GivreLauncher.Core.Proxy;

namespace GivreClone.App.Services.Relay;

/// <summary>
/// Point d'entrée GivreRelay pour le reste de l'app : le client WebSocket (un seul, partagé par tous les bots), le pont
/// SOCKS5 local, et la synchro des partages dans la liste des proxys (une entrée « relay-&lt;exit_id&gt; » par partage).
/// Un partage assigné à un compte se résout en proxy SOCKS5 127.0.0.1:&lt;pont&gt; avec l'exit_id pour identifiant.
/// </summary>
public static class GivreRelay
{
    public static GivreRelayClient Client { get; } = new();
    private static readonly RelaySocksBridge Bridge = new(Client);
    private static int _wired;

    /// <summary>Liste des agents synchronisée dans la liste des proxys (thread UI) : l'onglet Proxys se rafraîchit ici.</summary>
    public static event Action? Changed;

    // Dernier état vu de chaque agent (null = pas encore de liste dans cette session) et début d'absence des agents hors ligne.
    private static Dictionary<string, RelayExit>? _known;
    private static readonly Dictionary<string, DateTime> _offlineSince = new(StringComparer.Ordinal);
    // Un agent qui se reconnecte (box, Wi-Fi) repasse en ligne en quelques secondes : hors ligne annoncé après ce délai seulement.
    private static readonly TimeSpan OfflineGrace = TimeSpan.FromSeconds(10);

    /// <summary>Session de licence ouverte : démarre le client (idempotent) et la synchro des agents vers la liste des proxys.</summary>
    public static void Start()
    {
        if (System.Threading.Interlocked.Exchange(ref _wired, 1) == 0)
        {
            Client.ExitsListed += () => Dispatcher.UIThread.Post(() => Reconcile(listed: true));
            Client.ExitsChanged += () => Dispatcher.UIThread.Post(() => Reconcile(listed: false));
            // Licence coupée (kick, pause, expiration) : le lien tombe tout de suite ; il revient avec la session suivante.
            LicenseService.PermissionsChanged += () => { if (!LicenseService.IsAuthorized) Client.OnSessionEnded(); };
        }
        Client.EnsureStarted();
    }

    /// <summary>Session de licence fermée (déconnexion, licence perdue) : coupe le lien ; la boucle attend la session suivante.</summary>
    public static void Stop()
    {
        Client.OnSessionEnded();
        Dispatcher.UIThread.Post(() => { _known = null; _offlineSince.Clear(); });   // session suivante : nouvel état de référence
    }

    /// <summary>
    /// Synchronise la liste des proxys avec les agents du relais et prévient de chaque changement : agent ajouté, supprimé,
    /// renommé, en ligne / hors ligne, portée ou quota modifiés, quota atteint. Liste complète (<paramref name="listed"/>) :
    /// seule à faire foi pour les ajouts et les suppressions ; un <c>exit_update</c> ne porte que l'état d'un agent connu.
    /// </summary>
    private static void Reconcile(bool listed)
    {
        if (Client.State != RelayLinkState.Connected) return;
        if (_known is null && !listed) return;   // état de référence = première liste complète de la session
        var cur = Client.Exits.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var inList = Stores.Proxies.Proxies.Where(p => p.FromRelay).Select(p => p.RelayExitId!).ToHashSet(StringComparer.Ordinal);

        foreach (var c in RelayChanges.Diff(_known, cur, listed, inList))
        {
            string label = c.Now?.Label ?? c.Before?.Label ?? "";
            switch (c.Kind)
            {
                case RelayChangeKind.Removed: RemoveDeleted(c.Id); break;
                case RelayChangeKind.Added: Notify(Loc.Fmt("L.Relay.N.Added", label), ok: true); break;
                case RelayChangeKind.Renamed: Notify(Loc.Fmt("L.Relay.N.Renamed", c.Before!.Label, label), ok: true); break;
                case RelayChangeKind.Scope:
                    Notify(Loc.Fmt("L.Relay.N.Scope", label, Loc.T(c.Now!.AnkamaOnly ? "L.Relay.ScopeAnkama" : "L.Relay.ScopeAll")), ok: true);
                    break;
                case RelayChangeKind.Quota:
                    Notify(Loc.Fmt("L.Relay.N.Quota", label, c.Now!.QuotaBytes > 0 ? Gb(c.Now.QuotaBytes) + " Go" : Loc.T("L.Relay.N.NoLimit")), ok: true);
                    break;
                case RelayChangeKind.QuotaReached: Notify(Loc.Fmt("L.Relay.N.QuotaReached", label), ok: false); break;
                case RelayChangeKind.WentOffline: OnWentOffline(c.Id); break;
                case RelayChangeKind.BackOnline: OnBackOnline(c.Id, label); break;
                case RelayChangeKind.OfflineAtStart: _offlineSince[c.Id] = DateTime.UtcNow - OfflineGrace; break;   // son retour sera annoncé
            }
        }
        foreach (var e in cur.Values)
            try { Stores.Proxies.UpsertRelay(e.Id, e.Label); } catch { /* store illisible : réessayé au prochain push */ }
        _known = cur;
        try { Changed?.Invoke(); } catch { }
    }

    // Hors ligne annoncé seulement s'il dure (reconnexion de la box de l'agent = silence).
    private static void OnWentOffline(string id)
    {
        var since = DateTime.UtcNow;
        _offlineSince[id] = since;
        DispatcherTimer.RunOnce(() =>
        {
            if (!_offlineSince.TryGetValue(id, out var s) || s != since) return;   // revenu entre-temps
            if (Client.GetExit(id) is { Online: false } e) Notify(Loc.Fmt("L.Relay.N.Offline", e.Label), ok: false);
        }, OfflineGrace);
    }

    private static void OnBackOnline(string id, string label)
    {
        bool announced = _offlineSince.Remove(id, out var since) && DateTime.UtcNow - since >= OfflineGrace;
        if (announced) Notify(Loc.Fmt("L.Relay.N.Online", label), ok: true);
    }

    private static void Notify(string message, bool ok)
    {
        GivreLauncher.Core.Transport.TransportLog.Line("[relais] " + message);
        Toasts.Show(message, success: ok);
    }

    private static string Gb(long b) => (b / 1024d / 1024d / 1024d).ToString("0.#");

    /// <summary>
    /// Agent supprimé sur le portail (absent de la liste complète de la licence) : retiré de la liste des proxys, avec un
    /// message. Les comptes qui l'utilisaient GARDENT son identifiant : leur lancement est refusé tant qu'un autre proxy
    /// n'est pas choisi (jamais de bascule silencieuse sur l'IP réelle) ; si l'agent revient (même identifiant), ils le
    /// retrouvent.
    /// </summary>
    private static void RemoveDeleted(string exitId)
    {
        if (Stores.Proxies.Get("relay-" + exitId) is not { } p) return;
        Stores.Proxies.Remove(p.Id);
        _offlineSince.Remove(exitId);
        var users = Stores.Accounts.Accounts.Where(a => a.ProxyId == p.Id)
            .Select(a => string.IsNullOrWhiteSpace(a.DisplayName) ? a.Login : a.DisplayName!).ToList();
        Notify(users.Count > 0 ? Loc.Fmt("L.Relay.RemovedAccounts", p.Name, string.Join(", ", users))
                               : Loc.Fmt("L.Relay.Removed", p.Name), ok: false);
    }

    /// <summary>Raison lisible pour laquelle ce partage ne peut pas servir maintenant, ou null s'il est utilisable.</summary>
    public static string? Unavailable(string exitId)
    {
        if (Client.State != RelayLinkState.Connected)
            return Client.LastError is { Length: > 0 } err ? err : Loc.T("L.Relay.R.RelayOffline");
        var ex = Client.GetExit(exitId);
        if (ex is null) return Loc.T("L.Relay.R.NotFound");
        if (!ex.Online) return Loc.T("L.Relay.R.Offline");
        if (ex.QuotaReached) return Loc.T("L.Relay.R.Quota");
        return null;
    }

    /// <summary>Proxy local vers ce partage, ou null s'il est indisponible (lancement refusé, jamais de repli sur l'IP réelle).</summary>
    public static ProxySettings? SettingsFor(string exitId)
    {
        if (Unavailable(exitId) is not null) return null;
        int port = Bridge.EnsureStarted();
        return new ProxySettings(ProxyKind.Socks5, "127.0.0.1", port, exitId, Bridge.Secret);
    }

    /// <summary>Partage derrière ces réglages de proxy (pont local du relais), sinon null.</summary>
    public static RelayExit? ExitFor(ProxySettings? ps)
        => ps is { Kind: ProxyKind.Socks5, Host: "127.0.0.1", Username: { Length: > 0 } id } && ps.Port == Bridge.Port && Bridge.Port > 0
            ? Client.GetExit(id)
            : null;

    /// <summary>« 1,2 / 10 Go » (ou « 1,2 Go » sans quota).</summary>
    public static string Usage(RelayExit e)
    {
        static string Go(long b) => (b / 1024d / 1024d / 1024d).ToString(b < 10L * 1024 * 1024 * 1024 ? "0.0#" : "0.#");
        return e.QuotaBytes > 0 ? $"{Go(e.UsedBytes)} / {Go(e.QuotaBytes)} Go" : $"{Go(e.UsedBytes)} Go";
    }
}
