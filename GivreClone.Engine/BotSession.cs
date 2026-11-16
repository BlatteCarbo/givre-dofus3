using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreClone.Engine.Recovery;

namespace GivreClone.Engine;

/// <summary>
/// UNE connexion de jeu = UN compte = un contexte instancie (le remplacement du static Proxy/MessageHandler/
/// MainWindow de CoreMitm). Possede son bus, son etat, son emetteur d'actions, son correlateur et l'hote de
/// comportement. Recoit les CapturedMessage de GivreCore via Dispatch, resout le nom clair, replie l'etat et diffuse.
/// </summary>
public sealed class BotSession
{
    public int ConnectionId { get; }
    public uint? ProcessId { get; internal set; }
    public string? AccountId { get; internal set; }

    public PacketBus Bus { get; }
    public GameState State { get; }
    public RequestCorrelator Correlator { get; }
    public IActionSender Actions { get; }
    public IBotContext Context { get; }
    public BehaviorHost Behaviors { get; }
    /// <summary>
    /// Positive only after an accepted attributed ingress under the guarded
    /// session authority. A legacy or merely observed connection is NOT an
    /// authenticated session.
    /// </summary>
    public bool IsAuthenticated => _guarded?.Admission == MZP1Admission.Ready &&
        _lastGuardedCaptureAccepted;

    private readonly ProtocolCatalog _catalog;
    private readonly MZP1GuardedEngineWiring? _guarded;
    private bool _lastGuardedCaptureAccepted;

    public BotSession(int connectionId, ProtocolCatalog catalog, IFrameSink sink, IPacer pacer)
    {
        ConnectionId = connectionId;
        _catalog = catalog;
        Bus = new PacketBus();
        State = new GameState();
        Correlator = new RequestCorrelator();
        Actions = new ActionSender(catalog, sink, Bus, pacer, Correlator);
        Context = new BotContext(State, Actions, Bus);
        Behaviors = new BehaviorHost(Context);
    }

    /// <summary>
    /// Protected composition path: reuse the actual guarded context rather than
    /// constructing another (unguarded) ActionSender. An unverified identity
    /// produces a denied action sender, never an implicit legacy fallback.
    /// </summary>
    public BotSession(int connectionId, ProtocolCatalog catalog, MZP1GuardedEngineWiring guarded)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(guarded);
        if (guarded.ConnectionId != connectionId)
            throw new ArgumentException("Guarded session connection does not match the capture route.", nameof(guarded));
        ConnectionId = connectionId;
        _catalog = catalog;
        _guarded = guarded;
        Bus = guarded.Bus;
        State = guarded.State;
        Correlator = guarded.Correlator;
        Actions = guarded.Actions;
        Context = guarded.Context;
        Behaviors = guarded.Behaviors;
    }

    /// <summary>Traite un message capture par GivreCore : resout le nom clair, replie l'etat, diffuse sur le bus.</summary>
    public void Dispatch(CapturedMessage captured)
    {
        if (_guarded is not null)
        {
            _lastGuardedCaptureAccepted =
                _guarded.TryDispatchCaptured(captured) == MZP1CaptureResult.Accepted;
            return;
        }
        var name = _catalog.NameForCode(captured.Code);
        var message = new BotMessage(
            captured.Seq,
            captured.Direction,
            captured.ConnectionId,
            captured.Code,
            name,
            captured.Payload,
            captured.RawFrame);

        State.Apply(message);
        Bus.Publish(message);
    }
}
