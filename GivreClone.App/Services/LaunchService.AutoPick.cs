using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GivreClone.Engine;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    // Sélection du personnage À LA PLACE DU CLIC (MITM), uniquement sur demande explicite d'un ordre (MCP/superviseur).
    // Sans demande, rien ne change : l'utilisateur clique lui-même comme avant.
    private readonly GivreClone.Engine.CharacterSelectionQueue _autoPick = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, long> _clientSelectedAt = new();

    public IDisposable ExpectSessionCharacter(GivreClone.Engine.SessionIdentity target) => _autoPick.Expect(target);

    /// <summary>Arms auto-pick on an already running client (same pid). The target is copied into the request.</summary>
    public void BindSessionCharacter(uint pid, GivreClone.Engine.SessionIdentity target)
    {
        using (ExpectSessionCharacter(target))
            _autoPick.Bind(pid, target.Character);
    }

    /// <summary>Empty ReloginRequest on the live MITM relay. Socket pids are refused.</summary>
    public bool InjectReloginRequest(uint pid)
    {
        if (pid == 0 || IsSocketPid(pid)) return false;
        byte[] packet = MoveForge.BuildReloginFramed();
        if (packet.Length == 0)
        {
            TransportLog.Line(pid, "[jeu] ReloginRequest non forgé (mapping) — relance de repli");
            return false;
        }
        bool ok = InjectForge(pid, packet);
        TransportLog.Line(pid, $"[jeu] pid {pid} : ReloginRequest {(ok ? "injecté" : "NON injecté")} (0 champ, même process)");
        return ok;
    }

    // The client needs time to finish loading its character selection screen.
    private static int AutoPickDelayMs => int.TryParse(Environment.GetEnvironmentVariable("GIVRE_AUTOPICK_MS"), out var v) && v >= 0 ? v : 25000;

    private void TryAutoPickCharacter(uint pid, IReadOnlyList<(long Id, CharacterInfo Info)> all)
    {
        if (!_autoPick.TryClaim(pid, out var request)) return;
        string who = request.Character;
        long? id = request.Target is { } target
            ? all.Where(c => c.Id == target.CharacterId && string.Equals(c.Info?.Name, target.Character, StringComparison.Ordinal))
                .Select(c => (long?)c.Id).SingleOrDefault()
            : CharacterPick.Pick(all.Select(c => (c.Id, c.Info?.Name, (int)(c.Info?.Level ?? 0))).ToList(), who);
        if (id is null) { _autoPick.Complete(request); TransportLog.Line($"[perso] pid {pid} : sélection automatique « {who} » : introuvable dans la liste, rien envoyé"); return; }
        long claimedAt = Environment.TickCount64;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(AutoPickDelayMs).ConfigureAwait(false);
                if (!_autoPick.IsCurrent(request)) return; // client stopped, replaced, or PID reused
                if (request.Target is { } expected && ServerIdOf(pid) is > 0 and var server && server != expected.ServerId) return;
                bool clientChose = ClientSelectionInFlight(pid, claimedAt);
                bool playing = CharacterAlreadyPlaying(pid, request);
                var engaged = NativeTurboRuntime.EngagementOf((int)pid);
                bool nativeBusy = engaged.Host || engaged.SpeedHack;
                if (!CharacterAutoPick.ShouldInject(clientChose, playing, nativeBusy))
                {
                    TransportLog.Line(nativeBusy
                        ? $"[perso] pid {pid} : sélection automatique non injectée (hôte natif ou speedhack encore engagé)"
                        : clientChose
                        ? $"[perso] pid {pid} : le client a déjà envoyé sa sélection, sélection automatique non injectée"
                        : $"[perso] pid {pid} : perso déjà sélectionné, sélection automatique inutile");
                    return;
                }
                MarkCharacterLoading(pid);
                bool ok = InjectForge(pid, MoveForge.BuildCharacterSelectFramed(id.Value));
                TransportLog.Line($"[perso] pid {pid} : sélection automatique « {who} » -> id {id} {(ok ? "envoyée" : "NON envoyée (relais indisponible)")}");
            }
            finally { _autoPick.Complete(request); }
        });
    }

    private bool ClientSelectionInFlight(uint pid, long claimedAt)
        => _clientSelectedAt.TryGetValue(pid, out var at) && at >= claimedAt;

    // An explicit target still needs CharacterSelectionRequest while the client is on the selection
    // screen. A remembered character id, or the single-character list shortcut, is not "already playing".
    private bool CharacterAlreadyPlaying(uint pid, GivreClone.Engine.CharacterSelectionQueue.Request request)
    {
        if (request.Target is { } aimed)
            return _charId.TryGetValue(pid, out long have) && have == aimed.CharacterId && CurrentMap(pid) is > 0;
        return _charId.ContainsKey(pid);
    }
}
