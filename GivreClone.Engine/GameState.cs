using GivreClone.Protocol;

namespace GivreClone.Engine;

/// <summary>Vue LECTURE seule de l'etat de jeu d'une session (ce que voient les comportements).</summary>
public interface IGameStateView
{
    long? CharacterId { get; }
    string? CharacterName { get; }
    long? MapId { get; }
    int? CellId { get; }
    bool InFight { get; }
    bool IsMyTurn { get; }
}

/// <summary>
/// Etat de jeu materialise par SESSION, alimente en repliant les Events decodes (pattern DofusBridgeListener /
/// PacketAnalyser, mais instance-scoped). Phase 0 : squelette + quelques champs ; le fold complet
/// (carte/entites/inventaire/combat) arrive en Phase 2, pilote par les IMessageReader typees.
/// </summary>
public sealed class GameState : IGameStateView
{
    public long? CharacterId { get; private set; }
    public string? CharacterName { get; private set; }
    public long? MapId { get; private set; }
    public int? CellId { get; private set; }
    public bool InFight { get; private set; }
    public bool IsMyTurn { get; private set; }
    public bool MapKnown { get; private set; }
    public bool FightKnown { get; private set; }
    public bool CharacterKnown { get; private set; }
    public long? FightId { get; private set; }

    /// <summary>Replie un message dans l'etat. Extensible par nom clair (les readers typees viennent en Phase 2).</summary>
    public void Apply(BotMessage message)
    {
        // Intentional fail-closed behavior, rather than a switch with guessed
        // tags: 'Name' and raw bytes alone are not enough to certify a Dofus
        // 3.7.10.10 map, fight or character. An independently version-checked,
        // session-bound field reader must call the internal Set* methods after
        // a successful authenticated ingress. Until then UNKNOWN remains.
        _ = message;
    }

    /// <summary>
    /// Called ONLY by MZP1's independently guarded ingress, after admission,
    /// process identity and response verifier checks. Not available to a raw
    /// unverified BotSession.Dispatch or a test-supplied packet bus.
    /// </summary>
    internal void ApplyGuardedInbound(BotMessage message, ProtocolCatalog catalog)
    {
        if (MZP14VerifiedCharacterReader.TryRead(message, catalog, out var selected))
        {
            // Selection of a different character invalidates all world/combat
            // state of the previous character, even on the same connection.
            if (!CharacterKnown || CharacterId != selected!.Id)
            {
                MapId = null;
                CellId = null;
                MapKnown = false;
                InFight = false;
                IsMyTurn = false;
                FightKnown = false;
                FightId = null;
            }
            SetCharacter(selected!.Id, selected.Name);
            CharacterKnown = true;
        }
        if (MZP13VerifiedMapReader.TryRead(message, catalog, out long mapId))
        {
            // A new map invalidates the previous cell until a separately
            // admitted movement/placement event gives its actual position.
            if (!MapKnown || MapId != mapId) CellId = null;
            MapId = mapId;
            MapKnown = true;
        }
        if (MZP15VerifiedFightReader.TryRead(message, catalog, out var fight))
        {
            switch (fight!.Type)
            {
                case MZP15VerifiedFightReader.Transition.Start:
                    FightId = fight.FightId;
                    InFight = true;
                    FightKnown = true;
                    IsMyTurn = false; // turn owner not yet observed
                    break;
                case MZP15VerifiedFightReader.Transition.Turn:
                    // A stray turn event cannot manufacture an entire fight.
                    if (!FightKnown || !InFight) break;
                    // Without a selected, independently verified character,
                    // "my turn" is unknown, not a guessed yes.
                    if (!CharacterKnown || CharacterId is null) break;
                    IsMyTurn = fight.ActorId == CharacterId;
                    break;
                case MZP15VerifiedFightReader.Transition.End:
                    FightId = null;
                    InFight = false;
                    IsMyTurn = false;
                    FightKnown = true;
                    break;
            }
        }
    }

    /// <summary>Discard all session-derived information on revoke/logout.</summary>
    internal void ClearVerifiedSession()
    {
        CharacterId = null;
        CharacterName = null;
        MapId = null;
        CellId = null;
        InFight = false;
        IsMyTurn = false;
        MapKnown = false;
        FightKnown = false;
        FightId = null;
        CharacterKnown = false;
    }

    // Setters internes reserves aux readers de Phase 2 (evite d'exposer un setter public a tout le monde).
    internal void SetCharacter(long id, string name) { CharacterId = id; CharacterName = name; }
    internal void SetPosition(long mapId, int cellId) { MapId = mapId; CellId = cellId; }
    internal void SetFight(bool inFight, bool myTurn) { InFight = inFight; IsMyTurn = myTurn; }
}
