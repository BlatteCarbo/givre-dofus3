using System;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace GivreClone.App.Services.Transport.Socket;

/// <summary>
/// Vérification d'identité du serveur de JEU Dofus 3 = preuve de connaissance de Schnorr sur le groupe DH standard
/// RFC 2409 groupe 1 (MODP 768 bits, paramètres PUBLICS). Ce n'est PAS une signature anti-triche obfusquée : c'est
/// un protocole d'identification classique, exécutable en BigInteger pur. Portage fidèle de la logique de référence.
///
/// Séquence : ServerSessionReadyEvent → ClientIdRequest{ id = G^cvlg mod P }. ServerVerificationEvent → cvlh aléatoire
/// + ClientChallengeInitRequest{ ehcu = G^cvlh mod P }. ServerChallengeEvent{value} → ClientChallengeProofRequest{proof}.
/// cvlg est tiré UNE fois et réutilisé sur toute la poignée de main ; cvlh est régénéré au challenge init.
/// </summary>
internal sealed class SocketSchnorr
{
    // RFC 2409 / Oakley groupe 1, prime 768 bits (public, standard). "0" en tête -> BigInteger POSITIF.
    private const string PHex =
        "0FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD129024E088A67CC74" +
        "020BBEA63B139B22514A08798E3404DDEF9519B3CD3A431B302B0A6DF25F1437" +
        "4FE1356D6D51C245E485B576625E7EC6F44C42E9A63A3620FFFFFFFFFFFFFFFF";

    private static readonly BigInteger P = BigInteger.Parse(PHex, NumberStyles.HexNumber);
    private static readonly BigInteger G = 2;
    private static readonly BigInteger Q = (P - 1) / 2;   // ordre du sous-groupe (prime sûr p = 2q+1)

    private readonly BigInteger _cvlg;
    private BigInteger _cvlh;

    public SocketSchnorr()
    {
        var buf = new byte[0x400];                         // 1024 octets aléatoires
        RandomNumberGenerator.Fill(buf);
        _cvlg = new BigInteger(buf, isUnsigned: true, isBigEndian: false) % P;
    }

    /// <summary>ClientIdRequest.id = G^cvlg mod P (engagement public), en décimal.</summary>
    public string ClientId() => BigInteger.ModPow(G, _cvlg, P).ToString(CultureInfo.InvariantCulture);

    /// <summary>Régénère cvlh et renvoie ClientChallengeInitRequest.ehcu = G^cvlh mod P, en décimal.</summary>
    public string ChallengeInit()
    {
        var buf = new byte[50];
        RandomNumberGenerator.Fill(buf);
        _cvlh = new BigInteger(buf, isUnsigned: true, isBigEndian: false);
        return BigInteger.ModPow(G, _cvlh, P).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>ClientChallengeProofRequest.proof : réponse au défi serveur (ou Fiat-Shamir si value vide), en décimal.</summary>
    public string ChallengeProof(string? value, string hwid)
    {
        BigInteger modulo;
        if (!string.IsNullOrEmpty(value))
        {
            var serverValue = BigInteger.Parse(value, CultureInfo.InvariantCulture);
            modulo = (_cvlh + serverValue * _cvlg) % Q;
        }
        else
        {
            var b1 = BigInteger.ModPow(G, _cvlg, P);
            var b2 = BigInteger.ModPow(G, _cvlh, P);
            var concat = string.Concat(G.ToString(CultureInfo.InvariantCulture),
                                       b1.ToString(CultureInfo.InvariantCulture),
                                       b2.ToString(CultureInfo.InvariantCulture), hwid);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(concat));
            Array.Reverse(hash);
            var multiplied = new BigInteger(hash, isUnsigned: true, isBigEndian: false) * _cvlg;
            modulo = (_cvlh + multiplied) % Q;
        }
        if (modulo.Sign < 0) modulo += Q;
        return modulo.ToString(CultureInfo.InvariantCulture);
    }
}
