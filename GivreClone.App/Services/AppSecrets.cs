using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace GivreClone.App.Services;

/// <summary>
/// Secrets embarques du client de licence Ikuware (URL de base + cle publique Ed25519), obfusques par
/// XOR roulant pour ne PAS apparaitre en clair dans un dump `strings`. La routine <see cref="R"/> et la
/// cle <see cref="K"/> sont a VIRTUALISER sous VMProtect en prod (equivalent C# d'un VMProtectDecryptString).
///
/// Ni l'URL ni la cle publique ne sont "secretes" (l'URL transite en clair, la cle est PUBLIQUE) — la
/// cle publique reste le point d'ancrage de confiance : c'est elle, pas TLS, qui authentifie l'enveloppe
/// signee. L'obfuscation ne sert qu'a compliquer le repiquage/patch du binaire.
///
/// Ordre de resolution : variable d'environnement (dev/test) > valeur embarquee > defaut dev.
/// L'operateur fournit l'URL de prod + les 64 hex de server/keys/ed25519_public.hex ; on les bake ici
/// via <c>AppSecrets.Enc("…")</c> (build DEBUG) en remplacant les tableaux placeholder ci-dessous.
/// </summary>
internal static class AppSecrets
{
    // Cle XOR (a virtualiser/masquer par VMProtect).
    private static readonly byte[] K =
        { 0x3A, 0xC8, 0x91, 0x4F, 0xD2, 0x0B, 0x77, 0xA6, 0x1E, 0x5C, 0xE9, 0x84, 0x30, 0xBD, 0x62, 0xF1 };

    [MethodImpl(MethodImplOptions.NoInlining)]   // fonction distincte => selectionnable dans VMProtect
    private static string R(byte[] e)
    {
        var p = new byte[e.Length];
        for (int i = 0; i < e.Length; i++)
            p[i] = (byte)(e[i] ^ K[i % K.Length] ^ ((i * 27 + 165) & 0xFF));
        return Encoding.UTF8.GetString(p);
    }

    // Valeurs EMBARQUEES (encodees via Enc, XOR roulant). BAKÉ PROD (2026-08-24).
    private static readonly byte[] BaseUrlEnc = System.Array.Empty<byte>();
    private static readonly byte[] PublicKeyHexEnc = System.Array.Empty<byte>();

    // Defaut de dev : serveur Ikuware local (clients/csharp/gui utilise la meme valeur de repli).
    private const string DevBaseUrl = "http://127.0.0.1:4000/api/v1/client";

    /// <summary>URL de base du client. PROD = valeur BAKÉE uniquement. L'override par variable d'environnement
    /// GIVRE_BASE_URL n'est autorisé qu'en DEBUG (dev/test) — sinon un tiers pourrait rediriger le client.</summary>
    public static string BaseUrl
    {
        get
        {
#if DEBUG
            var env = Environment.GetEnvironmentVariable("GIVRE_BASE_URL");
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
#endif
            var baked = BaseUrlEnc.Length > 0 ? R(BaseUrlEnc) : "";
            return string.IsNullOrWhiteSpace(baked) ? DevBaseUrl : baked;
        }
    }

    /// <summary>Cle publique Ed25519 (64 hex), ancre de confiance. PROD = valeur BAKÉE uniquement. L'override
    /// GIVRE_SERVER_PUBKEY n'est autorisé qu'en DEBUG : en Release, impossible de substituer la clé (donc de
    /// pointer un serveur pirate qui forgerait des enveloppes signées). Vide = non configure.</summary>
    public static string ServerPublicKeyHex
    {
        get
        {
#if DEBUG
            var env = Environment.GetEnvironmentVariable("GIVRE_SERVER_PUBKEY");
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
#endif
            return PublicKeyHexEnc.Length > 0 ? R(PublicKeyHexEnc) : "";
        }
    }

    /// <summary>true quand une cle publique 64-hex exploitable est disponible (embarquee ou env).</summary>
    public static bool IsConfigured => ServerPublicKeyHex.Length == 64;

#if DEBUG
    /// <summary>DEV : renvoie le <c>new byte[]{…}</c> encode d'une valeur (pour baker un secret ci-dessus).</summary>
    public static string Enc(string s)
    {
        var p = Encoding.UTF8.GetBytes(s);
        var sb = new StringBuilder("new byte[]{");
        for (int i = 0; i < p.Length; i++)
            sb.Append("0x").Append(((byte)(p[i] ^ K[i % K.Length] ^ ((i * 27 + 165) & 0xFF))).ToString("X2")).Append(i < p.Length - 1 ? "," : "");
        return sb.Append('}').ToString();
    }
#endif
}
