using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services;

/// <summary>Choix de l'utilisateur pour une carte pendant l'enregistrement d'un trajet (modale « récolter / combattre »).</summary>
public readonly record struct MapPlan(bool Gather, bool Fight);

/// <summary>Nature de la sortie enregistrée pour une carte (détermine le champ Lua émis).</summary>
internal enum RoadExit { None, Direction, Travel, Door, Npc }

/// <summary>Une étape enregistrée : une carte du circuit, ce qu'on y fait, et COMMENT on en sort.</summary>
internal sealed class RoadStep
{
    public long Map;
    public bool Gather;
    public bool Fight;
    public bool Planned;              // l'utilisateur a répondu à la modale pour cette carte
    public RoadExit Kind = RoadExit.None;
    public string? Exit;             // Direction:"right" | Travel:"<mapId>" | Door:"<cell>" ; null pour Npc/None
    public List<string>? Npc;        // Npc : séquence Lua ["talkNpc(123)", "replyNpc(2)", "replyNpcAndChangeMap(1)"]
    public List<string> Actions = new(); // actions PNJ terminées SANS changement de carte (quêtes)
    public string Mechanism = "";    // libellé lisible pour le commentaire
}

/// <summary>
/// Enregistreur de trajet (« Road Creator » natif). OBSERVE les déplacements MANUELS du joueur via
/// <see cref="LaunchService.MapChanged"/> + <see cref="LaunchService.PacketDecoded"/> et construit un trajet
/// DÉCLARATIF GivreClone (une étape par carte distincte, avec la sortie réellement utilisée). Ne FORGE ni n'ENVOIE
/// rien au jeu — pure observation. À chaque nouvelle carte, demande via <see cref="PromptForMap"/> si l'on veut y
/// récolter / combattre.
/// <para>Classification de la sortie : (1) un BORD adjacent (coord cardinale ±1) est TOUJOURS une marche à pied — un
/// vrai franchissement PNJ/porte/zaap n'atterrit jamais sur une coord cardinale-adjacente (bateau = loin, porte/intérieur
/// = même coord ou non adjacent) ; (2) sinon on prend l'action OUTBOUND la PLUS RÉCENTE dans sa fenêtre (celle qui a
/// effectivement déclenché le changement) : dialogue PNJ (talkNpc + replyNpc… → custom), zaap/havre-sac (nom du message),
/// porte/escalier (InteractiveUseRequest sur un interactif type -1 non-ressource → door=cellule) ; (3) sinon voyage
/// numérique (GoToMapId gère). Aucun numéro de champ proto en dur : npc_id / reply_id / element_id lus PAR NOM via
/// <see cref="LaunchService.PacketInt"/> et l'index de réponse via <see cref="LaunchService.NpcReplyIndexOf"/>.</para>
/// </summary>
public sealed class RoadRecorder
{
    private const long NpcWindowMs = 6000;    // délai max action PNJ -> changement de carte pour l'attribuer au PNJ
    private const long TpWindowMs = 6000;     // délai max zaap/havre-sac -> changement de carte
    private const long DoorWindowMs = 8000;   // délai max usage porte -> changement de carte

    private readonly LaunchService _launch;

    /// <summary>Modale « récolter / combattre » pour une carte, fournie par la VM (null = annulé/superseded).</summary>
    public Func<long, CancellationToken, Task<MapPlan?>>? PromptForMap { get; set; }
    /// <summary>Journalisation lisible (branchée sur la console Lua du perso).</summary>
    public Action<string>? Log { get; set; }

    private readonly object _gate = new();
    private readonly List<RoadStep> _steps = new();
    private readonly Dictionary<long, RoadStep> _byMap = new();
    private readonly List<string> _questEvents = new(); // journal des validations, en commentaire Lua uniquement
    private long _prevMap;
    private (int X, int Y)? _prevCoord;

    // Buffers de la carte COURANTE (remis à zéro à chaque changement de carte). Tous accédés SOUS _gate.
    private readonly List<string> _npcSeq = new();   // talkNpc/replyNpc accumulés sur la carte courante
    private bool _npcLastReply;                       // le dernier élément de _npcSeq est une réponse
    private int _npcLastReplyIdx = -1;                // index dans _npcSeq de la dernière réponse (→ replyNpcAndChangeMap)
    private long _npcTalkAt;                           // horodatage du dernier talkNpc
    private long _npcLastReplyAt;                      // horodatage de la dernière réponse
    private string? _lastMech;                        // "zaap" / "havre-sac" (nom du message sortant)
    private long _lastMechAt;
    private int _lastDoorCell = -1;                   // cellule d'un interactif porte/escalier utilisé
    private long _lastDoorAt;

    private CancellationTokenSource? _cts;
    private int _gen;                                  // génération de session (anti-restart en vol)

    public bool IsRecording { get; private set; }
    public uint RecordingPid { get; private set; }
    public int StepCount { get { lock (_gate) return _steps.Count; } }

    /// <summary>Valide la sélection actuellement visible lorsque l'utilisateur arrête
    /// l'enregistrement sans cliquer d'abord sur « Confirmer » dans la modale.</summary>
    public void SetCurrentPlan(bool gather, bool fight)
    {
        lock (_gate)
        {
            if (!IsRecording || _prevMap <= 0 || !_byMap.TryGetValue(_prevMap, out var step)) return;
            step.Gather = gather;
            step.Fight = fight;
            step.Planned = true;
        }
    }

    public RoadRecorder(LaunchService launch) => _launch = launch;

    /// <summary>Démarre l'enregistrement pour <paramref name="pid"/> à partir de <paramref name="startMap"/>. Une seule
    /// session à la fois (redémarre proprement si déjà en cours).</summary>
    public void Start(uint pid, long startMap)
    {
        Stop(silent: true);                          // une seule session à la fois (désabonne + annule l'ancienne)
        RoadStep first;
        CancellationTokenSource cts;
        lock (_gate)
        {
            _steps.Clear(); _byMap.Clear(); _questEvents.Clear(); ResetBuffers();
            _prevMap = startMap;
            _prevCoord = _launch.MapCoordOf(startMap);
            _cts = cts = new CancellationTokenSource();
            _gen++;
            RecordingPid = pid;
            IsRecording = true;
            first = GetOrCreate(startMap);           // première carte : créée + promptée
        }
        _launch.MapChanged += OnMapChanged;
        _launch.PacketDecoded += OnPacket;
        PromptFor(first, cts);
    }

    /// <summary>Arrête l'enregistrement et renvoie le script Lua généré (chaîne vide si rien d'exploitable).</summary>
    public string Stop() => Stop(silent: false);

    private string Stop(bool silent)
    {
        if (!IsRecording) return "";
        IsRecording = false;
        _launch.MapChanged -= OnMapChanged;
        _launch.PacketDecoded -= OnPacket;
        try { _cts?.Cancel(); } catch { /* best-effort */ }
        _cts?.Dispose(); _cts = null;
        if (silent) return "";
        lock (_gate)
        {
            // Le dernier dialogue peut se terminer sans transition de carte.
            FlushNpcHere();
            return Generate();
        }
    }

    // Observation OUTBOUND (le joueur agit). On lit npc_id / reply_id / element_id PAR NOM (aucun numéro en dur).
    private void OnPacket(DecodedPacket p)
    {
        if (!IsRecording || p.Pid != RecordingPid) return;
        if (!p.ClientToServer)
        {
            if (p.Name is "QuestStartedEvent" or "QuestStepStartedEvent" or "QuestStepValidatedEvent"
                or "QuestObjectiveValidatedEvent" or "QuestValidatedEvent")
            {
                int questId = _launch.PacketInt(p, "quest_id", 0);
                int stepId = _launch.PacketInt(p, "step_id", 0);
                int objId = _launch.PacketInt(p, "objective_id", 0);
                string line = $"-- QUETE {p.Name} : quest={questId}, step={stepId}, objective={objId}, map={_prevMap}";
                lock (_gate)
                {
                    if (_questEvents.Count < 1000 && (_questEvents.Count == 0 || _questEvents[^1] != line))
                        _questEvents.Add(line);
                }
            }
            return;
        }
        switch (p.Name)
        {
            case "TeleportRequest": lock (_gate) { _lastMech = "zaap"; _lastMechAt = Now; } break;          // zaap au sol / zaapi / zaap havre-sac
            case "HavenBagEnterRequest": lock (_gate) { _lastMech = "havre-sac"; _lastMechAt = Now; } break;
            case "DialogLeaveRequest":                                                           // le joueur ferme le dialogue => pas un franchissement PNJ
                lock (_gate) { FlushNpcHere(); ResetNpc(); }
                break;
            case "NpcGenericActionRequest":                                                      // le joueur parle à un PNJ
            {
                int npc = _launch.PacketInt(p, "npc_id", 0);
                if (npc != 0)
                {
                    // Un id contextuel négatif varie à chaque session : préférer le modèle PNJ stable.
                    var actor = Stores.World.Get(p.Pid).Actors.FirstOrDefault(a => a.ActorId == npc && a.NpcId > 0);
                    int stable = actor is not null ? actor.NpcId : npc;
                    int action = _launch.PacketInt(p, "npc_action_id", 3);
                    lock (_gate)
                    {
                        if (_npcSeq.Count > 0) FlushNpcHere(); // nouveau dialogue sur la même carte
                        _npcSeq.Add(action == 3 ? $"talkNpc({stable})" : $"npc:npc({stable},{action})");
                        _npcLastReply = false; _npcTalkAt = Now;
                    }
                }
                break;
            }
            case "NpcDialogReplyRequest":                                                        // le joueur choisit une réponse
            {
                int rid = _launch.PacketInt(p, "reply_id", -1);
                int idx = rid >= 0 ? _launch.NpcReplyIndexOf(p.Pid, rid) : 0;
                lock (_gate)
                {
                    if (idx >= 1) { _npcSeq.Add($"replyNpc({idx})"); _npcLastReply = true; _npcLastReplyIdx = _npcSeq.Count - 1; _npcLastReplyAt = Now; }
                    else if (rid > 0)
                    {
                        // Quand l'index 1-based est inconnu, l'ID serveur reste
                        // vérifiable via npc:hasReply et jouable via npc:sendReply.
                        _npcSeq.Add($"if not npc:hasReply({rid}) then global:finishScript() return end npc:sendReply({rid})");
                        _npcLastReply = true; _npcLastReplyIdx = _npcSeq.Count - 1; _npcLastReplyAt = Now;
                    }
                    else _npcSeq.Add("global:printWarning('Reponse PNJ non decodee'); global:finishScript(); return");
                }
                break;
            }
            case "InteractiveUseRequest":                                                        // récolte OU porte : on ne garde que les portes/escaliers
            {
                int elementId = _launch.PacketInt(p, "element_id", 0);
                if (elementId <= 0) break;
                var it = Stores.World.Get(p.Pid).Interactives.FirstOrDefault(i => i.ElementId == elementId);
                if (it is not null && !_launch.IsGatherResource(it) && it.ElementTypeId == -1) lock (_gate) { _lastDoorCell = it.CellId; _lastDoorAt = Now; }
                break;
            }
        }
    }

    private void OnMapChanged(uint pid, long mapId, int? x, int? y)
    {
        if (!IsRecording || pid != RecordingPid || mapId <= 0) return;
        if (_launch.IsInFight(pid)) return;          // instance de combat -> pas une vraie étape de trajet
        int gen = _gen;

        (int X, int Y)? toCoord = _launch.MapCoordOf(mapId);
        if (toCoord is null && x is int xi && y is int yi) toCoord = (xi, yi);

        RoadStep? newStep = null;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            if (!IsRecording || pid != RecordingPid || gen != _gen) return;   // (F6) un Stop/Start a pu passer : session périmée
            if (mapId == _prevMap) return;                                    // re-sync de la même carte (jru/jss redondant)
            // 1) Finaliser la SORTIE de la carte qu'on vient de quitter (on garde la PREMIÈRE sortie vue).
            if (_prevMap > 0)
            {
                var fromStep = GetOrCreate(_prevMap);
                if (fromStep.Kind == RoadExit.None) ClassifyExit(fromStep, toCoord, mapId);
                // Les quêtes peuvent demander des conversations sans téléportation.
                // Elles précèdent la sortie normale et ne doivent pas être perdues.
                if (fromStep.Kind != RoadExit.Npc) FlushNpcHere();
            }
            // 2) Nouvelle carte : créer l'étape si inédite (elle sera « promptée »).
            if (!_byMap.ContainsKey(mapId)) newStep = GetOrCreate(mapId);
            _prevMap = mapId;              // (F6) curseur mis à jour SOUS le verrou
            _prevCoord = toCoord;
            ResetBuffers();
            cts = _cts;
        }

        if (newStep is { } ns) { Log?.Invoke(FmtStarted(mapId)); PromptFor(ns, cts); }
        else Log?.Invoke(FmtLoop(mapId));
    }

    // Décide la sortie de `from` vers `toMap`. Appelé SOUS _gate.
    private void ClassifyExit(RoadStep from, (int X, int Y)? toCoord, long toMap)
    {
        // (F1/F3) BORD adjacent = toujours une marche à pied (prioritaire : masque tout buffer PNJ/porte périmé d'une
        // action non-franchissante — quête, coffre, PNJ de quête — car un vrai franchissement n'est jamais cardinal-adjacent).
        if (_prevCoord is { } f && toCoord is { } t)
        {
            int dx = t.X - f.X, dy = t.Y - f.Y;
            if (dx == 1 && dy == 0) { SetDir(from, "right"); return; }
            if (dx == -1 && dy == 0) { SetDir(from, "left"); return; }
            if (dx == 0 && dy == 1) { SetDir(from, "bottom"); return; }
            if (dx == 0 && dy == -1) { SetDir(from, "top"); return; }
        }
        long now = Now;
        // (F4) PNJ seulement si la séquence commence par un talkNpc (rejouable) ; (F5) réponse OU talk seul (téléport sur le talk).
        bool npcHasTalk = _npcSeq.Count > 0
            && (_npcSeq[0].StartsWith("talkNpc(", StringComparison.Ordinal)
                || _npcSeq[0].StartsWith("npc:npc(", StringComparison.Ordinal));
        long npcAt = _npcLastReply ? _npcLastReplyAt : _npcTalkAt;
        bool npcOk = npcHasTalk && now - npcAt < NpcWindowMs;
        bool tpOk = _lastMech is "zaap" or "havre-sac" && now - _lastMechAt < TpWindowMs;
        bool doorOk = _lastDoorCell >= 0 && now - _lastDoorAt < DoorWindowMs;
        // Choisir l'action OUTBOUND la PLUS RÉCENTE (celle qui a déclenché le changement de carte).
        long best = long.MinValue; int which = 0;   // 1=PNJ 2=zaap/havre-sac 3=porte
        if (npcOk && npcAt > best) { best = npcAt; which = 1; }
        if (tpOk && _lastMechAt > best) { best = _lastMechAt; which = 2; }
        if (doorOk && _lastDoorAt > best) { best = _lastDoorAt; which = 3; }
        switch (which)
        {
            case 1:
            {
                var seq = new List<string>(_npcSeq);
                if (_npcLastReply && _npcLastReplyIdx >= 0 && _npcLastReplyIdx < seq.Count)
                {
                    if (seq[_npcLastReplyIdx].StartsWith("replyNpc(", StringComparison.Ordinal))
                        seq[_npcLastReplyIdx] = seq[_npcLastReplyIdx].Replace("replyNpc(", "if not replyNpcAndChangeMap(")
                            + " then global:printWarning('PNJ: aucun changement de carte'); global:finishScript(); return end";
                    else if (seq[_npcLastReplyIdx].Contains("npc:sendReply(", StringComparison.Ordinal))
                        seq[_npcLastReplyIdx] = seq[_npcLastReplyIdx].Replace("npc:sendReply(", "if not npc:replyByIdAndChangeMap(")
                            + " then global:printWarning('PNJ: aucun changement de carte'); global:finishScript(); return end";
                }
                from.Kind = RoadExit.Npc; from.Npc = seq; from.Mechanism = "PNJ";
                return;
            }
            case 2: from.Kind = RoadExit.Travel; from.Exit = Id(toMap); from.Mechanism = _lastMech!; return;
            case 3: from.Kind = RoadExit.Door; from.Exit = _lastDoorCell.ToString(CultureInfo.InvariantCulture); from.Mechanism = "porte"; return;
        }
        // Même coord (intérieur/porte non typée), non adjacent ou coord inconnue -> voyage numérique (GoToMapId gère porte/zaap/instance).
        from.Kind = RoadExit.Travel; from.Exit = Id(toMap); from.Mechanism = "porte / carte non adjacente";
    }

    private static void SetDir(RoadStep s, string dir) { s.Kind = RoadExit.Direction; s.Exit = dir; s.Mechanism = "à pied"; }

    private void ResetNpc() { _npcSeq.Clear(); _npcLastReply = false; _npcLastReplyIdx = -1; _npcTalkAt = 0; _npcLastReplyAt = 0; }

    private void FlushNpcHere()
    {
        if (_npcSeq.Count == 0 || _prevMap <= 0) return;
        var step = GetOrCreate(_prevMap);
        // Un trajet classique garde une seule étape par carte. Les passages
        // répétés avec progression de quête nécessitent le Road Creator v4.
        if (step.Actions.Count + _npcSeq.Count <= 256) step.Actions.AddRange(_npcSeq);
        ResetNpc();
    }

    private void ResetBuffers()
    {
        ResetNpc();
        _lastMech = null; _lastMechAt = 0; _lastDoorCell = -1; _lastDoorAt = 0;
    }

    private RoadStep GetOrCreate(long map)
    {
        if (_byMap.TryGetValue(map, out var s)) return s;
        s = new RoadStep { Map = map };
        _byMap[map] = s; _steps.Add(s);
        return s;
    }

    private async void PromptFor(RoadStep step, CancellationTokenSource? cts)
    {
        var prompt = PromptForMap;
        if (prompt is null || cts is null) return;
        int generation = _gen;
        try
        {
            var plan = await prompt(step.Map, cts.Token).ConfigureAwait(false);
            lock (_gate)
            {
                // Une réponse tardive de l'ancienne session, ou la modale supersédée,
                // ne peut pas écraser un plan déjà confirmé ni un nouvel enregistrement.
                if (IsRecording && generation == _gen && ReferenceEquals(_cts, cts)
                    && plan is { } mp && !step.Planned)
                {
                    step.Gather = mp.Gather;
                    step.Fight = mp.Fight;
                    step.Planned = true;
                }
            }
        }
        catch { /* annulé / superseded */ }
    }

    private static long Now => Environment.TickCount64;
    private static string Id(long map) => map.ToString(CultureInfo.InvariantCulture);
    private string Coord(long map) => _launch.MapCoordOf(map) is (int x, int y) ? $"{x},{y}" : Id(map);
    private string FmtStarted(long map) => $"Carte {Coord(map)} enregistrée ({StepCount}).";
    private string FmtLoop(long map) => $"Retour sur {Coord(map)} — boucle bouclée ({StepCount} cartes).";

    // Génère un trajet déclaratif GivreClone exécutable par RunRoute (move() -> table d'étapes). map = id NUMÉRIQUE
    // (délibéré : les intérieurs partagent la coord de l'extérieur -> une clé coord serait ambiguë ; l'id matche exact).
    private string Generate()
    {
        if (_steps.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine($"-- Trajet enregistré par GivreClone — {_steps.Count} carte(s).");
        sb.AppendLine("-- Complète ELEMENTS_TO_GATHER avec les ElementTypeId à récolter (visibles dans le journal « récolte »).");
        sb.AppendLine("ELEMENTS_TO_GATHER = { }");
        sb.AppendLine("MAX_PODS = 90");
        sb.AppendLine("MIN_MONSTERS = 1");
        sb.AppendLine("MAX_MONSTERS = 8");
        sb.AppendLine("local RC_CUSTOM_DONE = {}  -- les actions PNJ sans déplacement ne sont jouées qu'une fois par map");
        foreach (var e in _questEvents) sb.AppendLine(e);
        sb.AppendLine();
        sb.AppendLine("function move()");
        sb.AppendLine("  return {");
        foreach (var s in _steps)
        {
            string g = s.Gather ? "true" : "false";
            string f = s.Fight ? "true" : "false";
            string exitField, comment;
            switch (s.Kind)
            {
                case RoadExit.Direction:
                    exitField = $", changeMap = \"{s.Exit}\""; comment = "  -- a pied"; break;
                case RoadExit.Travel:
                    exitField = $", changeMap = \"{s.Exit}\""; comment = $"  -- {s.Mechanism} -> {Coord(long.Parse(s.Exit!, CultureInfo.InvariantCulture))}"; break;
                case RoadExit.Door:
                    exitField = $", door = \"{s.Exit}\""; comment = $"  -- porte (cellule {s.Exit})"; break;
                case RoadExit.Npc:
                    exitField = $", custom = function() {string.Join(" ", s.Npc ?? new List<string>())} end"; comment = "  -- PNJ (dialogue)"; break;
                default:
                    exitField = ""; comment = "  -- derniere carte (pas de sortie enregistree)"; break;
            }
            // Moteur : custom est exécuté AVANT door/path/changeMap.
            if (s.Actions.Count > 0 && s.Kind != RoadExit.Npc)
                exitField += $", custom = function() if RC_CUSTOM_DONE[{s.Map}] then return end {string.Join(" ", s.Actions)} RC_CUSTOM_DONE[{s.Map}] = true end";
            if (s.Actions.Count > 0 && s.Kind == RoadExit.Npc)
                exitField = $", custom = function() if RC_CUSTOM_DONE[{s.Map}] then return end {string.Join(" ", s.Actions.Concat(s.Npc ?? new List<string>()))} RC_CUSTOM_DONE[{s.Map}] = true end";
            sb.AppendLine($"    {{ map = \"{s.Map}\", gather = {g}, fight = {f}{exitField} }},{comment}");
        }
        sb.AppendLine("  }");
        sb.AppendLine("end");
        return sb.ToString();
    }
}
