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

// ===================== Scheduler du planning : lancement / arrêt du script selon les plages =====================
public partial class BotsViewModel
{
    // Scheduler : pour chaque bot dont le planning est ACTIVE, s'assure que le script DE LA PLAGE tourne PENDANT ses plages et
    // est arrete EN DEHORS (autoritaire quand le planning est activé). Le demarrage/arret passe par le meme hote Lua que les
    // boutons -> la console affiche "script démarré / arrêté". Resolution minute (evalue au throttle ~1,5 s).
    // Décision : PlanningEval.DecideRun (pure, testée). Plages CONTIGUËS (09:00 = fin de l'une, début de l'autre) : le script de
    // la plage précédente est arrêté PUIS celui de la nouvelle lancé dans le même passage (même corps -> il continue). Script
    // lancé À LA MAIN pendant une plage : laissé jusqu'à la fin de cette plage, le planning reprend la main à la plage
    // suivante (même règle qu'une fin de plage avec trou). Arrêt manuel pendant une plage : pas de relance avant la suivante.
    private void EvaluatePlanning()
    {
        // Killswitch : sans licence valide (session tuée / pas connecté), aucun script ne doit tourner NI redémarrer.
        if (!LicenseService.CanBot) { StopAllScripts(); return; }   // licence non autorisée OU botting suspendu (reprise bloquée)
        var now = DateTime.Now;
        foreach (var b in Bots)
        {
            // !Running : compte déconnecté dans ce même tick (fin de plage) -> nom remis à null, ne pas évaluer le planning "?"
            if (b.Pid <= 0 || !b.Row.Running || b.Row.Pid != b.Pid) continue;
            int pid = b.Pid;
            string? who = b.Row.CharacterName;
            var cp = PlanningStore.Get(who);
            if (!cp.Enabled) { _planRun.Remove(pid); _planRecheck.Remove(pid); continue; }
            // Le nom conservé par la ligne pendant une relance n'est pas une identité de session. Ne jamais lancer
            // l'ancien planning avant la première carte + identification, ni sur un homonyme d'un autre compte.
            if (string.IsNullOrWhiteSpace(who) || !PlanningEval.CanUseAccount(cp, b.Row.Model.Id)
                || Stores.Launch.CurrentMap((uint)pid) is not > 0
                || Stores.Launch.SelfCharacterInfo((uint)pid)?.Name != who
                || (string.IsNullOrEmpty(cp.AccountId) && Bots.Count(x => x.Row.Running && x.Row.CharacterName == who) != 1))
                continue;
            string? occ = PlanningEval.ActiveOccurrence(cp, now, who);
            bool running = _luaHosts.TryGetValue(pid, out var h) && h.Running;
            // Cap de niveau licence (offre gratuite) : un perso au-delà ne botte JAMAIS, même dans sa plage (arrêt + fenêtre).
            if (Stores.Launch.LevelBlocked((uint)pid)) { if (running) EnforceLevelCap(pid); continue; }
            if (occ is null)
            {
                // Hors plage : fin de la plage -> on ré-arme (la prochaine plage relancera) et on arrête le script.
                _planningPaused.Remove(pid);
                _planNoScriptLogged.Remove(pid);
                _planRun.Remove(pid);
                _planRecheck.Remove(pid);
                if (running && h is not null)
                {
                    AppendLuaLine("planning", Loc.Fmt("L.Bots.Planning.LogStop", who), LuaLogLevel.Warning, pid);
                    h.Stop("planning");
                    if (SelectedBot?.Pid == pid) LuaRunning = false;
                }
                continue;
            }
            // Rien à piloter pour l'instant : la plage n'est PAS marquée servie (une bascule de plage survenue pendant
            // l'attente est traitée dès le retour, script repris compris).
            if (_teamPiloted.Contains(pid) || b.FollowerLocked || b.Row.SessionTransitionActive
                || _socketWaiting.ContainsKey(pid)    // bot socket en cours de reconnexion / parqué
                || _socketResume.Contains(pid)        // reprise de SON script en attente (DriveSocketResume) : pas de doublon
                || Stores.Launch.IsLinkDown((uint)pid)   // MITM : session de jeu perdue (Dofus.exe ouvert) -> rien avant le retour en jeu
                || Stores.Launch.LevelStartBlocked((uint)pid)   // offre gratuite : niveau pas encore connu (ou au-delà du cap) -> rien à lancer
                // Connecté par le planning, ou « Reconnecter » en place (relance MITM : la ligne re-clée garde son perso, le
                // nouveau client démarre à peine) : attend la 1re carte.
                || ((PlanManaged(b.Row) || b.Row.ReconnectInPlace || _accounts.IsRelaunching(b.Row))
                    && Stores.Launch.CurrentMap((uint)pid) is null))
                continue;

            _planRun.TryGetValue(pid, out var run);
            if (PlanningEval.IsNewOccurrence(run?.Occ, occ))
            {
                // Nouvelle plage (même contiguë) : l'arrêt manuel / le blocage et le signalement « rien à lancer » de la plage
                // précédente sont levés.
                _planningPaused.Remove(pid);
                _planNoScriptLogged.Remove(pid);
            }
            bool resumed = _planRecheck.Remove(pid);
            int slot = PlanningEval.ActiveSlotIndex(cp, now, who);
            // Script PRECIS assigne a CETTE plage (bibliotheque). Assigné mais INTROUVABLE (supprimé) -> rien, signalé (pas un
            // autre trajet à la place). Non assigne -> script unique fige (cp.Script), sinon le texte de l'éditeur SEULEMENT si
            // ce bot est celui affiché (l'éditeur montre le script DU perso sélectionné : le lancer sur un autre bot ferait
            // tourner le mauvais trajet) — jamais le MODÈLE d'exemple par défaut (ni figé, ni affiché). Résolu seulement si la
            // décision en a besoin (rechargement du fichier .lua d'origine).
            SlotScriptPick pick = default;
            bool picked = false;
            string? SlotBody()
            {
                if (picked) return pick.Body;
                picked = true;
                ReloadSlotScriptFromFile(cp, slot, pid);   // script ajouté depuis un fichier -> contenu à jour du disque
                string? editor = ReferenceEquals(SelectedBot, b) && !IsSampleScript(LuaScript) ? LuaScript : null;
                // Modèle d'exemple figé par une build antérieure (texte d'alors) : traité lui aussi comme « aucun script ».
                pick = PlanningEval.ResolveSlotScript(cp, slot, editor, IsSampleScript(cp.Script) ? cp.Script : DefaultLuaScript);
                return pick.Body;
            }
            var act = PlanningEval.DecideRun(run?.Occ, occ, running, running ? h!.LastScriptText : null,
                                             _planningPaused.Contains(pid), resumed, SlotBody, started: run?.Body is not null);
            run ??= _planRun[pid] = new PlanRun();
            bool served = run.Occ == occ;
            run.Occ = occ;   // plage désormais servie (bascule traitée)
            if (!served && act == PlanRunAction.None) run.Body = null;   // 1re vue d'un script lancé à la main : pas celui du planning
            switch (act)
            {
                case PlanRunAction.Start:
                case PlanRunAction.Switch:
                {
                    string body = pick.Body!;
                    _planNoScriptLogged.Remove(pid);
                    if (act == PlanRunAction.Switch)
                    {
                        AppendLuaLine("planning", pick.Source == SlotScriptSource.Slot
                            ? Loc.Fmt("L.Bots.Planning.LogSwitch", who, pick.Name)
                            : Loc.Fmt("L.Bots.Planning.LogSwitchFallback", who), LuaLogLevel.Success, pid);
                        h!.Stop("planning");   // script de la plage PRÉCÉDENTE (raison « planning », pas « restart »)
                    }
                    else
                        AppendLuaLine("planning", pick.Source == SlotScriptSource.Slot
                            ? Loc.Fmt("L.Bots.Planning.LogStart", who, pick.Name)
                            : Loc.Fmt("L.Bots.Planning.LogStartFallback", who), LuaLogLevel.Success, pid);
                    _linkLostResume.Remove(pid);   // le planning (re)lance le script de la plage : plus de reprise d'après perte de session
                    var host = GetOrCreateHost(pid);
                    ApplyMovementSpeed(host, who);
                    // Nom dans les erreurs : script de la plage (bibliothèque), sinon le script du perso (« script.lua », sans nom de perso).
                    host.Start(body, pick.Source == SlotScriptSource.Slot && !string.IsNullOrWhiteSpace(pick.Name)
                        ? (pick.Name!.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? pick.Name : pick.Name + ".lua") : "script.lua");
                    run.Body = body;
                    BotScripts.Set(pid, ScriptDisplayName(cp, slot, who), true);   // télémétrie serveur : nom + running
                    if (SelectedBot?.Pid == pid) LuaRunning = true;
                    break;
                }
                case PlanRunAction.Keep:
                    // Nouvelle plage, MÊME script : il continue, désormais tenu par cette plage (nom télémétrie à jour).
                    run.Body = pick.Body;
                    AppendLuaLine("planning", Loc.Fmt("L.Bots.Planning.LogKeep", who), LuaLogLevel.Info, pid);
                    BotScripts.Set(pid, ScriptDisplayName(cp, slot, who), true);
                    break;
                case PlanRunAction.StopNoScript:
                    // Nouvelle plage sans script à lancer : celui de la plage précédente n'a plus lieu de tourner.
                    run.Body = null;
                    AppendLuaLine("planning", Loc.Fmt("L.Bots.Planning.LogStopNoScript", who), LuaLogLevel.Warning, pid);
                    h!.Stop("planning");
                    if (SelectedBot?.Pid == pid) LuaRunning = false;
                    LogNoScript(pid, pick);
                    break;
                case PlanRunAction.NoScript:
                    // Rien à lancer : signalé UNE fois par plage (pas de pause : si l'utilisateur sélectionne ce bot ou assigne
                    // un script entre-temps, il démarre au passage suivant).
                    run.Body = null;
                    LogNoScript(pid, pick);
                    break;
            }
        }
        if (SelectedBot is { Pid: > 0 } sb)
        {
            var cpSel = PlanningStore.Get(sb.Row.CharacterName);
            Planning.UpdateClock(now, cpSel.Enabled && PlanningEval.IsActiveNow(cpSel, now, sb.Row.CharacterName));
        }
    }

    // Plage sans script à lancer (script assigné introuvable / rien d'assigné ni de repli) : signalé UNE fois par plage.
    private void LogNoScript(int pid, SlotScriptPick pick)
    {
        if (_planNoScriptLogged.Add(pid))
            AppendLuaLine("planning", pick.Source == SlotScriptSource.Missing
                ? Loc.Fmt("L.Planning.Auto.ScriptMissing", pick.Name)
                : Loc.T("L.Planning.Auto.NoScript"), LuaLogLevel.Warning, pid);
    }
}
