using GivreClone.Protocol;
using GivreCore.Mitm;

namespace GivreClone.Engine;

public enum SendOutcome
{
    Sent,               // frame injectee
    NotMapped,          // aucun code obfusque connu pour ce nom clair (matching a regenerer)
    NoSchema,           // pas de definition .proto pour ce message (proto a rafraichir)
    ConnectionClosed,   // la connexion n'est plus ecrivable
}

public sealed record ActionResult(SendOutcome Outcome, string ClearName, int Uid, byte[]? Frame)
{
    public bool Sent => Outcome == SendOutcome.Sent;
}

/// <summary>
/// Surface de CONTROLE : intentions (deplacer / lancer / recolter / utiliser / finir le tour / parler) forgees
/// via EnvelopeBuilder et injectees via le sink, avec cadence humaine. C'est le seam pour les comportements :
/// un faux IActionSender rend chaque comportement testable sans jeu.
/// </summary>
public interface IActionSender
{
    /// <summary>Forge et emet une Request par NOM CLAIR, en construisant son payload par nom de champ.</summary>
    Task<ActionResult> SendRequestAsync(string clearName, Action<PayloadBuilder> build, CancellationToken ct = default);

    /// <summary>Attend un Event confirmant (par nom clair) apres une action.</summary>
    Task<BotMessage> WaitForEventAsync(string clearEventName, TimeSpan timeout, CancellationToken ct = default);
}

/// <summary>Implementation live : catalogue -&gt; forge -&gt; pacing -&gt; injection -&gt; (attente sur le bus).</summary>
public sealed class ActionSender(
    ProtocolCatalog catalog,
    IFrameSink sink,
    IPacketBus bus,
    IPacer pacer,
    RequestCorrelator correlator) : IActionSender
{
    public async Task<ActionResult> SendRequestAsync(string clearName, Action<PayloadBuilder> build, CancellationToken ct = default)
    {
        var code = catalog.CodeForName(clearName);
        if (code is null) return new ActionResult(SendOutcome.NotMapped, clearName, 0, null);
        var def = catalog.Message(clearName);
        if (def is null) return new ActionResult(SendOutcome.NoSchema, clearName, 0, null);

        var pb = new PayloadBuilder(def);
        build(pb);
        var payload = pb.Build();

        await pacer.BeforeActionAsync(ct).ConfigureAwait(false);
        int uid = correlator.NextUid();
        var frame = EnvelopeBuilder.BuildClientRequest(code, payload, uid);
        bool ok = sink.Send(Direction.ClientToServer, frame);
        return new ActionResult(ok ? SendOutcome.Sent : SendOutcome.ConnectionClosed, clearName, uid, frame);
    }

    public Task<BotMessage> WaitForEventAsync(string clearEventName, TimeSpan timeout, CancellationToken ct = default)
        => bus.WaitForAsync(m => m.Is(clearEventName), timeout, ct);
}

/// <summary>
/// Sucre d'intention au-dessus de SendRequestAsync : chaque helper connait le NOM CLAIR du message et ses
/// champs (map_id, spell_id, cell...). Extension methods -&gt; marchent aussi sur un faux IActionSender.
/// Les noms de champ suivent clear_game_2025.proto.
/// </summary>
public static class ActionSenderExtensions
{
    public static Task<ActionResult> MoveToCellAsync(this IActionSender a, long mapId, IEnumerable<int> keyCells, bool cautious = false, CancellationToken ct = default)
        => a.SendRequestAsync("MapMovementRequest", pb => pb.Int("map_id", mapId).Bool("cautious", cautious).Ints("key_cells", keyCells), ct);

    public static Task<ActionResult> CastSpellAsync(this IActionSender a, int spellId, int cell, CancellationToken ct = default)
        => a.SendRequestAsync("GameActionFightCastRequest", pb => pb.Int("spell_id", spellId).Int("cell", cell), ct);

    public static Task<ActionResult> CastOnTargetAsync(this IActionSender a, long targetId, int spellId, CancellationToken ct = default)
        => a.SendRequestAsync("GameActionFightCastOnTargetRequest", pb => pb.Int("target_id", targetId).Int("spell_id", spellId), ct);

    public static Task<ActionResult> HarvestAsync(this IActionSender a, int elementId, int skillInstanceUid, CancellationToken ct = default)
        => a.SendRequestAsync("InteractiveUseRequest", pb => pb.Int("skill_instance_uid", skillInstanceUid).Int("element_id", elementId), ct);

    public static Task<ActionResult> UseObjectAsync(this IActionSender a, long objectUid, CancellationToken ct = default)
        => a.SendRequestAsync("ObjectUseRequest", pb => pb.Int("object_uid", objectUid), ct);

    public static Task<ActionResult> EndTurnAsync(this IActionSender a, CancellationToken ct = default)
        => a.SendRequestAsync("FightTurnFinishRequest", pb => pb.Bool("is_afk", false), ct);

    public static Task<ActionResult> SendChatAsync(this IActionSender a, int channel, string text, CancellationToken ct = default)
        => a.SendRequestAsync("ChatChannelMessageRequest", pb => pb.Int("channel", channel).Str("content", text), ct);
}
