using System.Collections.Concurrent;

namespace GivreClone.App.Services.Combat;

/// <summary>Monstres à âme capturable d'après les données du jeu (mis en cache : la carte interroge ce test à chaque dessin).</summary>
public static class CapturableMonsters
{
    private static readonly ConcurrentDictionary<int, bool> Cache = new();

    public static bool Is(int monsterId)
        => monsterId > 0 && Cache.GetOrAdd(monsterId, id =>
        {
            try { return Stores.Launch.GameData?.MonsterCapturable(id) ?? false; }
            catch (System.Exception e) when (e is System.InvalidOperationException or Microsoft.Data.Sqlite.SqliteException) { return false; }
        });
}
