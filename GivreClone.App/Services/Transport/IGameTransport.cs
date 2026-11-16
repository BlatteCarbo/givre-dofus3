using System;

namespace GivreClone.App.Services.Transport;

/// <summary>
/// Transport de jeu d'un mode (MITM aujourd'hui, Socket ensuite). Tout ce qui est au-dessus (forge, Lua, combat, planning)
/// ne voit que ces deux primitives, clé = pid du bot (le process Dofus.exe en MITM).
/// </summary>
public interface IGameTransport
{
    /// <summary>Nom court du mode, pour les journaux ("mitm", "socket").</summary>
    string Name { get; }

    /// <summary>Envoie au SERVEUR une frame déjà préfixée (varint(len)+GameMessage) pour le bot <paramref name="pid"/>.
    /// Retourne false si rien n'a été écrit (aucune connexion active).</summary>
    bool Inject(uint pid, byte[] framedToServer);

    /// <summary>Tap de réception (pid, clientToServer, octets) vers le décodeur. null = plus d'observation (kill-switch).
    /// Les frames envoyées par <see cref="Inject"/> n'y repassent JAMAIS : le décodeur prend tout clientToServer pour du
    /// trafic du vrai client (ex. un jrw C->S = déplacement MANUEL). Un transport trace ses envois via SocketLog.Tx.</summary>
    Action<uint, bool, byte[]>? OnData { get; set; }

    /// <summary>Fermeture d'une connexion du transport : (pid, wasCurrent = c'était la connexion d'injection du pid,
    /// serverClosedFirst = fermée d'abord côté serveur). null = pas d'écoute. Purement passif.</summary>
    Action<uint, bool, bool>? OnClosed { get; set; }
}
