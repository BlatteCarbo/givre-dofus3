using System;
using System.Collections.Concurrent;

namespace GivreClone.App.Services.Transport.Socket;

/// <summary>Suppression demandée : personnage visé (nom exact) et réponse à la question secrète du compte.</summary>
public sealed record CharacterDeletionSpec(string CharacterName, string Answer);

/// <summary>Suppression de personnage demandée pour un compte (clé = login), consommée UNE fois par la prochaine session de jeu :
/// une reconnexion ne supprime jamais rien.</summary>
public static class SocketDeletionRegistry
{
    private static readonly ConcurrentDictionary<string, CharacterDeletionSpec> Pending = new(StringComparer.OrdinalIgnoreCase);
    public static void Set(string login, CharacterDeletionSpec spec) => Pending[login] = spec;
    public static CharacterDeletionSpec? Take(string login) => Pending.TryRemove(login, out var s) ? s : null;
    public static void Clear(string login) => Pending.TryRemove(login, out _);
}
