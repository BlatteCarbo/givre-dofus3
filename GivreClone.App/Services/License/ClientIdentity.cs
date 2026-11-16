using System;
using System.Security.Cryptography;
using System.Text;
using GivreClone.Engine.Secrets;

namespace GivreClone.App.Services.License;

/// <summary>Paire Ed25519 CLIENTE, une par utilisateur Windows (graine protégée DPAPI CurrentUser dans les réglages, conservée
/// d'une session à l'autre). Signe les envois WS (bots, pm, arch, accusés) et, quand le serveur l'annonce, les requêtes
/// /auth et /mapping. Doit rester STABLE : une autre paire = une autre identité pour le serveur (session liée à la clé).</summary>
internal static class ClientIdentity
{
    private static readonly object _lock = new();
    // RÉFÉRENCE (lecture atomique) et non un tuple nullable : la copie d'une structure de 3 champs hors verrou pouvait être
    // déchirée (HasValue vrai, clés encore nulles) entre deux premiers appels simultanés.
    private sealed class Pair(byte[] sk, byte[] pk) { public readonly byte[] Sk = sk, Pk = pk; }
    private static volatile Pair? _cache;

    public static (byte[] Sk, byte[] Pk) Get()
    {
        if (_cache is { } c) return (c.Sk, c.Pk);
        // Verrou : deux premiers appels concurrents (télémétrie + /auth) tiraient chacun une graine -> paire en mémoire ≠ paire
        // sur disque, et une session liée à l'une refusait l'autre.
        lock (_lock)
        {
            if (_cache is { } c2) return (c2.Sk, c2.Pk);
            byte[] seed = Array.Empty<byte>();
            var stored = AppConfig.Current.ClientSigningKey;
            if (!string.IsNullOrEmpty(stored) && Dpapi.Unprotect(stored) is { Length: > 0 } clear)
                try { seed = Convert.FromBase64String(clear); } catch { seed = Array.Empty<byte>(); }
            if (seed.Length != 32)
            {
                seed = RandomNumberGenerator.GetBytes(32);
                AppConfig.Current.ClientSigningKey = Dpapi.Protect(Convert.ToBase64String(seed));
                AppConfig.Save();
            }
            var pair = new Pair(seed, Cryptographic.Ed25519.PublicKey(seed));
            _cache = pair;
            return (pair.Sk, pair.Pk);
        }
    }

    /// <summary>Clé publique en hexadécimal minuscule (64 caractères).</summary>
    public static string PubHex => Convert.ToHexString(Get().Pk).ToLowerInvariant();

    /// <summary>Empreinte (support) : 16 premiers hexa de SHA-256 sur les 64 caractères ASCII de la clé publique en hexa minuscule
    /// — la même formule que le serveur (key_fingerprint) et que la fiche licence de l'admin.</summary>
    public static string FingerprintOf(string pubHexLower) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(pubHexLower)))[..16].ToLowerInvariant();

    public static string Fingerprint => FingerprintOf(PubHex);

    /// <summary>Signature Ed25519 PURE (64 octets) des octets exacts fournis.</summary>
    public static byte[] Sign(byte[] data)
    {
        var (sk, pk) = Get();
        return Cryptographic.Ed25519.Signature(data, sk, pk);
    }
}
