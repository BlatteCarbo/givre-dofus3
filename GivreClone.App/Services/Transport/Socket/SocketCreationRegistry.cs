using System;
using System.Collections.Concurrent;

namespace GivreClone.App.Services.Transport.Socket;

/// <summary>Création de personnage demandée pour un compte (clé = login), consommée UNE fois par la prochaine session de jeu :
/// une reconnexion ne recrée jamais un personnage.</summary>
public static class SocketCreationRegistry
{
    private static readonly ConcurrentDictionary<string, CharacterCreationSpec> Pending = new(StringComparer.OrdinalIgnoreCase);
    public static void Set(string login, CharacterCreationSpec spec) => Pending[login] = spec;
    public static CharacterCreationSpec? Take(string login) => Pending.TryRemove(login, out var s) ? s : null;
    public static bool Has(string login) => Pending.ContainsKey(login);
    public static void Clear(string login) => Pending.TryRemove(login, out _);
}
