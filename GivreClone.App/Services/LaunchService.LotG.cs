using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

// ===================== Lot G : plateforme de scripting (hooks paquets, team:runOnce, loadScript, état du groupe) =====================
// Tout ce qui est écrit ici depuis le thread réseau (état du groupe) ou le thread script (files) est concurrent-safe ; les
// files sont drainées par BotsViewModel.Tick (thread UI), seul autorisé à démarrer des hôtes / lire la bibliothèque planning.
public sealed partial class LaunchService
{
    /// <summary>Catalogue du protocole courant (lu par le thread script pour nommer les champs des paquets hookés). null =
    /// décodage désactivé (pas de session) -> les tables Lua n'ont que des clés f&lt;n&gt;.</summary>
    public ProtocolCatalog? Catalog => _catalog;

    // ---------------------------------------------------------------- team:runOnce (extrait Lua exécuté sur un membre)
    public readonly record struct TeamRunOnceRequest(uint FromPid, uint TargetPid, string Source);
    private readonly ConcurrentQueue<TeamRunOnceRequest> _teamRunOnce = new();
    private const int MaxRunOnceSource = 16384, MaxRunOnceQueue = 32;

    /// <summary>Enfile l'exécution d'un extrait Lua sur un membre d'équipe (hôte /cmd transitoire). Gaté Perm.Multi au POINT
    /// D'ACTION. false si refusé (droit, source vide/trop longue, file pleine).</summary>
    public bool EnqueueTeamRunOnce(uint fromPid, uint targetPid, string source)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Multi)) return false;
        if (string.IsNullOrWhiteSpace(source) || source.Length > MaxRunOnceSource || _teamRunOnce.Count >= MaxRunOnceQueue) return false;
        _teamRunOnce.Enqueue(new TeamRunOnceRequest(fromPid, targetPid, source));
        return true;
    }
    public bool TryDequeueTeamRunOnce(out TeamRunOnceRequest r) => _teamRunOnce.TryDequeue(out r);

    // ---------------------------------------------------------------- loadScript / reloadScript (bascule du script principal)
    public sealed record ScriptSwitchRequest(uint Pid, string? Name, TaskCompletionSource<bool> Done, CancellationToken Caller);
    private readonly ConcurrentQueue<ScriptSwitchRequest> _scriptSwitches = new();

    /// <summary>Demande de bascule du script principal de <paramref name="pid"/> (name null = relancer le script courant).
    /// Résolue par le VM au Tick (thread UI : la bibliothèque planning n'est pas thread-safe). Une demande dont le run appelant
    /// est arrêté (<paramref name="caller"/> annulé) ou que l'appelant a abandonnée (Done déjà résolu) est ignorée.</summary>
    public TaskCompletionSource<bool> EnqueueScriptSwitch(uint pid, string? name, CancellationToken caller)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (caller.IsCancellationRequested || _scriptSwitches.Count >= 16) { tcs.SetResult(false); return tcs; }
        _scriptSwitches.Enqueue(new ScriptSwitchRequest(pid, name, tcs, caller));
        return tcs;
    }
    public bool TryDequeueScriptSwitch(out ScriptSwitchRequest r) => _scriptSwitches.TryDequeue(out r!);

    // ---------------------------------------------------------------- état du GROUPE Dofus (party) par pid
    private sealed class PartyState
    {
        public long PartyId;
        public long LeaderId;
        public readonly ConcurrentDictionary<long, string> Members = new();
    }
    private readonly ConcurrentDictionary<uint, PartyState> _party = new();

    /// <summary>party_id du groupe de <paramref name="pid"/> (0 = pas de groupe connu).</summary>
    public long PartyIdOf(uint pid) => _party.TryGetValue(pid, out var s) ? s.PartyId : 0;

    /// <summary>true si le personnage <paramref name="characterId"/> est membre du groupe de <paramref name="pid"/>.</summary>
    public bool IsInParty(uint pid, long characterId)
        => characterId != 0 && _party.TryGetValue(pid, out var s) && s.PartyId != 0 && s.Members.ContainsKey(characterId);

    /// <summary>Membre d'équipe (pid connu) : par nom décodé, sinon par son identifiant de personnage (repli robuste si le nom
    /// des events de groupe est mal lu).</summary>
    public bool IsInParty(uint pid, uint memberPid, string name)
        => IsInPartyByName(pid, name) || IsInParty(pid, CharacterId(memberPid));

    /// <summary>Par nom : nom décodé du groupe, sinon membre d'équipe de ce nom -> son identifiant de personnage.</summary>
    public bool IsInParty(uint pid, string name)
    {
        if (IsInPartyByName(pid, name)) return true;
        var m = TeamMembers(pid).FirstOrDefault(x => string.Equals(x.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
        return m.Pid != 0 && IsInParty(pid, CharacterId(m.Pid));
    }

    private bool IsInPartyByName(uint pid, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !_party.TryGetValue(pid, out var s) || s.PartyId == 0) return false;
        string n = name.Trim();
        foreach (var kv in s.Members) if (string.Equals(kv.Value, n, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Membres du groupe de <paramref name="pid"/> : (id, nom, chef).</summary>
    public IReadOnlyList<(long Id, string Name, bool Leader)> PartyMembers(uint pid)
    {
        if (!_party.TryGetValue(pid, out var s) || s.PartyId == 0) return Array.Empty<(long, string, bool)>();
        long leader = s.LeaderId;
        return s.Members.Select(kv => (kv.Key, kv.Value, kv.Key == leader)).OrderBy(x => x.Item1).ToList();
    }

    // Thread réseau (OnDecoded). Clé = p.Name uniquement (serveur -> client) : confirmed.json ne renomme pas ces messages.
    // Chaque event porte un party_id : on n'applique que ceux du groupe suivi (un 2e groupe — arène/kolizéum — ne corrompt pas
    // l'état du groupe classique). id 0 (champ omis / illisible) = accepté (repli best-effort).
    private void OnPartyPacket(DecodedPacket p)
    {
        if (p.ClientToServer || p.Name is not { } name || !name.StartsWith("Party", StringComparison.Ordinal)) return;
        try
        {
            switch (name)
            {
                case "PartyJoinEvent":
                {
                    var (partyId, leaderId, members) = PartyReader.ReadJoin(p.Payload);
                    // Déjà dans un AUTRE groupe connu : on garde le premier (le groupe classique précède l'arène).
                    if (_party.TryGetValue(p.Pid, out var cur) && cur.PartyId != 0 && partyId != 0 && cur.PartyId != partyId) break;
                    var st = new PartyState { PartyId = partyId, LeaderId = leaderId };
                    foreach (var m in members) st.Members[m.Id] = m.Name;
                    _party[p.Pid] = st;
                    LogParty(p.Pid, st);
                    break;
                }
                case "PartyNewMemberEvent":
                case "PartyMemberUpdateEvent":
                {
                    var (partyId, member) = name == "PartyNewMemberEvent" ? PartyReader.ReadNewMember(p.Payload) : PartyReader.ReadMemberUpdate(p.Payload);
                    if (member is not { } m) break;
                    var st = _party.GetOrAdd(p.Pid, _ => new PartyState());
                    if (!Matches(st, partyId)) break;
                    if (st.PartyId == 0) st.PartyId = partyId;
                    // Mise à jour sans nom lisible : on garde le nom déjà connu.
                    st.Members[m.Id] = m.Name.Length > 0 ? m.Name : (st.Members.TryGetValue(m.Id, out var old) ? old : "");
                    if (name == "PartyNewMemberEvent") LogParty(p.Pid, st);
                    break;
                }
                case "PartyMemberRemoveEvent":
                {
                    var (partyId, leaving) = PartyReader.ReadMemberRemove(p.Payload);
                    if (leaving == 0 || !_party.TryGetValue(p.Pid, out var st) || !Matches(st, partyId)) break;
                    if (leaving == CharacterId(p.Pid)) { RemoveParty(p.Pid, st); break; }
                    st.Members.TryRemove(leaving, out _); LogParty(p.Pid, st);
                    break;
                }
                case "PartyLeaveEvent":
                case "PartyDeletedEvent":
                case "PartyKickedByEvent":
                {
                    long partyId = PartyReader.ReadPartyId(name, p.Payload);
                    if (_party.TryGetValue(p.Pid, out var st) && Matches(st, partyId)) RemoveParty(p.Pid, st);
                    break;
                }
                case "PartyLeaderUpdateEvent":
                {
                    var (partyId, leaderId) = PartyReader.ReadLeaderUpdate(p.Payload);
                    if (leaderId != 0 && _party.TryGetValue(p.Pid, out var st) && Matches(st, partyId)) st.LeaderId = leaderId;
                    break;
                }
            }
        }
        catch { /* best-effort : un event de groupe illisible ne casse jamais le décodage */ }
    }

    private static bool Matches(PartyState st, long partyId) => partyId == 0 || st.PartyId == 0 || st.PartyId == partyId;

    private void RemoveParty(uint pid, PartyState st)
    {
        // Retire CET état seulement (un Join concurrent plus récent n'est pas effacé).
        if (((ICollection<KeyValuePair<uint, PartyState>>)_party).Remove(new KeyValuePair<uint, PartyState>(pid, st)))
            TransportLog.Line($"[équipe] groupe pid {pid} : quitté");
    }

    private static void LogParty(uint pid, PartyState st)
        => TransportLog.Line($"[équipe] groupe pid {pid} : {st.Members.Count} membre(s)");

    private void ForgetLotG(uint pid)
    {
        _party.TryRemove(pid, out _);
        _nativeFollowLeader.TryRemove(pid, out _);
        _nativeFollowSent.TryRemove(pid, out _);
        _nativeFollowConfirmed.TryRemove(pid, out _);
    }
}
