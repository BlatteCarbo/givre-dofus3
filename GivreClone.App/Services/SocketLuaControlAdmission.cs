using System;

namespace GivreClone.App.Services;

/// <summary>
/// A Socket bot has a synthetic PID. "Running" on an account row or a decoded
/// map never proves a writable game transport. This is a pure admission policy
/// shared by Lua's manual/web/resume pathways; true server ACKs remain per-action
/// responsibilities of the socket transport, never fabricated here.
/// </summary>
public sealed record SocketLuaControlSnapshot(
    bool IsSocketSession,
    bool AccountRunning,
    bool SessionParked,
    bool SessionBackoff,
    bool Reconnecting,
    bool WaitingForNewSession,
    bool HasServerMap,
    bool InjectorRegistered,
    string? ControlState);

public static class SocketLuaControlAdmission
{
    /// <summary>Null admits an attempt to start Lua, NOT a successful game action.</summary>
    public static string? StartBlockReason(SocketLuaControlSnapshot input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.IsSocketSession) return null; // no MITM behavior change
        if (!input.AccountRunning)
            return "Session Socket hors ligne : aucun script ne peut agir.";
        if (input.SessionParked || input.SessionBackoff || input.Reconnecting ||
            input.WaitingForNewSession || input.ControlState == "suspended")
            return "Session Socket déconnectée ou en reconnexion : script en attente.";
        if (!input.HasServerMap || input.ControlState == "awaiting-map")
            return "Carte serveur non reçue : script Socket en attente.";
        if (!input.InjectorRegistered || input.ControlState == "observation-only")
            return "Socket connecté en observation seulement : injecteur de commandes absent.";
        if (input.ControlState != "channel-registered")
            return "Canal de commandes Socket indisponible (droits, mapping ou session).";
        return null;
    }
}
