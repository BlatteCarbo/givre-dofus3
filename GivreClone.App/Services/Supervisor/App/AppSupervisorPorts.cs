using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using GivreClone.App.ViewModels;
using GivreClone.Engine.Accounts;
using GivreClone.Engine.Proxies;

namespace GivreClone.App.Services.Supervisor;

/// <summary>Droit « Multi » : suit la licence de l'application, sans rien contourner.</summary>
internal sealed class AppPermissions : ISupervisorPermissions
{
    public bool HasMulti => LicenseService.HasPermission(LicenseService.Perm.Multi);
}

/// <summary>
/// Annuaire des bots : instantané de TOUS les comptes enregistrés, rafraîchi sur le thread UI (<see cref="Refresh"/>, ~1 s) puis lu
/// de n'importe quel thread. Aucun identifiant de connexion n'y figure (alias, perso, pid seulement).
/// Non renseignés dans ce lot : team / leader (équipes non portées) et lastStop.
/// </summary>
internal sealed class AppBotDirectory : IBotDirectory
{
    private readonly AccountsViewModel _acc;
    private readonly BotWorldStore _world;
    private readonly LaunchService _launch;
    private volatile IReadOnlyList<BotSnapshot> _snap = Array.Empty<BotSnapshot>();
    private readonly Dictionary<string, (string Status, long Tick)> _since = new();
    private volatile IReadOnlyDictionary<string, BotSnapshot> _byId = new Dictionary<string, BotSnapshot>();

    /// <summary>Instantané d'un compte par son identifiant interne (jamais exposé aux scripts).</summary>
    public BotSnapshot? ByAccountId(string id) => _byId.TryGetValue(id, out var b) ? b : null;

    public AppBotDirectory(AccountsViewModel acc) : this(acc, Stores.World, Stores.Launch) { }

    internal AppBotDirectory(AccountsViewModel acc, BotWorldStore world, LaunchService launch)
        => (_acc, _world, _launch) = (acc, world, launch);

    public IReadOnlyList<BotSnapshot> Snapshot() => _snap;

    /// <summary>Thread UI.</summary>
    public void Refresh()
    {
        var list = new List<BotSnapshot>();
        var byId = new Dictionary<string, BotSnapshot>();
        int i = 0;
        foreach (var row in _acc.Accounts)
        {
            int pid = row.Pid ?? 0;
            bool hint = row.ReconnectPending || row.ServerKickedOffline || row.SocketBackoff;
            string status = BotStatusMap.From(row.Running, hint, row.Activity);
            string alias = BotStatusMap.Alias(row.Label, row.CharacterName, row.LastCharacter, i++);

            long now = Environment.TickCount64;
            if (!_since.TryGetValue(row.Model.Id, out var s) || s.Status != status) _since[row.Model.Id] = s = (status, now);

            var world = pid > 0 ? _world.Get((uint)pid) : null;
            var life = pid > 0 ? _launch.SelfLife((uint)pid) : null;
            var script = pid > 0 ? BotScripts.Get(pid) : null;
            var snapRow = new BotSnapshot(
                Account: alias, Name: row.CharacterName ?? row.LastCharacter ?? "", Pid: pid,
                Mode: row.ModeIsSocket ? "socket" : "mitm", Status: status,
                MapId: world?.MapId ?? row.MapId ?? 0,
                Coords: world is not null ? (world.MapX is int x && world.MapY is int y ? $"{x}, {y}" : "") : row.MapCoords ?? "",
                Level: row.Level ?? world?.Level ?? 0, Kamas: pid > 0 ? _launch.Kamas((uint)pid) : -1,
                Pods: world?.Pods.Cur ?? 0, PodsMax: world?.Pods.Max ?? 0, Life: life?.Cur ?? 0, MaxLife: life?.Max ?? 0,
                InFight: status == BotStatus.Fight,
                Script: script?.Name ?? "", ScriptRunning: script?.Running ?? false,
                Disconnect: status == BotStatus.Disconnected ? (row.Status ?? "") : "",
                SinceSec: (int)Math.Max(0, (now - s.Tick) / 1000));
            list.Add(snapRow);
            byId[row.Model.Id] = snapRow;
        }
        _snap = list;
        _byId = byId;
    }
}

/// <summary>Ordres exécutés sur le thread UI avec les gardes de l'application ; le résultat revient au script.</summary>
internal sealed class AppBotOrders : IBotOrders
{
    private readonly AccountsViewModel _acc;
    private readonly BotsViewModel _bots;
    private readonly IBotDirectory _dir;

    public AppBotOrders(AccountsViewModel acc, BotsViewModel bots, IBotDirectory dir) => (_acc, _bots, _dir) = (acc, bots, dir);

    private Task<T> Ui<T>(Func<Task<T>> f) => Dispatcher.UIThread.InvokeAsync(f);
    private Task<T> Ui<T>(Func<T> f) => Dispatcher.UIThread.InvokeAsync(f).GetTask();

    /// <summary>Compte désigné par alias, perso ou pid (même règle que bots:get). Thread UI.</summary>
    private AccountRow? RowOf(string key)
    {
        // Id de compte exact (list_accounts) : seul moyen sûr de viser un compte quand deux comptes portent le même nom affiché.
        if (_acc.Accounts.FirstOrDefault(r => string.Equals(r.Model.Id, key, StringComparison.OrdinalIgnoreCase)) is { } byId) return byId;
        var snap = BotMatcher.Find(_dir.Snapshot(), key);
        if (snap is null) return null;
        if (snap.Pid > 0 && _acc.Accounts.FirstOrDefault(r => r.Pid == snap.Pid) is { } byPid) return byPid;
        return _acc.Accounts.FirstOrDefault(r => BotMatcher.SameAccount(snap,
            BotStatusMap.Alias(r.Label, r.CharacterName, r.LastCharacter, 0), r.CharacterName ?? r.LastCharacter));
    }

    private static readonly OrderResult Unknown = OrderResult.No("bot inconnu");
    private static readonly OrderResult Protected = OrderResult.No("compte protégé : interdit de connexion et de pilotage automatiques (AppConfig.McpBlockedLogins)");
    private static bool IsProtected(AccountRow r) => GivreClone.App.Services.Mcp.McpGuards.IsBlocked(r.Login, AppConfig.Current.McpBlockedLogins);

    public Task<OrderResult> ConnectAsync(string bot, string? character, string? server, CancellationToken ct)
        => Ui(async () => RowOf(bot) is { } r ? (IsProtected(r) ? Protected : await _acc.OrderConnectAsync(r, character, server)) : Unknown);

    public Task<OrderResult> DisconnectAsync(string bot, CancellationToken ct)
        => Ui(() => RowOf(bot) is { } r ? _acc.OrderDisconnect(r) : Unknown);

    public Task<OrderResult> ReconnectAsync(string bot, CancellationToken ct)
        => Ui(async () => RowOf(bot) is { } r ? (IsProtected(r) ? Protected : await _acc.OrderReconnectAsync(r)) : Unknown);

    public Task<OrderResult> SwitchCharacterAsync(string bot, string? character, string? server, bool restartScript, CancellationToken ct)
        => Task.FromResult(OrderResult.No("changement de perso ou de serveur non câblé dans le clone"));

    public Task<OrderResult> StartScriptAsync(string bot, string scriptOrCode, CancellationToken ct)
        => Ui(() => RowOf(bot) is { Pid: > 0 } r ? (IsProtected(r) ? Protected : _bots.OrderStartScript(r.Pid!.Value, scriptOrCode)) : OrderResult.No("bot hors ligne"));

    public Task<OrderResult> StopScriptAsync(string bot, CancellationToken ct)
        => Ui(() => RowOf(bot) is { Pid: > 0 } r ? _bots.OrderStopScript(r.Pid!.Value) : OrderResult.No("bot hors ligne"));

    public Task<OrderResult> RunAsync(string bot, string code, CancellationToken ct)
        => Ui(() => RowOf(bot) is { Pid: > 0 } r ? (IsProtected(r) ? Protected : _bots.OrderRun(r.Pid!.Value, code)) : OrderResult.No("bot hors ligne"));

    public Task<int> StopAllScriptsAsync(CancellationToken ct) => Ui(() => _bots.OrderStopAllScripts());
    public Task<int> DisconnectAllAsync(CancellationToken ct) => Ui(() => _acc.OrderDisconnectAll());
}

/// <summary>Comptes : liste, réglages et ajout par la fenêtre de connexion Ankama. Création de personnage : non câblée (voir CreateCharacter).</summary>
internal sealed class AppAccountsPort : IAccountsPort
{
    private readonly AccountsViewModel _acc;
    private readonly AppBotDirectory _dir;
    private readonly IProxiesPort _proxies;
    /// <summary>Appelé à l'ouverture de la fenêtre de connexion : loginWaiting { reason = "browser" }.</summary>
    public Action<string>? LoginWaiting { get; set; }

    public AppAccountsPort(AccountsViewModel acc, AppBotDirectory dir, IProxiesPort proxies) => (_acc, _dir, _proxies) = (acc, dir, proxies);

    private static string ProxyName(string? id) => id is null ? "" : Stores.Proxies.Get(id)?.Name ?? "";

    private AccountInfo Info(Account a, int index, bool? added = null)
    {
        var snap = _dir.ByAccountId(a.Id);
        return new AccountInfo(a.Id, snap?.Account ?? BotStatusMap.Alias(a.DisplayName, null, null, index), snap?.Name ?? "", snap?.Status ?? BotStatus.Offline,
                               a.Mode == ConnectionMode.Socket ? "socket" : "mitm", a.Source == AuthSource.Browser, a.AutoStartScript,
                               a.ProxyId is null ? null : ProxyName(a.ProxyId), a.GameProxyId is null ? null : ProxyName(a.GameProxyId), null, added);
    }

    public IReadOnlyList<AccountInfo> List() => Stores.Accounts.Accounts.Select((a, i) => Info(a, i)).ToList();

    /// <summary>Id, alias, perso ou identifiant de connexion (portail) — la clé peut être un identifiant, mais il n'est jamais RENDU.</summary>
    private Account? Find(string key)
    {
        var all = Stores.Accounts.Accounts;
        return all.FirstOrDefault(a => a.Id == key || a.Login.Equals(key, StringComparison.OrdinalIgnoreCase)
                                    || (a.DisplayName ?? "").Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(a => _dir.ByAccountId(a.Id) is { } b
                                    && (b.Account.Equals(key, StringComparison.OrdinalIgnoreCase) || b.Name.Equals(key, StringComparison.OrdinalIgnoreCase)
                                        || (int.TryParse(key, out var pid) && pid > 0 && b.Pid == pid)));
    }

    public AccountInfo? Get(string key) => Find(key) is { } a ? Info(a, Stores.Accounts.Accounts.ToList().IndexOf(a)) : null;

    public async Task<(AccountInfo? Account, string Reason)> AddAsync(string login, string? password, string? label, string? proxy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(login)) return (null, "identifiant manquant");
        if (Stores.Accounts.Accounts.Any(a => a.Login.Equals(login.Trim(), StringComparison.OrdinalIgnoreCase))) return (null, "compte déjà enregistré");

        ProxyEntry? entry = null;
        if (!string.IsNullOrWhiteSpace(proxy))
        {
            entry = ProxiesPort.Resolve(proxy!, create: true);
            if (entry is null) return (null, "proxy introuvable ou invalide");
            if (entry.FromAdapter && !LicenseService.HasPermission(LicenseService.Perm.Adapter)) return (null, "droit « adaptateur » absent");
            if (!entry.FromAdapter && !entry.FromRelay && !LicenseService.HasPermission(LicenseService.Perm.Proxy)) return (null, "droit « Proxy réseau » absent");
        }
        var ps = entry is null ? null : GivreClone.App.Services.ProxyResolver.Resolve(entry, Stores.Proxies);
        if (entry is not null && ps is null) return (null, "proxy injoignable");   // jamais de connexion depuis ton IP à la place

        LoginWaiting?.Invoke(string.IsNullOrWhiteSpace(label) ? login.Trim() : label!);
        var res = await Dispatcher.UIThread.InvokeAsync(() => WebAuthRunner.RunAsync(ps, login.Trim(), password ?? ""));
        switch (res.Outcome)
        {
            case WebAuthRunner.Outcome.Success when res.Tokens is not null:
                var acc = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var a = Stores.Accounts.AddBrowser(login.Trim(), res.Tokens.AccessToken, res.Tokens.RefreshToken);
                    if (entry is not null) a.ProxyId = entry.Id;
                    if (!string.IsNullOrWhiteSpace(label)) a.DisplayName = label!.Trim();
                    Stores.Accounts.Persist();
                    _acc.ReloadAccounts();
                    return a;
                });
                return (Info(acc, Stores.Accounts.Accounts.ToList().IndexOf(acc), true), "");
            case WebAuthRunner.Outcome.Unavailable: return (null, "fenêtre de connexion indisponible");
            case WebAuthRunner.Outcome.Cancelled: return (null, "fenêtre fermée");
            default: return (null, res.Error ?? "échec de l'authentification");
        }
    }

    public async Task<(IReadOnlyList<AccountInfo> Accounts, int Failures)> ImportFileAsync(string accountsPath, string? proxiesPath, CancellationToken ct)
    {
        var text = ProxiesPort.ReadSmallFile(accountsPath);
        if (text is null) return (Array.Empty<AccountInfo>(), 1);
        var (lines, errors) = AccountsLogic.ParseAccountFile(text);
        IReadOnlyList<ParsedProxy>? plist = null;
        if (!string.IsNullOrWhiteSpace(proxiesPath))
        {
            var ptext = ProxiesPort.ReadSmallFile(proxiesPath!);
            if (ptext is null) return (Array.Empty<AccountInfo>(), 1);
            plist = ProxyImportParser.Parse(ptext).Valid;
        }
        var result = new List<AccountInfo>();
        int fails = errors.Count;
        for (int i = 0; i < lines.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var l = lines[i];
            if (Find(l.Login) is { } existing) { result.Add(Info(existing, Stores.Accounts.Accounts.ToList().IndexOf(existing), false)); continue; }
            string? proxySpec = null;
            if (plist is not null)
            {
                if (i >= plist.Count) { fails++; continue; }   // pas de proxy pour cette ligne : ignoré, jamais depuis ton IP
                var p = plist[i];
                proxySpec = $"{(p.Protocol == "HTTP" ? "http" : "socks5")}://{(p.User is null ? "" : p.User + ":" + p.Pass + "@")}{p.Host}:{p.Port}";
            }
            var (acc, _) = await AddAsync(l.Login, l.Password, l.Label, proxySpec, ct);
            if (acc is null) fails++; else result.Add(acc);
        }
        return (result, fails);
    }

    private OrderResult Mutate(string key, Action<Account, AccountRow?> apply)
    {
        if (Find(key) is not { } a) return OrderResult.No("compte inconnu");
        Dispatcher.UIThread.Post(() => { apply(a, _acc.RowByModelId(a.Id)); Stores.Accounts.Persist(); });
        return OrderResult.Yes;
    }

    public OrderResult SetProxy(string account, string? proxy)
    {
        ProxyEntry? e = null;
        if (!string.IsNullOrWhiteSpace(proxy) && (e = ProxiesPort.Resolve(proxy!, create: true)) is null) return OrderResult.No("proxy introuvable ou invalide");
        return Mutate(account, (a, row) => { a.ProxyId = e?.Id; if (row is not null) row.SelectedProxy = e ?? ProxyEntry.None; });
    }

    public OrderResult SetGameProxy(string account, string? proxy)
    {
        ProxyEntry? e = null;
        if (!string.IsNullOrWhiteSpace(proxy) && (e = ProxiesPort.Resolve(proxy!, create: true)) is null) return OrderResult.No("proxy introuvable ou invalide");
        return Mutate(account, (a, _) => a.GameProxyId = e?.Id);
    }

    public OrderResult SetMode(string account, string mode)
        => Mutate(account, (a, row) => { a.Mode = mode == "socket" ? ConnectionMode.Socket : ConnectionMode.Mitm; if (row is { Running: false }) row.IsSocketMode = mode == "socket"; });

    public OrderResult SetAutoScript(string account, bool on) => Mutate(account, (a, _) => a.AutoStartScript = on);
    public OrderResult SetLabel(string account, string label) => Mutate(account, (a, row) => { a.DisplayName = label.Trim(); row?.RaiseIdentityChanged(); });

    private OrderResult OnUi(string account, Func<AccountRow, Task<OrderResult>> run)
    {
        if (Find(account) is not { } a) return OrderResult.No("compte inconnu");
        if (GivreClone.App.Services.Mcp.McpGuards.IsBlocked(a.Login, AppConfig.Current.McpBlockedLogins)) return OrderResult.No("compte protégé (AppConfig.McpBlockedLogins)");
        if (Dispatcher.UIThread.CheckAccess()) return OrderResult.No("appel depuis le fil d'interface refusé");
        try
        {
            return Dispatcher.UIThread.InvokeAsync(async () => _acc.RowByModelId(a.Id) is { } row ? await run(row) : OrderResult.No("compte introuvable")).GetAwaiter().GetResult();
        }
        catch (Exception e) { return OrderResult.No("échec : " + e.GetType().Name); }
    }

    public OrderResult CreateCharacter(string account, CharacterSpec spec) => OnUi(account, row => _acc.OrderCreateCharacterAsync(row, spec));

    public OrderResult DeleteCharacter(string account, string? server, string? character, string? answer)
        => OnUi(account, row => _acc.OrderDeleteCharacterAsync(row, server, character, answer));

    public OrderResult CancelCharacter(string account) => OrderResult.No("aucune création en attente");
}

/// <summary>Proxys de l'onglet Proxys. Le test mesure le temps total vers le serveur Dofus (msProxy non mesuré).</summary>
internal sealed class ProxiesPort : IProxiesPort
{
    public const int MaxFileBytes = 1_000_000;

    public static string? ReadSmallFile(string path)
    {
        try
        {
            if (!Path.IsPathRooted(path)) path = Path.Combine(MZP19LocalClone.DataRoot, "scripts", path);
            var fi = new FileInfo(path);
            return fi.Exists && fi.Length <= MaxFileBytes ? File.ReadAllText(path) : null;
        }
        catch { return null; }
    }

    public static ProxyInfo ToInfo(ProxyEntry e) => new(e.Id, e.Name,
        e.FromRelay ? "relay" : e.FromAdapter ? "adapter" : e.Protocol == ProxyProtocol.Socks5 ? "socks5" : "http",
        e.FromRelay || e.FromAdapter ? null : e.Host, e.FromRelay || e.FromAdapter ? null : e.Port,
        e.FromRelay || e.FromAdapter ? null : !string.IsNullOrEmpty(e.Username),
        Stores.Accounts.Accounts.Count(a => a.ProxyId == e.Id));

    /// <summary>Proxy par id, nom, « hôte:port », ou écrit en entier (créé s'il n'existe pas encore et <paramref name="create"/>).</summary>
    public static ProxyEntry? Resolve(string key, bool create)
    {
        key = key.Trim();
        var all = Stores.Proxies.Proxies;
        if (all.FirstOrDefault(p => p.Id == key || p.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) is { } hit) return hit;
        var parsed = ProxyImportParser.Parse(key).Valid.FirstOrDefault();
        if (parsed is null) return null;
        var same = all.FirstOrDefault(p => !p.FromAdapter && !p.FromRelay && p.Host.Equals(parsed.Host, StringComparison.OrdinalIgnoreCase)
                                        && p.Port == parsed.Port && string.Equals(p.Username, parsed.User, StringComparison.Ordinal));
        if (same is not null) return same;
        if (!create) return null;
        return Stores.Proxies.AddExternal($"{parsed.Host}:{parsed.Port}", parsed.Protocol == "HTTP" ? ProxyProtocol.Http : ProxyProtocol.Socks5,
                                          parsed.Host, parsed.Port, parsed.User, parsed.Pass);
    }

    public IReadOnlyList<ProxyInfo> List() => Stores.Proxies.Proxies.Select(ToInfo).ToList();

    public (ProxyInfo? Proxy, string Reason) Add(string spec, string? name)
    {
        var parsed = ProxyImportParser.Parse(spec).Valid.FirstOrDefault();
        if (parsed is null) return (null, "format non reconnu");
        var e = Resolve(spec, create: true);
        if (e is null) return (null, "format non reconnu");
        if (!string.IsNullOrWhiteSpace(name) && e.Name != name) { e.Name = name!.Trim(); Stores.Proxies.Update(e); }
        return (ToInfo(e), "");
    }

    public IReadOnlyList<ProxyInfo> ImportFile(string path)
    {
        var text = ReadSmallFile(path);
        if (text is null) return Array.Empty<ProxyInfo>();
        return ProxyImportParser.Parse(text).Valid
            .Select(p => Resolve($"{(p.Protocol == "HTTP" ? "http" : "socks5")}://{(p.User is null ? "" : p.User + ":" + p.Pass + "@")}{p.Host}:{p.Port}", create: true))
            .Where(e => e is not null).Select(e => ToInfo(e!)).ToList();
    }

    public async Task<ProxyTestResult> TestAsync(string proxy, CancellationToken ct)
    {
        var e = Resolve(proxy, create: false);
        if (e is null) return new ProxyTestResult(false, "", null, null, "proxy inconnu");
        if (!e.FromRelay && !LicenseService.HasPermission(LicenseService.Perm.Proxy)) return new ProxyTestResult(false, "", null, null, "droit « Proxy réseau » absent");
        var ps = GivreClone.App.Services.ProxyResolver.Resolve(e, Stores.Proxies);
        if (ps is null) return new ProxyTestResult(false, "", null, null, "proxy injoignable");
        var (ok, ip, ms) = await ProxyTester.TestAsync(ps, ct);
        return ok ? new ProxyTestResult(true, ip, (int)ms, null) : new ProxyTestResult(false, "", null, null, string.IsNullOrEmpty(ip) ? "échec du test" : ip);
    }
}
