using System.Collections.Generic;

namespace GivreClone.Protocol;

/// <summary>
/// Coupe-circuit des déplacements REFUSÉS par le serveur, hors combat. Un même déplacement (carte, départ, arrivée) refusé
/// <see cref="Threshold"/> fois en <see cref="WindowMs"/> n'est plus émis pendant <see cref="CooldownMs"/> : le moteur répond
/// « refusé » sans envoyer de trame. Journal du 24/10 : un script de minage renvoyait 256->231 toutes les 200 ms, refusé à
/// chaque fois (≈ 28 000 refus en une session) — spam serveur risqué pour le compte et boucle sans issue pour le script.
/// Non thread-safe : l'appelant sérialise (verrou de dispatch des déplacements).
/// </summary>
public sealed class MoveRefusalBreaker
{
    public const int Threshold = 3;
    public const long WindowMs = 60_000;
    public const long CooldownMs = 60_000;

    private readonly Dictionary<(long Map, int From, int To), (int Count, long FirstAt, long OpenUntil)> _state = new();

    /// <summary>Ce déplacement peut-il être émis maintenant ?</summary>
    public bool Allows(long map, int from, int to, long now) =>
        !_state.TryGetValue((map, from, to), out var s) || now >= s.OpenUntil;

    /// <summary>Refus serveur du déplacement (map, from, to). Vrai si le coupe-circuit vient de s'ouvrir.</summary>
    public bool NoteRefused(long map, int from, int to, long now)
    {
        var key = (map, from, to);
        if (!_state.TryGetValue(key, out var s) || now - s.FirstAt > WindowMs || (s.OpenUntil > 0 && now >= s.OpenUntil))
            s = (0, now, 0);
        s.Count++;
        bool opened = s.Count >= Threshold && s.OpenUntil <= now;
        if (opened) s.OpenUntil = now + CooldownMs;
        _state[key] = s;
        return opened;
    }

    /// <summary>Déplacement abouti : l'historique de refus de ce trajet est effacé.</summary>
    public void NoteArrived(long map, int from, int to) => _state.Remove((map, from, to));

    public void Clear() => _state.Clear();
}
