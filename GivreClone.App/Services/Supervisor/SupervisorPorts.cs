using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services.Supervisor;

/// <summary>Résultat d'un ordre : ok + raison (portail : « rend ok, raison »).</summary>
public readonly record struct OrderResult(bool Ok, string Reason = "")
{
    public static OrderResult Yes => new(true);
    public static OrderResult No(string reason) => new(false, reason);
}

/// <summary>Annuaire des bots : instantané de TOUS les comptes enregistrés, hors ligne compris.</summary>
public interface IBotDirectory
{
    IReadOnlyList<BotSnapshot> Snapshot();
}

/// <summary>
/// Ordres exécutés par l'onglet Bots avec les gardes de l'application (licence, Multi, Dofus.exe connu en MITM, cap de niveau,
/// reconnexion en cours, suiveur d'équipe). Le verbe Lua attend le résultat 15 s au plus.
/// </summary>
public interface IBotOrders
{
    Task<OrderResult> ConnectAsync(string bot, string? character, string? server, CancellationToken ct);
    Task<OrderResult> DisconnectAsync(string bot, CancellationToken ct);
    Task<OrderResult> ReconnectAsync(string bot, CancellationToken ct);
    Task<OrderResult> SwitchCharacterAsync(string bot, string? character, string? server, bool restartScript, CancellationToken ct);
    Task<OrderResult> StartScriptAsync(string bot, string scriptOrCode, CancellationToken ct);
    Task<OrderResult> StopScriptAsync(string bot, CancellationToken ct);
    Task<OrderResult> RunAsync(string bot, string code, CancellationToken ct);
    Task<int> StopAllScriptsAsync(CancellationToken ct);
    Task<int> DisconnectAllAsync(CancellationToken ct);
}

/// <summary>Table « compte » du portail : jamais d'identifiant de connexion ni de mot de passe.</summary>
public sealed record AccountInfo(string Id, string Label, string Character, string Status, string Mode, bool Browser, bool AutoScript,
                                 string? Proxy, string? GameProxy, PendingCharacter? Pending = null, bool? Added = null);

public sealed record PendingCharacter(int ServerId, string Server, string Breed, string Sex, string Name);

/// <summary>Demande de création de personnage (accounts:createCharacter).</summary>
public sealed record CharacterSpec(string? Server, string? Breed, string? Sex, string? Name, int? Head, int? Body,
                                   IReadOnlyDictionary<string, string>? Colors, bool LeaveTutorial);

public sealed record ProxyInfo(string Id, string Name, string Type, string? Host, int? Port, bool? Auth, int Accounts);

public sealed record ProxyTestResult(bool Ok, string Ip, int? Ms, int? MsProxy, string Reason = "");

/// <summary>Comptes : liste, réglages et création. Les ajouts passent par la fenêtre de connexion Ankama (captcha et Shield à la main).</summary>
public interface IAccountsPort
{
    IReadOnlyList<AccountInfo> List();
    AccountInfo? Get(string key);
    Task<(AccountInfo? Account, string Reason)> AddAsync(string login, string? password, string? label, string? proxy, CancellationToken ct);
    Task<(IReadOnlyList<AccountInfo> Accounts, int Failures)> ImportFileAsync(string accountsPath, string? proxiesPath, CancellationToken ct);
    OrderResult SetProxy(string account, string? proxy);
    OrderResult SetGameProxy(string account, string? proxy);
    OrderResult SetMode(string account, string mode);
    OrderResult SetAutoScript(string account, bool on);
    OrderResult SetLabel(string account, string label);
    OrderResult CreateCharacter(string account, CharacterSpec spec);
    OrderResult DeleteCharacter(string account, string? server, string? character, string? answer);
    OrderResult CancelCharacter(string account);
}

/// <summary>Proxys de l'onglet Proxys.</summary>
public interface IProxiesPort
{
    IReadOnlyList<ProxyInfo> List();
    (ProxyInfo? Proxy, string Reason) Add(string spec, string? name);
    IReadOnlyList<ProxyInfo> ImportFile(string path);
    Task<ProxyTestResult> TestAsync(string proxy, CancellationToken ct);
}

/// <summary>Droit « Multi » (lancer un superviseur et passer ses ordres), fourni par l'application.</summary>
public interface ISupervisorPermissions
{
    bool HasMulti { get; }
}
