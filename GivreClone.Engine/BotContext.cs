namespace GivreClone.Engine;

/// <summary>
/// LE seam testable remis aux comportements : etat (lecture), actions (controle), et attente de paquet.
/// Un comportement ne touche JAMAIS une socket ni GivreCore : il lit State, decide, appelle Actions.
/// Un FakeBotContext (frames scriptees) rend tout comportement testable sans jeu.
/// </summary>
public interface IBotContext
{
    IGameStateView State { get; }
    IActionSender Actions { get; }
    Task<BotMessage> WaitForAsync(Func<BotMessage, bool> predicate, TimeSpan timeout, CancellationToken ct = default);
}

/// <summary>Contexte live : vue sur l'etat + actions + bus d'une session.</summary>
public sealed class BotContext(IGameStateView state, IActionSender actions, IPacketBus bus) : IBotContext
{
    public IGameStateView State => state;
    public IActionSender Actions => actions;

    public Task<BotMessage> WaitForAsync(Func<BotMessage, bool> predicate, TimeSpan timeout, CancellationToken ct = default)
        => bus.WaitForAsync(predicate, timeout, ct);
}
