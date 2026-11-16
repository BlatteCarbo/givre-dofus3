using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

// ===================== Compatibilité tiers: actions de COMPTE demandées par un script (thread UI) =====================
// global:disconnect / reconnect(h) / reconnectBis(min) / reconnectSec(s) et givreController:loadAnAccount / unloadAnAccount.
// Le script enfile (LaunchService.EnqueueAccountAction) ; on résout ici, au Tick, avec les mêmes gardes que le planning
// (licence, multi-compte, Dofus.exe pour le MITM ; droit « socket » vérifié par le lancement lui-même).
public partial class BotsViewModel
{
    private sealed class ScriptReconnect
    {
        public required string AccountId;
        public required string Character;
        public long DueAt;                 // TickCount64 de la relance
        public string? ScriptName;
        public string? ScriptBody;         // script relancé une fois le perso de retour en jeu
        public bool Launched;
        public long LaunchedAt;
        public int Gen;                    // AccountRow.LaunchGen : session coupée par le script, puis celle de NOTRE relance
        public bool Attended;              // « Reconnecter » (clic) : lancement SURVEILLÉ (code du Bouclier possible)
        public bool Reselect;              // « Reconnecter » sur le bot sélectionné : resélectionné à son retour dans la liste
        public bool InPlace;               // « Reconnecter » MITM EN PLACE : reprise sur le NOUVEAU pid seulement (entrée Bots re-clée)
        public int OldPid;                 // ... pid du client tué (ses trames tardives ne déclenchent jamais la reprise)
        public bool Rekeyed;               // ... la ligne est passée sur le pid du nouveau client (AccountsViewModel.PidRekeyed)
        public int ExpectedServerId;       // explicit transitions require the confirmed identity, not just a name
        public long ExpectedCharacterId;
    }
    private readonly List<ScriptReconnect> _scriptReconnects = new();
    private const long ScriptResumeMaxMs = 5 * 60 * 1000;
    private int _accountsSnapTick;

    // Instantané des comptes lu par les scripts (alias = libellé du compte, sinon perso ; l'identifiant de connexion ne sert
    // qu'à reconnaître un nom saisi par le script). ~1 fois / 1,5 s.
    private void PushScriptAccounts()
    {
        if (++_accountsSnapTick < 5) return;
        _accountsSnapTick = 0;
        var list = new List<LaunchService.ScriptAccountInfo>();
        int n = 0;
        foreach (var row in _accounts.Accounts)
        {
            n++;
            string alias = !string.IsNullOrWhiteSpace(row.Label) ? row.Label!.Trim()
                         : !string.IsNullOrWhiteSpace(row.CharacterName) ? row.CharacterName!
                         : Loc.Fmt("L.Lua.SnowAccountN", n);
            list.Add(new LaunchService.ScriptAccountInfo(row.Model.Id, alias, row.CharacterName ?? "",
                row.Pid is int p && p > 0 ? (uint)p : 0, row.Running, row.Login ?? ""));
        }
        Stores.Launch.SetAccountsSnapshot(list);
    }

    private void DrainAccountActions()
    {
        int budget = 8;
        while (budget-- > 0 && Stores.Launch.TryDequeueAccountAction(out var act))
        {
            try
            {
                if (!LicenseService.IsAuthorized) continue;
                switch (act.Kind)
                {
                    case LaunchService.ScriptAccountActionKind.Disconnect: ScriptDisconnect(act); break;
                    case LaunchService.ScriptAccountActionKind.Launch: ScriptLaunchAccount(act); break;
                    case LaunchService.ScriptAccountActionKind.Stop: ScriptStopAccount(act); break;
                }
            }
            catch { /* une action ratée n'arrête jamais le Tick */ }
        }
    }

    private AccountRow? RowOfPid(uint pid) => _accounts.Accounts.FirstOrDefault(a => a.Pid is int p && p == (int)pid);

    private AccountRow? RowOfScriptName(string? name)
        => Stores.Launch.FindScriptAccount(name) is { } acc ? _accounts.Accounts.FirstOrDefault(a => a.Model.Id == acc.Id) : null;

    private void ScriptDisconnect(LaunchService.ScriptAccountAction act)
    {
        var rows = new List<AccountRow>();
        if (RowOfPid(act.FromPid) is { } self) rows.Add(self);
        if (act.IncludeTeam && Teams.FirstOrDefault(t => t.Leader is { } l && l.Pid == (int)act.FromPid) is { } team)
            foreach (var m in team.Members)
                if (m.Pid > 0 && !rows.Contains(m.Row)) rows.Add(m.Row);
        foreach (var row in rows)
        {
            if (!row.Running) continue;
            string character = row.CharacterName ?? "";
            if (act.DelayMs > 0 && character.Length > 0)
            {
                string? name = null, body = null;
                if (row.Pid is int p && _luaHosts.TryGetValue(p, out var host) && BotScripts.Get(p) is { Running: true } bs)
                { name = bs.Name; body = host.LastScriptText; }
                _scriptReconnects.RemoveAll(r => r.AccountId == row.Model.Id);
                _scriptReconnects.Add(new ScriptReconnect
                {
                    AccountId = row.Model.Id, Character = character, DueAt = Environment.TickCount64 + act.DelayMs,
                    ScriptName = name, ScriptBody = body, Gen = row.LaunchGen,
                });
            }
            else PlanHoldForScriptStop(row);   // déconnexion sèche : le planning « connexion auto » ne la défait pas (fin de plage)
            if (row.Pid is int pid) AppendLuaLine("compte", Loc.T("L.Lua.SnowDisconnected"), LuaLogLevel.Warning, pid);
            _accounts.StopForPlanning(row, act.DelayMs > 0 ? Loc.Fmt("L.Lua.SnowStatusReconnect", DateTime.Now.AddMilliseconds(act.DelayMs).ToString("HH:mm"))
                                                           : Loc.T("L.Lua.SnowStatusDisconnected"));
        }
    }

    private void ScriptLaunchAccount(LaunchService.ScriptAccountAction act)
    {
        if (RowOfScriptName(act.Target) is not { } row || row.Running) return;
        StartScriptLaunch(row, row.CharacterName, (int)act.FromPid);
    }

    private void ScriptStopAccount(LaunchService.ScriptAccountAction act)
    {
        if (RowOfScriptName(act.Target) is not { } row || !row.Running) return;
        PlanHoldForScriptStop(row);
        _accounts.StopForPlanning(row, Loc.T("L.Lua.SnowStatusDisconnected"));
    }

    /// <summary>Relance programmée par un script, pas encore échue : le planning « connexion auto » ne relance pas ce compte
    /// d'ici là (sinon il défaisait la pause ~60 s après, en la prenant pour un crash).</summary>
    private bool ScriptReconnectHeld(string accountId) => _scriptReconnects.Any(r => r.AccountId == accountId && !r.Launched);

    /// <summary>Arrêt / lancement MANUEL du compte : la relance programmée par le script est caduque.</summary>
    private void DropScriptReconnect(string accountId) => _scriptReconnects.RemoveAll(r => r.AccountId == accountId);

    /// <summary>Arrêt EXPLICITE du script (Arrêter le script, web STOP_SCRIPT, arrêt d'équipe) pendant une relance MITM en
    /// place : le script relevé avant la relance n'est plus repris au retour en jeu (l'entrée reste : resélection, « Reconnexion
    /// terminée »).</summary>
    private void DropInPlaceScript(string accountId)
    {
        foreach (var r in _scriptReconnects)
            if (r.InPlace && r.AccountId == accountId) { r.ScriptBody = null; r.ScriptName = null; }
    }

    // Lancement sans surveillance (mêmes pré-requis que le planning). false = refusé tout de suite (journalisé).
    // attended (« Reconnecter ») : lancement SURVEILLÉ (modal de lancement, code du Bouclier), refus aussi en toast.
    private bool StartScriptLaunch(AccountRow row, string? character, int logPid, bool attended = false)
    {
        bool Refuse(string text)
        {
            AppendLuaLine("compte", text, LuaLogLevel.Warning, logPid);
            if (attended) { AudioService.PlayError(); Toasts.Show($"{row.Login} : {text}", success: false, sound: false); }
            return false;
        }
        if (!AccountsViewModel.UsesSocket(row) && !AccountsViewModel.DofusExeKnown())
            return Refuse(Loc.T(attended ? "L.Dofus.NotSelected" : "L.Planning.Auto.NoDofus"));
        if (!LicenseService.HasPermission(LicenseService.Perm.Multi) && _accounts.Accounts.Any(a => a.Running && a != row))
            return Refuse(Loc.T(attended ? "L.Perm.MultiDenied" : "L.Lua.NoPermMulti"));
        if (!attended) { _ = LaunchQuietAsync(row, character); return true; }
        // Mapping purgé depuis le clic (déconnexion licence…) : pas de lancement « à l'aveugle ».
        if (MappingCatalog.MissingEssentials().Count > 0) return Refuse(Loc.T("L.Reconnect.NoMapping"));
        _ = LaunchAttendedAsync(row, character);
        return true;
    }

    private async Task LaunchAttendedAsync(AccountRow row, string? character)
    {
        try { await _accounts.LaunchForReconnectAsync(row, string.IsNullOrWhiteSpace(character) ? null : character).ConfigureAwait(true); }
        catch { /* le pipeline de lancement affiche déjà son erreur */ }
    }

    private async Task LaunchQuietAsync(AccountRow row, string? character)
    {
        try { await _accounts.LaunchForPlanningAsync(row, string.IsNullOrWhiteSpace(character) ? null : character).ConfigureAwait(true); }
        catch { /* le pipeline de lancement affiche déjà son erreur */ }
    }

    // Relances programmées : lancement à l'échéance, puis reprise du script quand le perso a rechargé sa carte.
    // Le script n'est repris QUE sur la session de NOTRE relance (LaunchGen) : un compte relancé entre-temps par quelqu'un
    // d'autre (utilisateur, « Connecter maintenant », équipe, autre script) rend la relance caduque — jamais adopté.
    // « Reconnecter » (Attended) : même file, lancement SURVEILLÉ immédiat (compte hors ligne), bot resélectionné à son retour ;
    // relance MITM en place (InPlace) : lancée par « Reconnecter », la file ne fait que reprendre le script sur le nouveau pid.
    private void DriveScriptReconnects()
    {
        if (_scriptReconnects.Count > 0) DriveScriptReconnectQueue();
        SyncReconnectFlags();   // bouton « Reconnecter » grisé tant qu'une reconnexion manuelle est en cours
    }

    private void DriveScriptReconnectQueue()
    {
        long now = Environment.TickCount64;
        // « Reconnecter » abandonné (licence, refus — même après sa mise en route —, compte relancé ailleurs, délai dépassé) : une
        // ligne hors ligne qui montre encore NOTRE statut (« Reconnexion… ») ne reste pas figée dessus. Un statut écrit depuis
        // par le lancement (erreur, crash…) est gardé. Compte MITM arrêté par le serveur / relance en place ratée : son script
        // redevient celui de « Reconnecter » (nouvel essai possible).
        void Drop(ScriptReconnect r, AccountRow? row)
        {
            _scriptReconnects.Remove(r);
            if (!r.Attended || row is not { Running: false }) return;
            if (row.Status == Loc.T("L.Reconnect.Status")) row.Status = Loc.T("L.Accounts.StatusIdle");
            if ((!r.Launched || r.InPlace) && row.ServerKickedOffline && !string.IsNullOrEmpty(r.ScriptBody))
                _kickedScripts[r.AccountId] = (r.ScriptName, r.ScriptBody!);
        }
        foreach (var r in _scriptReconnects.ToList())
        {
            var row = _accounts.Accounts.FirstOrDefault(a => a.Model.Id == r.AccountId);
            if (row is null || !LicenseService.IsAuthorized) { Drop(r, row); continue; }
            if (row.SessionTransitionActive) continue;
            if (!r.Launched)
            {
                if (row.Running || row.LaunchGen != r.Gen) { Drop(r, row); continue; }   // relancé par un autre
                if (now < r.DueAt) continue;
                int genBefore = row.LaunchGen;
                if (!StartScriptLaunch(row, r.Character, -1, r.Attended)) { Drop(r, row); continue; }
                // Lancement SURVEILLÉ refusé de façon SYNCHRONE par le pipeline de lancement (multi, route, droit socket, mapping :
                // toast déjà affiché) : rien n'est parti -> abandon, la ligne ne reste pas sur le décompte.
                if (r.Attended && row.LaunchGen == genBefore && !row.Running) { Drop(r, row); continue; }
                // LaunchCoreAsync a incrémenté row.LaunchGen AVANT son premier await : c'est la génération de NOTRE relance.
                r.Launched = true; r.LaunchedAt = now; r.Gen = row.LaunchGen;
                continue;
            }
            // Relance MITM en place en cours (Bouclier compris) : le délai de reprise n'est compté qu'APRÈS.
            if (r.InPlace && _accounts.IsRelaunching(row)) { r.LaunchedAt = now; continue; }
            // Relance refusée / échouée / arrêtée (compte hors ligne), autre lancement, ou délai dépassé : plus rien à reprendre.
            if (now - r.LaunchedAt > ScriptResumeMaxMs || row.LaunchGen != r.Gen || !row.Running) { Drop(r, row); continue; }
            if (row.Pid is not int pid || pid <= 0) continue;
            // En place : jamais sur l'ancien pid (une trame tardive du client tué y repeuple carte + nom) — seulement une fois la
            // ligne re-clée sur le nouveau client (un pid réutilisé par l'OS reste possible : Rekeyed).
            if (r.InPlace && pid == r.OldPid && !r.Rekeyed) continue;
            if (r.Reselect && _byPid.TryGetValue(pid, out var back))
            {
                r.Reselect = false;
                if (!ReferenceEquals(SelectedBot, back)) SelectedBot = back;
            }
            if (Stores.Launch.CurrentMap((uint)pid) is null) continue;
            if (r.ExpectedCharacterId > 0)
            {
                if (Stores.Launch.CharacterId((uint)pid) != r.ExpectedCharacterId
                    || Stores.Launch.ServerIdOf((uint)pid) != r.ExpectedServerId)
                { Drop(r, row); continue; }
                if (_byPid.TryGetValue(pid, out var readyBot) && SocketLuaStartBlockReason(readyBot) is not null) continue;
            }
            // Perso EN JEU : en place, celui identifié sur le NOUVEAU pid (la ligne garde l'ancien nom pendant la relance).
            string? live = r.InPlace ? Stores.World.Get((uint)pid).CharacterName : row.CharacterName;
            if (r.InPlace && string.IsNullOrEmpty(live)) continue;
            if (r.Character.Length > 0 && !string.Equals(live, r.Character, StringComparison.OrdinalIgnoreCase))
            {
                // « Reconnecter » entré sur un AUTRE perso (MITM : dernier perso joué) : pas de script sur le mauvais perso.
                if (!r.Attended || string.IsNullOrEmpty(live)) continue;
                _scriptReconnects.Remove(r);
                AppendLuaLine("compte", Loc.Fmt("L.Reconnect.OtherChar", live!), LuaLogLevel.Warning, pid);
                continue;
            }
            _scriptReconnects.Remove(r);
            if (string.IsNullOrEmpty(r.ScriptBody) || Stores.Launch.LevelStartBlocked((uint)pid) || BotScripts.Get(pid) is { Running: true }
                || (r.InPlace && _planningPaused.Contains(pid)))
            {
                // Rien à reprendre (pas de script, perso capé, un script tourne déjà : planning, ou script arrêté À LA MAIN pendant
                // la relance en place — pause du planning migrée sur le nouveau pid par OnPidRekeyed).
                if (r.Attended) AppendLuaLine("compte", Loc.T("L.Reconnect.Done"), LuaLogLevel.Success, pid);
                continue;
            }
            var host = GetOrCreateHost(pid);
            ApplyMovementSpeed(host, row.CharacterName);
            AppendLuaLine("compte", Loc.Fmt("L.Lua.SnowReconnectResume", r.ScriptName ?? "script"), LuaLogLevel.Success, pid);
            host.Start(r.ScriptBody!, r.ScriptName);
            BotScripts.Set(pid, r.ScriptName, true);
            if (SelectedBot?.Pid == pid) LuaRunning = true;
            // Script relevé AVANT la coupure (Reconnecter en place / hors ligne, reconnexion de script) : il peut dater d'une
            // plage précédente -> le planning le confronte à la plage active au prochain passage (bascule si elle a changé).
            PlanNoteResumed(pid, r.AccountId);
        }
    }
}
