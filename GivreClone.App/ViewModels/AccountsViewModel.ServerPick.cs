using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Transport;
using GivreClone.App.Services.Transport.Socket;
using GivreClone.Engine.Accounts;
using GivreClone.Engine.Proxies;
using GivreClone.Protocol;
using GivreLauncher.Core.Ankama;

namespace GivreClone.App.ViewModels;

// ===================== Modal de choix serveur -> personnage (mode Socket) =====================
public partial class AccountsViewModel
{
    private bool _pickForCreation;
    // ---------- Modal de choix serveur -> personnage (mode Socket « écran de choix… ») ----------
    // Affiché par la boucle de session pendant l'étape A (liste serveurs connue). mode 0 = on part de la liste des SERVEURS,
    // mode 1 = directement les PERSONNAGES du serveur le plus récemment joué (« Changer de serveur » pour revenir), comme le
    // vrai client. Renvoie TOUJOURS (serverId, personnage) choisis, ou null = annulé. Un seul modal à la fois ; 10 min max.
    private async Task<(int ServerId, string? Character)?> PromptSocketServerCharAsync(
        AccountRow row, IReadOnlyList<ConnServer> servers, int mode, CancellationToken ct, bool createCharacter = false)
    {
        // Choix imposé par un script (création / suppression de personnage) : pas de fenêtre.
        if (SocketScriptedPickRegistry.Take(row.Login) is { } scripted)
        {
            try { _launch.GameNames.EnsureServerNamesLoaded(); } catch { }
            var srv = servers.FirstOrDefault(s => s.Id.ToString() == scripted.Server.Trim())
                   ?? servers.FirstOrDefault(s => string.Equals(PickServerLabel(s.Id), scripted.Server.Trim(), StringComparison.OrdinalIgnoreCase));
            if (srv is null)
            {
                SocketCreationRegistry.Clear(row.Login); SocketDeletionRegistry.Clear(row.Login);
                await Dispatcher.UIThread.InvokeAsync(() => Toasts.Show($"{row.Login} : serveur « {scripted.Server} » introuvable pour ce compte.", success: false));
                return null;
            }
            return (srv.Id, scripted.Character);
        }
        try { await _serverPickGate.WaitAsync(ct).ConfigureAwait(true); }
        catch (OperationCanceledException) { return null; }
        try
        {
            // La modale ne s'ouvre qu'avec les VRAIS noms : lecture ciblée des noms de serveur (~70 ms, plus le chargement complet
            // des tables du jeu, ~12 s) + symboles de classe (~0,1 s). Déjà prêts d'habitude (WarmUpForSocket) ; sinon attente
            // courte. Dofus introuvable -> repli « Serveur N » / icône générique.
            try
            {
                await Task.Run(() =>
                {
                    try { _launch.GameNames.EnsureServerNamesLoaded(); } catch { }
                    try { Stores.ClassIcons.EnsureLoaded(); } catch { }
                    try { Stores.CosmeticIcons.EnsureLoaded(); } catch { }
                }, ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { return null; }
            if (ct.IsCancellationRequested) return null;

            // Tous les serveurs de l'identification, même vides : on peut y créer un personnage (sauf restriction d'accès, voir CreateAllowed).
            var playable = servers.ToList();
            var withChars = playable.Where(s => s.Characters.Count > 0).ToList();
            int start = mode == 1 && withChars.Count > 0 ? MostRecentServer(withChars) : 0;
            var tcs = new TaskCompletionSource<(int ServerId, string? Character)?>(TaskCreationOptions.RunContinuationsAsynchronously);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _pickSource = playable;
                _pickForCreation = createCharacter;
                ServerPickLogin = row.Login;
                ShowPickStep(start);
                _serverPickTcs = tcs;
                ServerPickVisible = true;
            });
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            await using (timeout.Token.Register(() => tcs.TrySetResult(null)))
            {
                var pick = await tcs.Task.ConfigureAwait(true);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ReferenceEquals(_serverPickTcs, tcs)) return;
                    ServerPickVisible = false;   // listes gardées : pas d'effondrement de la carte pendant le fondu de sortie
                    _pickForCreation = false;
                    _serverPickTcs = null;
                });
                return pick;
            }
        }
        finally { _serverPickGate.Release(); }
    }

    // Serveur du personnage joué le plus récemment (départ du mode « choix du personnage »).
    private static int MostRecentServer(IReadOnlyList<ConnServer> servers)
    {
        int best = servers.Count > 0 ? servers[0].Id : 0;
        DateTimeOffset newest = DateTimeOffset.MinValue;
        foreach (var s in servers)
            foreach (var c in s.Characters)
            {
                var d = ParsePickDate(c.LastConnectionDate);
                if (d > newest) { newest = d; best = s.Id; }
            }
        return best;
    }

    // Étape affichée : serverId 0 = liste des serveurs, sinon les personnages de ce serveur (recherche remise à zéro).
    private void ShowPickStep(int serverId)
    {
        _pickServerId = serverId;
        ServerPickStep = serverId > 0 ? 1 : 0;
        _pickSearchMute = true;
        ServerPickSearch = "";
        _pickSearchMute = false;
        RebuildPickLists();
    }

    partial void OnServerPickSearchChanged(string value) { if (!_pickSearchMute) RebuildPickLists(); }

    private void RebuildPickLists()
    {
        string q = (ServerPickSearch ?? "").Trim();
        bool Match(string s) => q.Length == 0 || s.Contains(q, StringComparison.OrdinalIgnoreCase);
        if (ServerPickStep == 0)
        {
            var cards = _pickSource.Select(BuildServerCard).OrderByDescending(c => c.LastPlayed).ToList();
            ServerPickServers.Clear();
            foreach (var c in cards)
                if (Match(c.Name) || c.Preview.Any(ch => Match(ch.Name))) ServerPickServers.Add(c);
            ServerPickTitle = Loc.T("L.Socket.PickServer");
            ServerPickSubtitle = Loc.Fmt("L.Socket.ServersSub", cards.Count(c => c.Preview.Count > 0));
            ServerPickShowSearch = cards.Count > 6;
        }
        else
        {
            var s = _pickSource.FirstOrDefault(x => x.Id == _pickServerId);
            var chars = s is null ? new List<SocketCharCard>()
                                  : s.Characters.Select(c => BuildCharCard(s, c)).OrderByDescending(c => c.LastPlayed).ToList();
            ServerPickChars.Clear();
            foreach (var c in chars)
                if (Match(c.Name) || Match(c.ClassName)) ServerPickChars.Add(c);
            ServerPickTitle = Loc.T("L.Socket.PickCharacter");
            ServerPickSubtitle = s is null ? "" : Loc.Fmt("L.Socket.CharsSub", PickServerLabel(s.Id), chars.Count);
            // Création : refusée par le serveur sans abonnement (serveur « abonnés ») ou si le serveur mono-compte est déjà occupé.
            (CreateAllowed, CreateCardHint) = s?.Accessibility switch
            {
                1 => (false, "Serveur réservé aux abonnés : un abonnement est nécessaire pour y créer un personnage."),
                2 => (false, "Serveur mono-compte déjà occupé par un autre de tes comptes."),
                _ => (true, s is { Characters.Count: 0 } ? "Aucun personnage ici : le bot en crée un puis entre en jeu." : "Créé par le bot sur ce serveur, puis entrée en jeu."),
            };
            ServerPickShowSearch = chars.Count > 6;
        }
    }

    [ObservableProperty] public partial bool CreateAllowed { get; set; } = true;
    [ObservableProperty] public partial string CreateCardHint { get; set; } = "";

    private string PickServerLabel(int id) => _launch.GameNames.ServerName(id) ?? Loc.Fmt("L.Socket.ServerN", id);

    private SocketServerCard BuildServerCard(ConnServer s)
    {
        var chars = s.Characters.Select(c => BuildCharCard(s, c)).OrderByDescending(c => c.LastPlayed).ToList();
        var last = chars.Count > 0 ? chars[0].LastPlayed : default;
        string name = PickServerLabel(s.Id);
        return new SocketServerCard
        {
            ServerId = s.Id,
            Name = name,
            Initial = name.Length > 0 && char.IsLetter(name[0]) ? char.ToUpperInvariant(name[0]).ToString() : "#",
            Online = s.Online,
            MonoAccount = s.MonoAccount && s.Accessibility != 2,   // déjà dit par la pastille « Mono-compte : déjà occupé »
            Restriction = s.Accessibility switch
            {
                1 => Loc.T("L.Socket.RestrictSub"),
                2 => Loc.T("L.Socket.RestrictMono"),
                _ => null,
            },
            CharCountText = Loc.Fmt("L.Socket.NChar", chars.Count),
            CharNames = string.Join("  ·  ", chars.Select(c => c.Name)),
            Preview = chars,
            LastPlayed = last,
            LastPlayedText = PickAgo(last),
        };
    }

    private static SocketCharCard BuildCharCard(ConnServer s, ConnCharacter c)
    {
        var when = ParsePickDate(c.LastConnectionDate);
        string cls = CharacterInfoReader.BreedName(c.Breed) ?? "?";
        return new SocketCharCard
        {
            ServerId = s.Id,
            Name = c.Name,
            LevelText = Loc.Fmt("L.Socket.Level", c.Level),
            ClassName = cls + "  ·  " + Loc.T(c.Gender == 1 ? "L.Socket.Female" : "L.Socket.Male"),
            Icon = Stores.ClassIcons.Get(c.Breed, c.Gender),   // null tant que le bundle n'est pas lu (rafraîchi ensuite)
            LastPlayed = when,
            LastPlayedText = PickAgo(when),
        };
    }

    // Date ISO du serveur de connexion (« 2026-09-27T04:34:28.303Z ») ; default si absente/illisible. Culture invariante (AOT).
    private static DateTimeOffset ParsePickDate(string s) =>
        DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : default;

    // « il y a 3 h » — nombres seulement (aucun nom de jour/mois : AOT = culture invariante).
    private static string PickAgo(DateTimeOffset when)
    {
        if (when == default) return "";
        var d = DateTimeOffset.UtcNow - when;
        if (d.TotalMinutes < 1) return Loc.T("L.Socket.AgoNow");
        if (d.TotalHours < 1) return Loc.Fmt("L.Socket.AgoMin", (int)d.TotalMinutes);
        if (d.TotalDays < 1) return Loc.Fmt("L.Socket.AgoHour", (int)d.TotalHours);
        return Loc.Fmt("L.Socket.AgoDay", (int)d.TotalDays);
    }

    [RelayCommand]
    private void PickSocketServer(SocketServerCard? s)
    {
        if (s is null || !ServerPickVisible) return;
        ShowPickStep(s.ServerId);
        if (_pickForCreation && CreateAllowed)
        {
            _pickForCreation = false;
            NewSocketCharacter();
        }
    }

    [RelayCommand]
    private void PickSocketCharacter(SocketCharCard? c)
    {
        if (c is null || !ServerPickVisible || !ServerPickOnChars) return;
        ServerPickVisible = false;
        _serverPickTcs?.TrySetResult((c.ServerId, c.Name));
    }

    [RelayCommand]
    private void SocketPickBack() => ShowPickStep(0);

    [RelayCommand]
    private void CancelServerPick() { ServerPickVisible = false; _serverPickTcs?.TrySetResult(null); }
}
