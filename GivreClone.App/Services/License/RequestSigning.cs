using System;
using System.Text.Json;
using System.Threading;

namespace GivreClone.App.Services.License;

/// <summary>Horloge SERVEUR estimée : horloge locale + un décalage (0 par défaut), recalé UNIQUEMENT par une réponse signée
/// « dérive d'horloge » à NOTRE requête (nonce en écho). Sert au server_time des requêtes/envois ET au contrôle de fraîcheur des
/// réponses (±5 min) : un PC dont l'horloge a dérivé en cours de session continue de fonctionner après un recalage.</summary>
internal static class ServerClock
{
    private static long _offsetMs;

    public static long OffsetMs => Interlocked.Read(ref _offsetMs);

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + OffsetMs;

    public static void Calibrate(long serverTimeMs) =>
        Interlocked.Exchange(ref _offsetMs, serverTimeMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}

/// <summary>Signature des requêtes /auth et /mapping, annoncée par le serveur dans la réponse SIGNÉE de /version
/// (<c>client_signing</c>). Champ présent (quelle que soit sa valeur) : on signe ; absent : en clair, comme 1.1.x. COLLANT pour
/// le processus : une fois vu, jamais de retour en clair (ni après un refus) ; un retour arrière serveur s'applique au lancement
/// suivant (rien n'est écrit sur disque).</summary>
internal static class RequestSigning
{
    private static volatile bool _enabled;
    private static int _modeCode = -1;

    public static bool Enabled => _enabled;

    /// <summary>Lit <c>client_signing</c> dans le payload VÉRIFIÉ de /version (démarrage + veille 45 s).</summary>
    public static void Note(JsonElement versionPayload)
    {
        if (versionPayload.ValueKind != JsonValueKind.Object || !versionPayload.TryGetProperty("client_signing", out var v)) return;
        string mode = v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        int code = mode switch { "optional" => 1, "mapping" => 2, "all" => 3, _ => 9 };
        _enabled = true;
        if (Interlocked.Exchange(ref _modeCode, code) != code) Diag.Log(502, code);
    }

    /// <summary>Le serveur a refusé une requête EN CLAIR (SIGNATURE_REJECTED « unsigned ») avant qu'on ait vu l'annonce (réglage
    /// passé de Off à mapping/all pendant que l'app tournait) : on passe en signé, de façon collante. Jamais l'inverse.</summary>
    public static void EnableFromRefusal()
    {
        _enabled = true;
        if (Interlocked.Exchange(ref _modeCode, 8) != 8) Diag.Log(502, 8);
    }
}
