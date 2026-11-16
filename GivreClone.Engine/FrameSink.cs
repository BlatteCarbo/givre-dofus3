using GivreCore.Mitm;

namespace GivreClone.Engine;

/// <summary>
/// Sortie d'injection d'une session : ecrit une frame GameMessage (deja encodee, NON prefixee) vers un sens.
/// Abstrait TcpProxyServer.Replay pour rendre l'ActionSender testable (un faux sink capture les octets).
/// </summary>
public interface IFrameSink
{
    /// <summary>Injecte une frame. Retourne false si la connexion n'est plus ecrivable.</summary>
    bool Send(Direction direction, byte[] gameMessageBytes);
}

/// <summary>Sink live : rejoue via GivreCore sur une connexion precise du proxy.</summary>
public sealed class ProxyFrameSink(TcpProxyServer proxy, int connectionId) : IFrameSink
{
    public int ConnectionId => connectionId;
    public bool Send(Direction direction, byte[] gameMessageBytes)
        => proxy.Replay(connectionId, direction, gameMessageBytes);
}
