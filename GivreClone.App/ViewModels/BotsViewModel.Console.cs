using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

// ===================== Console : envoi de message en jeu + commandes /… (façon AnkaBot) =====================
public partial class BotsViewModel
{
    // ============ ENVOI DE MESSAGE EN JEU (depuis la Console) ============
    // Discuter en jeu depuis Givre sans revenir sur le client. Les primitives sont dans LaunchService et résolvent
    // leur code+champs PAR NOM via le mapping streamé (ChatPrivateMessageRequest / ChatChannelMessageRequest), avec
    // durcissement anti-injection (\r\n retirés, cap 240). Un échec (non mappé / relais absent) est journalisé dans
    // la console transport (même onglet). Canaux = ChatActivableChannelsEnum Dofus (stables) ; Privé = MP par pseudo.
    public sealed record ChatChannel(string Label, int Channel, bool Private);

    /// <summary>Canaux d'envoi (localisés, reconstruits sur Loc.Changed). Privé (MP) + canaux numériques Dofus.</summary>
    [ObservableProperty] public partial IReadOnlyList<ChatChannel> ChatChannels { get; set; } = MakeChatChannels();
    private static IReadOnlyList<ChatChannel> MakeChatChannels() => new[]
    {
        new ChatChannel(Loc.T("L.Bots.Chat.Chan.Private"), -1, true),
        new ChatChannel(Loc.T("L.Bots.Chat.Chan.General"), 0, false),
        new ChatChannel(Loc.T("L.Bots.Chat.Chan.Group"), 1, false),
        new ChatChannel(Loc.T("L.Bots.Chat.Chan.Guild"), 2, false),
        new ChatChannel(Loc.T("L.Bots.Chat.Chan.Alliance"), 3, false),
        new ChatChannel(Loc.T("L.Bots.Chat.Chan.Trade"), 5, false),
        new ChatChannel(Loc.T("L.Bots.Chat.Chan.Recruit"), 6, false),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChatIsPrivate))]
    public partial int ChatChannelIndex { get; set; }
    /// <summary>Le canal sélectionné est-il « Privé » ? (affiche le champ destinataire).</summary>
    public bool ChatIsPrivate => ChatChannelIndex >= 0 && ChatChannelIndex < ChatChannels.Count && ChatChannels[ChatChannelIndex].Private;

    [ObservableProperty] public partial string ChatTarget { get; set; } = "";   // destinataire (MP)
    [ObservableProperty] public partial string ChatDraft { get; set; } = "";    // message à envoyer

    [RelayCommand]
    private void SendChat()
    {
        if (SelectedBot is not { Pid: > 0 } bot) return;
        string msg = (ChatDraft ?? "").Trim();
        if (msg.Length == 0) return;
        // Commande console façon AnkaBot : un input qui commence par '/' est une COMMANDE BOT (pas un message jeu).
        if (msg.StartsWith('/')) { ChatDraft = ""; HandleConsoleCommand(bot, msg); return; }
        var ch = (ChatChannelIndex >= 0 && ChatChannelIndex < ChatChannels.Count) ? ChatChannels[ChatChannelIndex] : ChatChannels[0];
        bool ok = ch.Private
            ? Stores.Launch.SendPrivateMessage((uint)bot.Pid, (ChatTarget ?? "").Trim(), msg)
            : Stores.Launch.SendChannelMessage((uint)bot.Pid, ch.Channel, msg);
        if (ok) ChatDraft = "";   // l'écho jeu confirme l'envoi ; l'échec (non mappé / relais absent / cible vide) apparaît dans la console transport
    }

    // ======================= COMMANDES CONSOLE /… (façon AnkaBot) =======================
    // Hôtes Lua TRANSITOIRES dédiés aux commandes /cmd (par pid) : n'interrompent NI le script principal (_luaHosts) NI le
    // suivi d'équipe. /abortCmd stoppe l'hôte de commande du perso.
    private readonly Dictionary<int, GivreLuaHost> _cmdHosts = new();

    /// <summary>Écho d'une commande console (/xxx) dans l'onglet CONSOLE du bot — LÀ où se trouve la barre de saisie.
    /// Avant, le retour partait dans l'onglet Lua (AppendLuaLine « cmd »), donc invisible là où l'utilisateur tape :
    /// les commandes semblaient « ne rien faire ». Tag [cmd], routé disque + Console par-bot comme le firehose transport.</summary>
    private static void CmdEcho(int pid, string text) => TransportDiag.Emit((uint)pid, $"[cmd] {text}");

    /// <summary>Route un input console `/xxx …` sur le bot donné. Réponses écrites dans SA Console (onglet Console).</summary>
    private void HandleConsoleCommand(BotRowViewModel bot, string raw)
    {
        uint pid = (uint)bot.Pid;
        string body = raw.Substring(1);
        int sp = body.IndexOf(' ');
        string cmd = (sp < 0 ? body : body.Substring(0, sp)).Trim().ToLowerInvariant();
        string arg = sp < 0 ? "" : body.Substring(sp + 1).Trim();   // reste BRUT (préserve espaces/parenthèses pour /cmd)
        switch (cmd)
        {
            case "clear": TransportDiag.ClearFor(pid); break;   // vide la Console que l'utilisateur regarde (pas l'onglet Lua)
            case "cellid":
            {
                int? c = Stores.World.Get(pid).SelfCell;
                CmdEcho(bot.Pid, c is int cc ? $"Cellule courante : {cc}" : "Cellule inconnue (pas encore synchronisée).");
                break;
            }
            case "mapid":
            {
                long? m = Stores.Launch.CurrentMap(pid);
                string coord = m is long mm && Stores.Launch.MapCoordOf(mm) is (int x, int y) ? $" ({x},{y})" : "";
                CmdEcho(bot.Pid, m is long v ? $"Carte courante : {v}{coord}" : "Carte inconnue.");
                break;
            }
            case "id":
            {
                if (arg.Length == 0) { CmdEcho(bot.Pid, "Usage : /id NOM"); break; }
                var a = Stores.World.Get(pid).Actors.FirstOrDefault(x => string.Equals(x.Name, arg, StringComparison.OrdinalIgnoreCase));
                // PNJ : + son identifiant MODÈLE (npc_id, celui des scripts), distinct de l'id d'acteur propre à la carte.
                CmdEcho(bot.Pid, a is null ? $"« {arg} » introuvable sur la carte courante."
                               : a.NpcId > 0 ? Loc.Fmt("L.Cmd.IdNpc", a.Name, a.ActorId, a.NpcId)
                               : $"{a.Name} : {a.ActorId}");
                break;
            }
            case "fight": RunConsoleLua(bot, "fight()"); break;
            case "gather": RunConsoleLua(bot, "gatherAll()"); break;
            case "goto": HandleGoto(bot, arg); break;
            case "cmd":
                if (arg.Length == 0) CmdEcho(bot.Pid, "Usage : /cmd <instruction Lua>");
                else RunConsoleLua(bot, arg);
                break;
            case "abortcmd": AbortConsoleLua(bot); break;
            case "joinboss": HandleJoinBoss(bot); break;
            default: CmdEcho(bot.Pid, $"Commande inconnue : /{cmd}. Dispo : clear, cmd, abortCmd, fight, gather, cellid, mapid, id, goto, joinBoss."); break;
        }
    }

    /// <summary>Lance une CHAÎNE Lua one-shot sur un hôte transitoire dédié aux commandes (annulable via /abortCmd).</summary>
    private void RunConsoleLua(BotRowViewModel bot, string lua)
    {
        if (!LicenseService.IsAuthorized) { CmdEcho(bot.Pid, "Licence requise."); return; }
        if (LicenseService.IsSuspended) { CmdEcho(bot.Pid, Loc.T("L.LicLink.Paused")); return; }   // reprise de session bloquée
        if (RefuseLevelCapped(bot.Pid, interactive: true)) return;   // cap de niveau licence (offre gratuite)
        int pid = bot.Pid;
        if (_cmdHosts.Remove(pid, out var old)) { try { old.Dispose(); } catch { /* best-effort */ } }   // Dispose = Stop + désabonne les events du launch
        _joinBossTargets.Remove(pid);   // nouvelle commande = plus un ralliement joinBoss (HandleJoinBoss la ré-étiquette après)
        var host = new GivreLuaHost((uint)pid, Stores.Launch, Stores.World);
        host.RunFinished += () =>
        {
            if (host.LastTravelResult is { Ok: false } result)
                Toasts.Post(result.Reason, success: false);
        };
        host.Log += entry => Dispatcher.UIThread.Post(() =>
        {
            var row = MakeRow(entry, pid);
            _allLog.Add(row);
            while (_allLog.Count > 2000) _allLog.RemoveAt(0);
            AppendTeamConsole(row);
            if (pid == LogPid && Passes(row)) { LuaOutput.Add(row); while (LuaOutput.Count > 600) LuaOutput.RemoveAt(0); }
        });
        ApplyMovementSpeed(host, bot.Row.CharacterName);
        _cmdHosts[pid] = host;
        CmdEcho(pid, $"▶ {lua}");   // écho dans la Console (les lignes d'exécution [lua pid]/[recolte] suivent déjà là)
        host.RunOnce(lua);
    }

    private void AbortConsoleLua(BotRowViewModel bot)
    {
        _joinBossTargets.Remove(bot.Pid);
        if (_cmdHosts.Remove(bot.Pid, out var h)) { try { h.Dispose(); } catch { /* best-effort */ } CmdEcho(bot.Pid, "Commande interrompue."); }
        else CmdEcho(bot.Pid, "Aucune commande /cmd en cours.");
    }

    /// <summary>/goto x,y  ou  /goto &lt;id&gt; : voyage multi-cartes (via l'hôte de commande, annulable).</summary>
    private void HandleGoto(BotRowViewModel bot, string arg)
    {
        if (arg.Length == 0) { CmdEcho(bot.Pid, "Usage : /goto x,y  ou  /goto <id>"); return; }
        long target = 0;
        int comma = arg.IndexOf(',');
        if (comma > 0)
        {
            if (int.TryParse(arg.Substring(0, comma).Trim(), out int x) && int.TryParse(arg.Substring(comma + 1).Trim(), out int y))
                target = Stores.Launch.MapIdAt(x, y, Stores.Launch.CurrentWorldMap((uint)bot.Pid)) ?? 0;
        }
        else long.TryParse(arg.Trim(), out target);
        if (target <= 0) { CmdEcho(bot.Pid, $"Carte introuvable : {arg}"); return; }
        RunConsoleLua(bot, $"travelTo({target})");
    }

    // Carte cible du DERNIER ralliement joinBoss lancé par membre (pid -> map du chef). Sert l'IDEMPOTENCE du chemin Lua :
    // un script appelle joinBoss en boucle; relancer un travelTo identique DISPOSE l'hôte /cmd en cours
    // (voyage coupé en pleine marche) -> le follower semblerait figé. Thread UI uniquement.
    private readonly Dictionary<int, long> _joinBossTargets = new();

    /// <summary>/joinBoss : le CHEF ordonne à ses membres de rejoindre sa carte (voyage one-shot par membre).
    /// <paramref name="fromLua"/> = appel team:joinLeader()/joinBoss() : IDEMPOTENT (un membre dont le voyage vers la MÊME
    /// carte est encore en cours n'est pas relancé, écho silencieux si rien de nouveau). La console /joinBoss reste inchangée.</summary>
    private void HandleJoinBoss(BotRowViewModel bot, bool fromLua = false)
    {
        var team = Teams.FirstOrDefault(t => t.Members.Any(m => m.Pid == bot.Pid));
        if (team is null) { CmdEcho(bot.Pid, "Ce perso n'est dans aucune équipe."); return; }
        if (team.Leader is not { Pid: > 0 } leader) { CmdEcho(bot.Pid, "Équipe sans chef en jeu."); return; }
        if (leader.Pid != bot.Pid) { CmdEcho(bot.Pid, "Commande réservée au CHEF du groupe."); return; }
        long leaderMap = Stores.Launch.CurrentMap((uint)leader.Pid) ?? 0;
        if (leaderMap <= 0) { CmdEcho(bot.Pid, "Carte du chef inconnue."); return; }
        int n = 0;
        foreach (var m in team.Members)
        {
            if (m.Pid <= 0 || m.Pid == leader.Pid) continue;
            if ((Stores.Launch.CurrentMap((uint)m.Pid) ?? 0) == leaderMap) continue;   // déjà sur place
            if (fromLua && _joinBossTargets.TryGetValue(m.Pid, out var tgt) && tgt == leaderMap
                && _cmdHosts.TryGetValue(m.Pid, out var running) && running.Running) continue;   // voyage identique en cours : ne pas le couper
            RunConsoleLua(m, $"travelTo({leaderMap})");
            _joinBossTargets[m.Pid] = leaderMap;
            n++;
        }
        if (fromLua && n == 0) return;   // appel Lua répété sans effet nouveau : pas de spam console
        CmdEcho(bot.Pid, n > 0 ? $"joinBoss : {n} membre(s) envoyé(s) vers la carte du chef ({leaderMap})." : "joinBoss : tous les membres sont déjà sur la carte du chef.");
    }
}
