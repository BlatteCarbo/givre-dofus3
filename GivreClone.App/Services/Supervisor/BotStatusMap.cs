namespace GivreClone.App.Services.Supervisor;

/// <summary>Traduit l'état interne d'un compte vers les statuts exposés aux scripts (offline, connecting, ingame, moving, gathering,
/// fight, dead, disconnected). Pur : ne lit rien d'autre que ses arguments.</summary>
public static class BotStatusMap
{
    public static string From(bool running, bool disconnectedHint, BotActivity activity)
    {
        if (!running) return disconnectedHint ? BotStatus.Disconnected : BotStatus.Offline;
        return activity switch
        {
            BotActivity.Offline or BotActivity.Connecting => BotStatus.Connecting,
            BotActivity.InGame => BotStatus.InGame,
            BotActivity.Moving => BotStatus.Moving,
            BotActivity.Gathering => BotStatus.Gathering,
            BotActivity.Combat => BotStatus.Fight,
            BotActivity.Dead => BotStatus.Dead,
            BotActivity.Disconnected => BotStatus.Disconnected,
            _ => BotStatus.InGame,
        };
    }

    /// <summary>Alias du compte exposé aux scripts : libellé, sinon dernier perso, sinon « compte-N » — JAMAIS l'identifiant de connexion.</summary>
    public static string Alias(string? label, string? character, string? lastCharacter, int index)
        => !string.IsNullOrWhiteSpace(label) ? label.Trim()
         : !string.IsNullOrWhiteSpace(character) ? character.Trim()
         : !string.IsNullOrWhiteSpace(lastCharacter) ? lastCharacter.Trim()
         : $"compte-{index + 1}";
}
