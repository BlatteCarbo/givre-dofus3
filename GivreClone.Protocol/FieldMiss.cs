using System;
using System.Collections.Concurrent;

namespace GivreClone.Protocol;

/// <summary>
/// Champ de message ABSENT du mapping streamé. Le client ne fige AUCUN numéro de champ : un champ que le mapping ne connaît
/// pas n'est pas lu (numéro -1, qui ne correspond à aucun champ réel -> valeur par défaut), et l'absence est signalée UNE
/// fois par (message, champ) via <see cref="Log"/> (branché par l'app sur le journal de transport). Corriger = mettre à
/// jour le bundle de mapping, jamais le code.
/// </summary>
public static class FieldMiss
{
    /// <summary>Numéro renvoyé pour un champ inconnu du mapping : aucun champ protobuf réel ne le porte.</summary>
    public const int None = -1;

    private static readonly ConcurrentDictionary<string, byte> _reported = new();

    /// <summary>Sortie du signalement (ex : journal de transport). Null = silencieux (tests).</summary>
    public static Action<string>? Log { get; set; }

    /// <summary>Signale (une fois) le champ absent et renvoie <see cref="None"/>.</summary>
    public static int Of(string message, string field)
    {
        if (_reported.TryAdd(message + "." + field, 0))
            try { Log?.Invoke($"[mapping] champ absent du mapping : {message}.{field} -> lecture ignorée."); } catch { }
        return None;
    }
}
