using GivreClone.Engine;
using GivreClone.Protocol;

namespace GivreClone.Tests;

/// <summary>Etat de jeu scriptable pour les tests (aucun jeu requis).</summary>
public sealed class FakeGameState : IGameStateView
{
    public long? CharacterId { get; set; }
    public string? CharacterName { get; set; }
    public long? MapId { get; set; }
    public int? CellId { get; set; }
    public bool InFight { get; set; }
    public bool IsMyTurn { get; set; }
}

/// <summary>IActionSender qui ENREGISTRE les intentions au lieu d'injecter (verifie ce qu'un comportement fait).</summary>
public sealed class FakeActionSender : IActionSender
{
    public List<string> Sent { get; } = new();

    public Task<ActionResult> SendRequestAsync(string clearName, Action<PayloadBuilder> build, CancellationToken ct = default)
    {
        Sent.Add(clearName);
        return Task.FromResult(new ActionResult(SendOutcome.Sent, clearName, 1, null));
    }

    public Task<BotMessage> WaitForEventAsync(string clearEventName, TimeSpan timeout, CancellationToken ct = default)
        => Task.FromException<BotMessage>(new TimeoutException());
}

/// <summary>Contexte bot factice : etat scriptable + actions enregistrees + un vrai bus alimentable par le test.</summary>
public sealed class FakeBotContext : IBotContext
{
    public FakeGameState FakeState { get; } = new();
    public FakeActionSender FakeActions { get; } = new();
    public PacketBus Bus { get; } = new();

    public IGameStateView State => FakeState;
    public IActionSender Actions => FakeActions;

    public Task<BotMessage> WaitForAsync(Func<BotMessage, bool> predicate, TimeSpan timeout, CancellationToken ct = default)
        => Bus.WaitForAsync(predicate, timeout, ct);
}
