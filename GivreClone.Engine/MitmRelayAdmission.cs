using System.Collections.Generic;

namespace GivreClone.Engine;

/// <summary>
/// Which process a relay socket belongs to, and whether it is the connection server.
/// Unarmed, the first socket of an OS pid is the connection server and every later one is the game server.
/// After <see cref="Arm"/>, the next socket is the connection server again (a relogin), even if that pid
/// was already seen, and it does not replace the game inject target. An unknown handshake pid is adopted
/// onto the single armed account so the same row sees the character list. Zero or two armed accounts
/// refuse an unknown pid. A known pid the application does not own is refused with no state change.
/// </summary>
public sealed class MitmRelayAdmission
{
    public readonly record struct Decision(uint OwnerPid, bool Admit, bool ConnectionServer, bool Adopted);

    private readonly object _gate = new();
    private readonly HashSet<uint> _armed = new();
    private readonly HashSet<uint> _awaitingFresh = new();
    private readonly HashSet<uint> _freshConsumed = new();
    private readonly HashSet<uint> _gameReplaced = new();
    private readonly HashSet<uint> _processSeen = new();
    private readonly Dictionary<uint, uint> _adopted = new();

    public void Arm(uint pid)
    {
        if (pid == 0) return;
        lock (_gate)
        {
            _armed.Add(pid);
            _awaitingFresh.Add(pid);
            _freshConsumed.Remove(pid);
            _gameReplaced.Remove(pid);
            DropAdoptions(pid);
        }
    }

    public void Disarm(uint pid)
    {
        lock (_gate)
        {
            _armed.Remove(pid);
            _awaitingFresh.Remove(pid);
            _freshConsumed.Remove(pid);
            _gameReplaced.Remove(pid);
            DropAdoptions(pid);
        }
    }

    /// <summary>
    /// The original game socket closed. Re-arm the fresh classification only if the connection-server
    /// hop has not already been consumed, so the following game socket stays the inject target.
    /// </summary>
    public void NoteLinkClosed(uint ownerPid)
    {
        lock (_gate)
        {
            if (!_armed.Contains(ownerPid) || _freshConsumed.Contains(ownerPid)) return;
            _awaitingFresh.Add(ownerPid);
        }
    }

    public bool GameSocketReplaced(uint pid)
    {
        lock (_gate) return _gameReplaced.Contains(pid);
    }

    public bool WouldAdmit(uint handshakePid, bool known, bool owned)
    {
        lock (_gate) return AdmitUnlocked(handshakePid, known, owned);
    }

    /// <summary>Account pid a socket would be attributed to. Does not consume the fresh hop.</summary>
    public uint ResolveOwner(uint handshakePid, bool known)
    {
        lock (_gate)
        {
            if (known) return handshakePid;
            if (_adopted.TryGetValue(handshakePid, out uint adopted)) return adopted;
            uint? sole = SoleArmed();
            return sole ?? handshakePid;
        }
    }

    public Decision Commit(uint handshakePid, bool known, bool owned)
    {
        lock (_gate)
        {
            if (!AdmitUnlocked(handshakePid, known, owned))
                return new Decision(handshakePid, false, false, false);

            uint owner = handshakePid;
            bool adopted = false;
            if (!known)
            {
                if (!_adopted.TryGetValue(handshakePid, out owner))
                {
                    owner = SoleArmed() ?? handshakePid;
                    _adopted[handshakePid] = owner;
                }
                adopted = true;
            }

            bool connectionServer;
            if (_awaitingFresh.Remove(owner))
            {
                connectionServer = true;
                _freshConsumed.Add(owner);
                _processSeen.Add(handshakePid);
            }
            else
                connectionServer = _processSeen.Add(handshakePid);

            if (_armed.Contains(owner) && !connectionServer)
                _gameReplaced.Add(owner);
            return new Decision(owner, true, connectionServer, adopted);
        }
    }

    private bool AdmitUnlocked(uint handshakePid, bool known, bool owned)
    {
        if (known) return owned;
        if (_adopted.ContainsKey(handshakePid)) return true;
        return _armed.Count == 1;
    }

    private uint? SoleArmed()
    {
        if (_armed.Count != 1) return null;
        foreach (uint pid in _armed) return pid;
        return null;
    }

    private void DropAdoptions(uint owner)
    {
        var drop = new List<uint>();
        foreach (var kv in _adopted)
            if (kv.Value == owner) drop.Add(kv.Key);
        foreach (uint key in drop) _adopted.Remove(key);
    }
}
